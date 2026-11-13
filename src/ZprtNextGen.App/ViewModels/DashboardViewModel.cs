#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ZprtNextGen.Core.Connection;
using ZprtNextGen.Core.Discord;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Зависимости <see cref="DashboardViewModel"/>.</summary>
public sealed record DashboardDependencies(
    ConnectionCoordinator Coordinator,
    IConfigStore ConfigStore,
    IDiscordFixService DiscordFix,
    IAppLogger Logger,
    IUiDispatcher Dispatcher,
    string LogsDirectory);

/// <summary>
/// Модель главного экрана «в один клик»: светофор, короткие статусы, Fix Discord,
/// донат и переход в настройки. Вся логика подключения живёт в <see cref="ConnectionCoordinator"/>.
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan FixSuccessDuration = TimeSpan.FromSeconds(3);

    private readonly ConnectionCoordinator _coordinator;
    private readonly IDiscordFixService _discordFix;
    private readonly IAppLogger _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly string _logsDirectory;
    private readonly AsyncRelayCommand _toggleCommand;
    private readonly AsyncRelayCommand _stopAndApplyCommand;
    private readonly AsyncRelayCommand _cancelSelectionCommand;
    private readonly AsyncRelayCommand _retryCommand;
    private readonly AsyncRelayCommand _fixDiscordCommand;
    private readonly RelayCommand _donateCommand;
    private readonly RelayCommand _openSettingsCommand;
    private readonly RelayCommand _openLogsCommand;

    private ConnectionSnapshot _snapshot;
    private AppConfig _config;
    private bool _isFixingDiscord;
    private string _fixDiscordText = "Fix Discord";

    public DashboardViewModel(DashboardDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _coordinator = dependencies.Coordinator ?? throw new ArgumentNullException(nameof(dependencies.Coordinator));
        _discordFix = dependencies.DiscordFix ?? throw new ArgumentNullException(nameof(dependencies.DiscordFix));
        _logger = dependencies.Logger ?? throw new ArgumentNullException(nameof(dependencies.Logger));
        _dispatcher = dependencies.Dispatcher ?? throw new ArgumentNullException(nameof(dependencies.Dispatcher));
        _logsDirectory = dependencies.LogsDirectory ?? string.Empty;

        _config = _coordinator.Config ?? new AppConfig();
        _snapshot = _coordinator.Snapshot;

        _toggleCommand = new AsyncRelayCommand(async _ => await ToggleAsync(), () => CanClickOrb);
        _stopAndApplyCommand = new AsyncRelayCommand(
            async _ => await _coordinator.StopAndApplyBestAsync(CancellationToken.None),
            () => CanStopAndApply);
        _cancelSelectionCommand = new AsyncRelayCommand(
            async _ => await _coordinator.CancelSelectionAsync(CancellationToken.None),
            () => CanCancelSelection);
        _retryCommand = new AsyncRelayCommand(
            async _ => await _coordinator.RetestAsync(CancellationToken.None),
            () => !IsBusy);
        _fixDiscordCommand = new AsyncRelayCommand(async _ => await FixDiscordAsync(), () => !_isFixingDiscord);
        _donateCommand = new RelayCommand(_ => OpenDonationPage());
        _openSettingsCommand = new RelayCommand(_ => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _openLogsCommand = new RelayCommand(_ => OpenLogs());

        _coordinator.StateChanged += OnStateChanged;
        _coordinator.Notification += OnNotification;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Пользователь просит открыть окно настроек.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Ошибка пользовательского уровня (одно окно на эпизод).</summary>
    public event EventHandler<string>? ErrorRaised;

    /// <summary>Короткое уведомление для трея (balloon).</summary>
    public event EventHandler<string>? NotificationRequested;

    public ConnectionState State => _snapshot.State;

    public ConnectionIssue Issue => _snapshot.Issue;

    public bool IsBusy => _snapshot.IsBusy;

    public bool CanClickOrb => State is ConnectionState.Disconnected
        or ConnectionState.Connected or ConnectionState.Error;

    public bool CanStopAndApply => State == ConnectionState.Selecting && _snapshot.HasQualifiedCandidate;

    public bool CanCancelSelection => State is ConnectionState.Selecting or ConnectionState.Connecting;

    public bool IsSelecting => State == ConnectionState.Selecting;

    public bool IsConnecting => State == ConnectionState.Connecting;

    public bool IsConnected => State == ConnectionState.Connected;

    public bool HasError => State == ConnectionState.Error;

    public bool IsEngineMissing => State == ConnectionState.Error && Issue == ConnectionIssue.EngineMissing;

    public double Progress => State is ConnectionState.Selecting or ConnectionState.Connecting
        ? Math.Clamp(_snapshot.Progress, 0, 1)
        : 0;

    /// <summary>Крупный текст внутри орба.</summary>
    public string OrbTitle => State switch
    {
        ConnectionState.Selecting => "Ищем обход…",
        ConnectionState.Connecting => "Подключаемся…",
        ConnectionState.Connected => "Обход включён",
        ConnectionState.Stopping => "Выключаем…",
        _ => "Включить",
    };

    /// <summary>Подпись под орбом.</summary>
    public string StatusTitle => State switch
    {
        ConnectionState.Disconnected => "Нажми для запуска YouTube и Discord",
        ConnectionState.Selecting => "Ищем самый быстрый обход",
        ConnectionState.Connecting => "Проверяем YouTube и Discord",
        ConnectionState.Connected => "Пользуйся YouTube и Discord в один клик",
        ConnectionState.Stopping => "Останавливаем обход",
        ConnectionState.Error => IssueText,
        _ => string.Empty,
    };

    /// <summary>Короткое пояснение причины проблемы.</summary>
    public string IssueText => Issue switch
    {
        ConnectionIssue.EngineMissing => "Движок не найден — открой настройки",
        ConnectionIssue.ForeignInstance => "Обход уже запущен извне. Останови его и попробуй снова.",
        ConnectionIssue.NoInternet => "Интернет не отвечает. Проверь подключение.",
        ConnectionIssue.AllPresetsFailed => "Не удалось найти рабочий обход. Попробуй снова.",
        ConnectionIssue.SavedProfileFailed => "Сохранённый обход перестал работать — ищем новый",
        _ => _snapshot.ErrorMessage ?? "Не удалось подключиться. Попробуй снова.",
    };

    /// <summary>Текущий профиль и задержки под орбом.</summary>
    public string ProfileText
    {
        get
        {
            if (State != ConnectionState.Connected)
                return string.Empty;

            var name = string.IsNullOrWhiteSpace(_snapshot.PresetName)
                ? "Подобранный обход"
                : _snapshot.PresetName;

            var latency = _snapshot.LatencyMs is { } ms ? $" • {ms} мс" : string.Empty;
            return $"Пресет: {name}{latency}";
        }
    }

    /// <summary>Строка прогресса подбора.</summary>
    public string ProgressText
    {
        get
        {
            if (State != ConnectionState.Selecting)
                return string.Empty;

            var text = $"Проверено {_snapshot.TestedCount} из {_snapshot.TotalCount}";
            if (!string.IsNullOrWhiteSpace(_snapshot.BestPresetName))
            {
                text += $" · Лучший: {_snapshot.BestPresetName}";
                if (_snapshot.BestLatencyMs is { } ms)
                    text += $" · {ms} мс";
            }

            return text;
        }
    }

    /// <summary>Плашка подбора видна только во время перебора.</summary>
    public bool IsBannerVisible => State == ConnectionState.Selecting;

    public string BannerTitle => Issue == ConnectionIssue.SavedProfileFailed
        ? "Сохранённый обход перестал работать"
        : "Ищем самый быстрый обход под твоего провайдера.";

    public string BannerText => Issue == ConnectionIssue.SavedProfileFailed
        ? "Это занимает пару минут. Мы автоматически подберём новый рабочий обход."
        : "Пожалуйста, подожди пару минут — это нужно сделать всего один раз.";

    /// <summary>Кнопка «Попробовать снова» на ошибке.</summary>
    public bool ShowRetry => HasError && Issue != ConnectionIssue.EngineMissing;

    /// <summary>Кнопка «Настройки» на ошибке движка.</summary>
    public bool ShowEngineSettingsHint => IsEngineMissing;

    public bool ReduceMotion => _config.Behavior.ReduceMotion;

    public bool MinimizeToTray => _config.Behavior.MinimizeToTray;

    public string OrbAutomationName => State switch
    {
        ConnectionState.Connected => "Обход включён, нажми чтобы выключить",
        ConnectionState.Selecting => "Идёт подбор обхода",
        ConnectionState.Connecting => "Идёт подключение",
        ConnectionState.Stopping => "Обход выключается",
        _ => "Включить обход",
    };

    public string OrbHint => State switch
    {
        ConnectionState.Connected => "Нажми, чтобы отключить обход",
        ConnectionState.Selecting or ConnectionState.Connecting => "Подожди, идёт подбор обхода",
        _ => "Нажми, чтобы подобрать и включить обход",
    };

    public string FixDiscordText
    {
        get => _fixDiscordText;
        private set => SetField(ref _fixDiscordText, value);
    }

    public bool IsFixingDiscord
    {
        get => _isFixingDiscord;
        private set
        {
            if (SetField(ref _isFixingDiscord, value))
                _fixDiscordCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand ToggleCommand => _toggleCommand;

    public ICommand StopAndApplyBestCommand => _stopAndApplyCommand;

    public ICommand CancelSelectionCommand => _cancelSelectionCommand;

    public ICommand RetryCommand => _retryCommand;

    public ICommand FixDiscordCommand => _fixDiscordCommand;

    public ICommand DonateCommand => _donateCommand;

    public ICommand OpenSettingsCommand => _openSettingsCommand;

    public ICommand OpenLogsCommand => _openLogsCommand;

    /// <summary>Перечитать конфиг после сохранения настроек.</summary>
    public void RefreshConfig()
    {
        _config = _coordinator.Config ?? new AppConfig();
        OnPropertyChanged(nameof(ReduceMotion));
        OnPropertyChanged(nameof(MinimizeToTray));
        Refresh();
    }

    /// <summary>Пересчитать все свойства из текущего снимка координатора.</summary>
    public void Refresh() => OnSnapshot(_coordinator.Snapshot);

    private async Task ToggleAsync()
    {
        if (IsEngineMissing)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!CanClickOrb)
            return;

        await _coordinator.ToggleAsync(CancellationToken.None);
    }

    private async Task FixDiscordAsync()
    {
        if (_isFixingDiscord)
            return;

        IsFixingDiscord = true;
        FixDiscordText = "Чиним Discord…";

        try
        {
            var result = await _discordFix.FixAsync(fullReset: false, CancellationToken.None);

            switch (result.Status)
            {
                case DiscordFixStatus.Success:
                    FixDiscordText = "Готово ✓";
                    NotificationRequested?.Invoke(this, "Discord починен. Кэш очищен, аккаунт на месте.");
                    _ = ResetFixTextLaterAsync();
                    break;
                case DiscordFixStatus.NotInstalled:
                    FixDiscordText = "Discord не найден";
                    ErrorRaised?.Invoke(this, "Discord не найден на этом компьютере.");
                    break;
                default:
                    FixDiscordText = "Не удалось";
                    ErrorRaised?.Invoke(this,
                        result.ErrorMessage ?? "Кэш очищен, но Discord не перезапустился. Запусти его вручную.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Fix Discord: " + ex.Message, ex);
            FixDiscordText = "Не удалось";
            ErrorRaised?.Invoke(this, "Не удалось починить Discord. Подробности в журнале.");
        }
        finally
        {
            IsFixingDiscord = false;
        }
    }

    private async Task ResetFixTextLaterAsync()
    {
        try
        {
            await Task.Delay(FixSuccessDuration).ConfigureAwait(false);
            _dispatcher.Post(() => FixDiscordText = "Fix Discord");
        }
        catch (Exception)
        {
            // Сброс подписи — вспомогательное действие.
        }
    }

    private void OpenDonationPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.donationalerts.com/r/benny_chip")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось открыть ссылку доната: " + ex.Message, ex);
            ErrorRaised?.Invoke(this, "Не удалось открыть браузер.");
        }
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            Process.Start(new ProcessStartInfo(_logsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось открыть папку логов: " + ex.Message, ex);
            ErrorRaised?.Invoke(this, "Не удалось открыть папку логов.");
        }
    }

    private void OnStateChanged(object? sender, ConnectionSnapshot snapshot) =>
        _dispatcher.Post(() => OnSnapshot(snapshot));

    private void OnNotification(object? sender, string message) =>
        _dispatcher.Post(() => NotificationRequested?.Invoke(this, message));

    private void OnSnapshot(ConnectionSnapshot snapshot)
    {
        _snapshot = snapshot;
        _config = _coordinator.Config ?? _config;

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Issue));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanClickOrb));
        OnPropertyChanged(nameof(CanStopAndApply));
        OnPropertyChanged(nameof(CanCancelSelection));
        OnPropertyChanged(nameof(IsSelecting));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEngineMissing));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(OrbTitle));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(IssueText));
        OnPropertyChanged(nameof(ProfileText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(IsBannerVisible));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(ShowEngineSettingsHint));
        OnPropertyChanged(nameof(OrbAutomationName));
        OnPropertyChanged(nameof(OrbHint));
        OnPropertyChanged(nameof(ReduceMotion));

        _toggleCommand.RaiseCanExecuteChanged();
        _stopAndApplyCommand.RaiseCanExecuteChanged();
        _cancelSelectionCommand.RaiseCanExecuteChanged();
        _retryCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
