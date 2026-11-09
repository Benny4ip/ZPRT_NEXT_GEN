#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Presentation;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;
using ZprtNextGen.Core.Startup;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Зависимости <see cref="MainViewModel"/>.</summary>
public record MainViewModelDependencies(
    PresetCatalog Catalog,
    ISettingsStore Settings,
    EngineValidation Engine,
    IEngineProcessManager EngineProcess,
    IConnectivityService Connectivity,
    IAutostartService Autostart,
    IAppLogger Logger,
    IUiDispatcher Dispatcher,
    Func<string, bool> Confirm,
    Func<string?, EngineValidation>? LocateEngine = null);

/// <summary>Сервисы обслуживания: обновления и тестирование пресетов.</summary>
public record MaintenanceDependencies(
    IGitHubReleaseClient GitHub,
    IEngineUpdater EngineUpdater,
    IAppUpdater AppUpdater,
    IPresetTester PresetTester,
    string AppVersion,
    string PresetsPath);

/// <summary>
/// Модель представления главного окна: тонкая обёртка над асинхронными методами
/// collaborators. Весь пользовательский текст — на русском языке.
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const int MaxLogLines = 500;

    private PresetCatalog _catalog;
    private readonly ISettingsStore _settingsStore;
    private readonly IEngineProcessManager _engineProcess;
    private readonly IConnectivityService _connectivity;
    private readonly IAutostartService _autostart;
    private readonly IAppLogger _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<string, bool> _confirm;
    private readonly Func<string?, EngineValidation> _locateEngine;

    private readonly AsyncRelayCommand _toggleCommand;
    private readonly AsyncRelayCommand _testConnectionCommand;
    private readonly RelayCommand _selectPresetCommand;
    private readonly RelayCommand _openLogsCommand;
    private readonly AsyncRelayCommand _toggleAutostartCommand;
    private readonly RelayCommand _exitCommand;
    private readonly AsyncRelayCommand _checkUpdatesCommand;
    private readonly AsyncRelayCommand _updateEngineCommand;
    private readonly AsyncRelayCommand _updateAppCommand;
    private readonly AsyncRelayCommand _testAllPresetsCommand;
    private readonly RelayCommand _cancelPresetTestCommand;
    private readonly RelayCommand _applyPresetCommand;
    private readonly RelayCommand _openGitHubCommand;
    private readonly MaintenanceDependencies? _maintenance;

    private AppSettings _settings = new();
    private EngineValidation _engine = new(false, null, Array.Empty<string>());
    private HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private bool _isBusy;
    private bool _canStart;
    private bool _isAutostartEnabled;
    private bool _minimizeToTray = true;
    private bool _reduceMotion;
    private string _enginePath = string.Empty;
    private GameFilterMode _gameFilter;
    private string _statusText = string.Empty;
    private MeditationState _animationState = MeditationState.Stopped;
    private Preset? _selectedPreset;
    private readonly ObservableCollection<string> _logLines = new();
    private IReadOnlyList<ConnectivityRowViewModel> _connectivityRows = Array.Empty<ConnectivityRowViewModel>();
    private IReadOnlyList<PresetGroupViewModel> _groups = Array.Empty<PresetGroupViewModel>();

    private string _updateStatusText = string.Empty;
    private double _updateProgress;
    private bool _isMaintenanceBusy;
    private string _presetTestStatus = string.Empty;
    private double _presetTestProgress;
    private bool _isTestingPresets;
    private CancellationTokenSource? _testCts;

    public MainViewModel(MainViewModelDependencies dependencies, MaintenanceDependencies? Maintenance = null)
    {
        _catalog = dependencies.Catalog ?? throw new ArgumentNullException(nameof(dependencies.Catalog));
        _settingsStore = dependencies.Settings ?? throw new ArgumentNullException(nameof(dependencies.Settings));
        _engine = dependencies.Engine;
        _engineProcess = dependencies.EngineProcess ?? throw new ArgumentNullException(nameof(dependencies.EngineProcess));
        _connectivity = dependencies.Connectivity ?? throw new ArgumentNullException(nameof(dependencies.Connectivity));
        _autostart = dependencies.Autostart ?? throw new ArgumentNullException(nameof(dependencies.Autostart));
        _logger = dependencies.Logger ?? throw new ArgumentNullException(nameof(dependencies.Logger));
        _dispatcher = dependencies.Dispatcher ?? throw new ArgumentNullException(nameof(dependencies.Dispatcher));
        _confirm = dependencies.Confirm ?? throw new ArgumentNullException(nameof(dependencies.Confirm));
        _locateEngine = dependencies.LocateEngine ?? (path => EngineLocator.Locate(path, AppContext.BaseDirectory));
        _maintenance = Maintenance;

        _toggleCommand = new AsyncRelayCommand(
            async _ => await ToggleAsync(),
            () => IsRunning || (!IsBusy && CanStart));
        _testConnectionCommand = new AsyncRelayCommand(async p => await TestConnectionAsync(p as string), () => !IsBusy);
        _selectPresetCommand = new RelayCommand(OnSelectPreset, _ => !IsBusy);
        _openLogsCommand = new RelayCommand(_ => OnOpenLogs());
        _toggleAutostartCommand = new AsyncRelayCommand(async _ => await ToggleAutostartAsync(), () => !IsBusy);
        _exitCommand = new RelayCommand(_ => Application.Current?.Shutdown(), _ => Application.Current is not null);
        _checkUpdatesCommand = new AsyncRelayCommand(async _ => await CheckUpdatesAsync(), () => !IsMaintenanceBusy);
        _updateEngineCommand = new AsyncRelayCommand(async _ => await UpdateEngineAsync(), () => !IsMaintenanceBusy && CanStart);
        _updateAppCommand = new AsyncRelayCommand(async _ => await UpdateAppAsync(), () => !IsMaintenanceBusy);
        _testAllPresetsCommand = new AsyncRelayCommand(async _ => await TestAllPresetsAsync(), () => !IsTestingPresets);
        _cancelPresetTestCommand = new RelayCommand(_ => CancelTest(), _ => IsTestingPresets);
        _applyPresetCommand = new RelayCommand(p => ApplyPreset(p as string));
        _openGitHubCommand = new RelayCommand(_ => OnOpenGitHub());

        _engineProcess.OutputReceived += OnOutputReceived;
        _engineProcess.StatusChanged += OnEngineStatusChanged;
    }

    public ICommand ToggleCommand => _toggleCommand;

    public ICommand TestConnectionCommand => _testConnectionCommand;

    public ICommand SelectPresetCommand => _selectPresetCommand;

    public ICommand OpenLogsCommand => _openLogsCommand;

    public ICommand ToggleAutostartCommand => _toggleAutostartCommand;

    public ICommand ExitCommand => _exitCommand;

    /// <summary>Событие ошибки пользовательского уровня (одно окно на эпизод).</summary>
    public event EventHandler<string>? ErrorRaised;

    private string _lastErrorMessage = string.Empty;

    /// <summary>Последнее сообщение ошибки, переданное через <see cref="ErrorRaised"/>.</summary>
    public string LastErrorMessage => _lastErrorMessage;

    public ICommand CheckUpdatesCommand => _checkUpdatesCommand;

    public ICommand UpdateEngineCommand => _updateEngineCommand;

    public ICommand UpdateAppCommand => _updateAppCommand;

    public ICommand TestAllPresetsCommand => _testAllPresetsCommand;

    public ICommand CancelPresetTestCommand => _cancelPresetTestCommand;

    /// <summary>Применить выбранный пресет по идентификатору (из результатов теста альтов).</summary>
    public ICommand ApplyPresetCommand => _applyPresetCommand;

    /// <summary>Открыть релизы ZPRT_NEXT_GEN на GitHub (резервный путь при недоступности API).</summary>
    public ICommand OpenGitHubCommand => _openGitHubCommand;

    /// <summary>Версия оболочки (из <see cref="MaintenanceDependencies.AppVersion"/>).</summary>
    public string AppVersion => _maintenance?.AppVersion ?? string.Empty;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetField(ref _updateStatusText, value);
    }

    public double UpdateProgress
    {
        get => _updateProgress;
        private set => SetField(ref _updateProgress, value);
    }

    public bool IsMaintenanceBusy
    {
        get => _isMaintenanceBusy;
        private set => SetField(ref _isMaintenanceBusy, value);
    }

    public ObservableCollection<PresetTestResult> PresetTestResults { get; } = new();

    public string PresetTestStatus
    {
        get => _presetTestStatus;
        private set => SetField(ref _presetTestStatus, value);
    }

    public double PresetTestProgress
    {
        get => _presetTestProgress;
        private set => SetField(ref _presetTestProgress, value);
    }

    public bool IsTestingPresets
    {
        get => _isTestingPresets;
        private set => SetField(ref _isTestingPresets, value);
    }

    /// <summary>Событие: требуется перезапуск программы после обновления самой оболочки.</summary>
    public event EventHandler? RestartRequested;

    public IReadOnlyList<PresetGroupViewModel> Groups
    {
        get => _groups;
        private set => SetField(ref _groups, value);
    }

    public Preset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetField(ref _selectedPreset, value))
            {
                _settings = _settings with { PresetId = value?.Id ?? _settings.PresetId };
                SaveSettings();
            }
        }
    }

    public bool IsRunning => _engineProcess.Status == EngineStatus.Running;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RefreshCommandStates();
        }
    }

    public bool CanStart
    {
        get => _canStart;
        private set
        {
            if (SetField(ref _canStart, value))
                RefreshCommandStates();
        }
    }

    public bool IsAutostartEnabled
    {
        get => _isAutostartEnabled;
        set
        {
            if (SetField(ref _isAutostartEnabled, value))
            {
                _settings = _settings with { Autostart = value };
                SaveSettings();
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetField(ref _minimizeToTray, value))
            {
                _settings = _settings with { MinimizeToTray = value };
                SaveSettings();
            }
        }
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (SetField(ref _reduceMotion, value))
            {
                _settings = _settings with { ReduceMotion = value };
                SaveSettings();
            }
        }
    }

    public string EnginePath
    {
        get => _enginePath;
        set => SetField(ref _enginePath, value);
    }

    public GameFilterMode GameFilter
    {
        get => _gameFilter;
        set
        {
            if (SetField(ref _gameFilter, value))
            {
                _settings = _settings with { GameFilter = value };
                SaveSettings();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    /// <summary>Версия движка из каталога пресетов.</summary>
    public string EngineVersion => _catalog.EngineVersion;

    public MeditationState AnimationState
    {
        get => _animationState;
        set => SetField(ref _animationState, value);
    }

    public ObservableCollection<string> LogLines => _logLines;

    public IReadOnlyList<ConnectivityRowViewModel> ConnectivityRows
    {
        get => _connectivityRows;
        private set => SetField(ref _connectivityRows, value);
    }

    /// <inheritdoc cref="InitializeAsync"/>
    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            _settings = _settingsStore.Load();
            _collapsedGroups = new HashSet<string>(_settings.CollapsedGroups, StringComparer.Ordinal);

            Groups = _catalog.Groups
                .Select(group =>
                {
                    var groupPresets = _catalog.Presets
                        .Where(preset => preset.GroupId == group.Id)
                        .ToList();
                    var groupVm = new PresetGroupViewModel(group.Id, group.Name, groupPresets, _collapsedGroups);
                    groupVm.PropertyChanged += OnGroupIsExpandedChanged;
                    return groupVm;
                })
                .ToList();

            SelectedPreset = _catalog.Presets
                .FirstOrDefault(preset => preset.Id.Equals(_settings.PresetId, StringComparison.OrdinalIgnoreCase))
                ?? _catalog.Presets.FirstOrDefault();

            EnginePath = _settings.EnginePath ?? string.Empty;
            GameFilter = _settings.GameFilter;
            IsAutostartEnabled = _autostart.IsEnabled();
            MinimizeToTray = _settings.MinimizeToTray;
            ReduceMotion = _settings.ReduceMotion;

            CanStart = _engine.IsValid;

            if (!_engine.IsValid)
            {
                StatusText = "Движок не найден: " + string.Join(", ", _engine.MissingFiles);
            }
            else if (_engineProcess.IsForeignInstanceRunning())
            {
                StatusText = "Обход запущен извне";
                AnimationState = MeditationState.Connecting;
            }
            else
            {
                AnimationState = MeditationState.Stopped;
                StatusText = "Готов к запуску.";
            }

            // Автопроверка обновлений движка при включении приложения.
            if (_engine.IsValid)
                _ = CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="ToggleAsync"/>
    public async Task ToggleAsync()
    {
        // Остановка доступна всегда, даже пока идёт проверка связи после запуска.
        if (IsRunning)
        {
            try
            {
                IsBusy = true;
                await _engineProcess.StopAsync(CancellationToken.None);
                AnimationState = MeditationState.Stopped;
                StatusText = "Движок остановлен";
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        if (IsBusy || !CanStart)
            return;

        try
        {
            IsBusy = true;

            if (_engineProcess.IsForeignInstanceRunning())
            {
                if (!_confirm(
                        "Обход уже запущен другим способом. Остановить его и запустить обход из этого приложения?"))
                {
                    StatusText = "Обход уже запущен извне.";
                    return;
                }

                _engineProcess.KillForeignInstances();

                // Ждём, пока чужой процесс действительно завершится (обычно < 300 мс),
                // иначе winws не сможет захватить WinDivert.
                for (var attempt = 0; attempt < 30 && _engineProcess.IsForeignInstanceRunning(); attempt++)
                    await Task.Delay(100);
            }

            var preset = _selectedPreset
                ?? throw new InvalidOperationException("Пресет не выбран.");
            var root = _engine.EngineRoot
                ?? throw new InvalidOperationException("Корень движка не определён.");

            var arguments = PresetArgumentBuilder.Build(preset, root, _settings.GameFilter);
            var binDirectory = Path.Combine(root, "bin");
            var startTask = _engineProcess.StartAsync(
                Path.Combine(binDirectory, "winws.exe"),
                arguments,
                binDirectory,
                CancellationToken.None);
            await startTask.WaitAsync(TimeSpan.FromSeconds(10));
            StatusText = "Обход включён";

            // Проверку связи не ждём: иначе кнопка «Выключить обход» остаётся
            // недоступной до её окончания (до 10 секунд).
            _ = TestConnectionAsync();
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="TestConnectionAsync"/>
    public async Task TestConnectionAsync(string? targetId = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var report = await _connectivity.CheckAsync(cts.Token, targetId);

            _dispatcher.Post(() =>
            {
                if (targetId is not null)
                {
                    // Перепроверяем только одну цель: обновляем её строку, остальные не трогаем.
                    var result = report.Results.FirstOrDefault();
                    if (result is not null)
                    {
                        var existing = ConnectivityRows.FirstOrDefault(
                            r => r.TargetId.Equals(result.Target.Id, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            existing.Success = result.Success;
                            existing.ElapsedMs = result.ElapsedMs;
                            existing.Error = result.Error;
                        }
                        else
                        {
                            ConnectivityRows = ConnectivityRows.Append(new ConnectivityRowViewModel(
                                result.Target.Id, result.Target.DisplayName, result.Success,
                                result.ElapsedMs, result.Error)).ToList();
                        }
                    }

                    if (_engineProcess.Status == EngineStatus.Running)
                    {
                        var single = new ConnectivityReport(report.Results);
                        AnimationState = AnimationStateResolver.Resolve(_engineProcess.Status, single);
                        StatusText = single.IsConnected ? "Связь подтверждена" : "Связь не подтверждена";
                    }
                    return;
                }

                ConnectivityRows = report.Results
                    .Select(result => new ConnectivityRowViewModel
                    {
                        TargetId = result.Target.Id,
                        Name = result.Target.DisplayName,
                        Success = result.Success,
                        ElapsedMs = result.ElapsedMs,
                        Error = result.Error
                    })
                    .ToList();

                // Если движок уже остановили, не перетираем статус остановки результатами связи.
                if (_engineProcess.Status != EngineStatus.Running)
                {
                    AnimationState = AnimationStateResolver.Resolve(_engineProcess.Status, null);
                    return;
                }

                AnimationState = AnimationStateResolver.Resolve(_engineProcess.Status, report);
                StatusText = report.IsConnected ? "Связь подтверждена" : "Связь не подтверждена";
            });
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    /// <inheritdoc cref="ChangeEnginePathAsync"/>
    public async Task ChangeEnginePathAsync(string path)
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            var result = _locateEngine(path);

            if (result.IsValid)
            {
                _engine = result;
                EnginePath = path;
                CanStart = true;
                _settings = _settings with { EnginePath = path };
                SaveSettings();
                StatusText = "Движок найден";
            }
            else
            {
                StatusText = "Папка не подходит: " + string.Join(", ", result.MissingFiles);
            }
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc cref="ShutdownAsync"/>
    public async Task ShutdownAsync(bool stopEngine)
    {
        try
        {
            if (stopEngine && IsRunning)
            {
                await _engineProcess.StopAsync(CancellationToken.None);
                AnimationState = MeditationState.Stopped;
                StatusText = "Движок остановлен";
            }

            _engineProcess.OutputReceived -= OnOutputReceived;
            _engineProcess.StatusChanged -= OnEngineStatusChanged;
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RefreshCommandStates()
    {
        _toggleCommand.RaiseCanExecuteChanged();
        _testConnectionCommand.RaiseCanExecuteChanged();
        _selectPresetCommand.RaiseCanExecuteChanged();
        _toggleAutostartCommand.RaiseCanExecuteChanged();
        _checkUpdatesCommand.RaiseCanExecuteChanged();
        _updateEngineCommand.RaiseCanExecuteChanged();
        _updateAppCommand.RaiseCanExecuteChanged();
        _testAllPresetsCommand.RaiseCanExecuteChanged();
        _cancelPresetTestCommand.RaiseCanExecuteChanged();
    }

    private void SaveSettings() => _settingsStore.Save(_settings);

    private void HandleError(Exception ex)
    {
        _logger.Error(ex.Message, ex);
        _lastErrorMessage = ex.Message;
        AnimationState = MeditationState.Error;
        StatusText = $"Ошибка: {ex.Message}";
        ErrorRaised?.Invoke(this, ex.Message);
    }

    /// <summary>Проверить обновления движка и программы через GitHub Releases.</summary>
    public async Task CheckUpdatesAsync()
    {
        if (_maintenance is null)
        {
            UpdateStatusText = "Проверка обновлений недоступна.";
            return;
        }

        IsMaintenanceBusy = true;
        try
        {
            UpdateProgress = 0;

            var engineLatest = await _maintenance.GitHub.GetLatestAsync(
                "Flowseal/zapret-discord-youtube", CancellationToken.None);
            UpdateProgress = 50;

            var appLatest = await _maintenance.GitHub.GetLatestAsync(
                "Benny4ip/ZPRT_NEXT_GEN", CancellationToken.None);
            UpdateProgress = 100;

            var text = string.Empty;
            if (engineLatest is not null)
            {
                var current = _catalog.EngineVersion;
                if (VersionComparer.IsNewer(engineLatest.Tag, current))
                    text += $"Доступна версия движка {engineLatest.Tag} (установлена {current}). ";
                else
                    text += $"Движок актуален (v{current}). ";
            }
            else
            {
                text += "Не удалось проверить движок: GitHub недоступен. ";
            }

            if (appLatest is not null)
            {
                if (VersionComparer.IsNewer(appLatest.Tag, AppVersion))
                    text += $"Доступна версия программы {appLatest.Tag} (установлена v{AppVersion}).";
                else
                    text += $"Программа актуальна (v{AppVersion}).";
            }
            else
            {
                text += "Не удалось проверить программу: GitHub недоступен.";
            }

            UpdateStatusText = text.Trim();
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message, ex);
            UpdateStatusText = "Ошибка проверки обновлений: " + ex.Message;
        }
        finally
        {
            IsMaintenanceBusy = false;
            UpdateProgress = 0;
        }
    }

    /// <summary>Обновить движок (запрет) с GitHub с окном прогресса и автоустановкой.</summary>
    public async Task UpdateEngineAsync()
    {
        if (_maintenance is null || !_engine.IsValid || _engine.EngineRoot is null)
        {
            UpdateStatusText = "Движок не найден или некорректен.";
            return;
        }
        if (_engineProcess.Status == EngineStatus.Running)
        {
            UpdateStatusText = "Остановите движок перед обновлением.";
            return;
        }

        var progressWindow = new EngineUpdateProgressWindow
        {
            Topmost = true,
        };
        progressWindow.Show();

        try
        {
            progressWindow.SetStatus("Проверка обновлений…");

            var release = await _maintenance.GitHub.GetLatestAsync(
                "Flowseal/zapret-discord-youtube", CancellationToken.None);
            if (release is null)
            {
                progressWindow.SetStatus("Не удалось проверить обновления: GitHub недоступен.");
                await Task.Delay(2000);
                progressWindow.Close();
                return;
            }

            if (!VersionComparer.IsNewer(release.Tag, _catalog.EngineVersion))
            {
                progressWindow.SetStatus($"Уже последняя версия (v{_catalog.EngineVersion}).");
                await Task.Delay(2000);
                progressWindow.Close();
                return;
            }

            progressWindow.SetStatus($"Скачивание и установка v{release.Tag}…");

            var result = await _maintenance.EngineUpdater.UpdateAsync(
                release,
                _engine.EngineRoot,
                _maintenance.PresetsPath,
                () => _engineProcess.StopAsync(CancellationToken.None),
                new Progress<string>(msg => progressWindow.SetStatus(msg)),
                CancellationToken.None);

            progressWindow.Close();

            UpdateStatusText = result.Message
                ?? $"Движок обновлён до v{result.NewVersion} ({result.PresetCount} пресетов).";
            await ReloadCatalogAsync();
        }
        catch (Exception ex)
        {
            try { progressWindow.Close(); } catch { }
            _logger.Error(ex.Message, ex);
            UpdateStatusText = "Ошибка обновления движка: " + ex.Message;
        }
    }

    /// <summary>Обновить саму программу с GitHub (перезапуск через скрипт после выхода).</summary>
    public async Task UpdateAppAsync()
    {
        if (_maintenance is null)
        {
            UpdateStatusText = "Обновление программы недоступно.";
            return;
        }

        IsMaintenanceBusy = true;
        try
        {
            var release = await _maintenance.GitHub.GetLatestAsync("Benny4ip/ZPRT_NEXT_GEN", CancellationToken.None);
            if (release is null)
            {
                UpdateStatusText = "Не удалось проверить обновления программы: GitHub недоступен.";
                return;
            }
            if (!VersionComparer.IsNewer(release.Tag, AppVersion))
            {
                UpdateStatusText = $"Программа уже актуальна (v{AppVersion}).";
                return;
            }

            var plan = await _maintenance.AppUpdater.PrepareAsync(
                release, AppContext.BaseDirectory, null, CancellationToken.None);

            // Сначала запускаем скрипт: он дождётся выхода этого процесса,
            // заменит .exe и запустит новую версию.
            bool launched = false;
            try
            {
                _maintenance.AppUpdater.Launch(plan);
                launched = true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Не удалось запустить скрипт обновления программы: {ex}", ex);
                UpdateStatusText = "Ошибка запуска обновления программы: " + ex.Message;
            }

            if (launched)
            {
                UpdateStatusText = $"Обновление программы до v{release.Tag} подготовлено. Перезапуск…";
                RestartRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message, ex);
            UpdateStatusText = "Ошибка обновления программы: " + ex.Message;
        }
        finally
        {
            IsMaintenanceBusy = false;
            UpdateProgress = 0;
        }
    }

    /// <summary>Последовательно протестировать все альтернативные пресеты.</summary>
    public async Task TestAllPresetsAsync()
    {
        if (_maintenance is null || _engine.EngineRoot is null)
        {
            PresetTestStatus = "Движок не найден.";
            return;
        }

        IsTestingPresets = true;
        PresetTestResults.Clear();
        PresetTestStatus = string.Empty;
        PresetTestProgress = 0;
        _testCts = new CancellationTokenSource();

        try
        {
            var presets = _catalog.Presets
                .Where(p => p.GroupId == "general" && p.Id.StartsWith("general-alt", StringComparison.Ordinal))
                .ToList();
            if (presets.Count == 0)
            {
                PresetTestStatus = "Альтернативные пресеты не найдены.";
                return;
            }

            var results = await _maintenance.PresetTester.TestAllAsync(
                presets,
                _engine.EngineRoot,
                GameFilter,
                new Progress<PresetTestProgress>(p =>
                {
                    PresetTestProgress = (double)p.Index / p.Total * 100;
                }),
                _testCts.Token);

            PresetTestResults.Clear();
            foreach (var result in results.OrderBy(r => r.ElapsedMs))
                PresetTestResults.Add(result);

            var ok = results.Count(r => r.Success);
            PresetTestStatus = $"Проверено {results.Count}, успешно {ok}.";
        }
        catch (OperationCanceledException)
        {
            PresetTestStatus = "Проверка отменена пользователем.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message, ex);
            PresetTestStatus = "Ошибка проверки пресетов: " + ex.Message;
        }
        finally
        {
            IsTestingPresets = false;
            _testCts = null;
        }
    }

    /// <summary>Отменить текущее тестирование пресетов.</summary>
    private void CancelTest() => _testCts?.Cancel();

    /// <summary>Применить пресет по идентификатору (выбрать его как активный).</summary>
    private void ApplyPreset(string? presetId)
    {
        if (string.IsNullOrEmpty(presetId))
            return;

        var preset = _catalog.Presets
            .FirstOrDefault(p => p.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            SelectedPreset = preset;
        }
    }

    /// <summary>Открыть релизы ZPRT_NEXT_GEN на GitHub в браузере (резервный путь при 504/недоступности API).</summary>
    private void OnOpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/Benny4ip/ZPRT_NEXT_GEN/releases")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось открыть GitHub releases: " + ex.Message, ex);
            MessageBox.Show(
                "Не удалось открыть ссылку в браузере: " + ex.Message,
                "ZPRT NEXT GEN",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>Перезагрузить каталог пресетов с диска (сохраняя выбранный пресет).</summary>
    public async Task ReloadCatalogAsync()
    {
        if (_maintenance is null || string.IsNullOrEmpty(_maintenance.PresetsPath))
            return;

        var previousId = _selectedPreset?.Id;
        _catalog = PresetCatalogLoader.Load(_maintenance.PresetsPath);

        var groups = new List<PresetGroupViewModel>();
        foreach (var group in _catalog.Groups)
        {
            var groupPresets = _catalog.Presets.Where(p => p.GroupId == group.Id).ToList();
            var groupVm = new PresetGroupViewModel(group.Id, group.Name, groupPresets, _collapsedGroups);
            groupVm.PropertyChanged += OnGroupIsExpandedChanged;
            groups.Add(groupVm);
        }
        Groups = groups;

        SelectedPreset = _catalog.Presets
            .FirstOrDefault(p => p.Id.Equals(previousId, StringComparison.OrdinalIgnoreCase))
            ?? _catalog.Presets.FirstOrDefault();
    }

    private void OnEngineStatusChanged(object? sender, EngineStatusChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // IsRunning — вычисляемое свойство: без явного уведомления кнопка
            // остаётся «Включить обход», хотя движок уже запущен.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            RefreshCommandStates();

            switch (e.Status)
            {
                case EngineStatus.Running:
                    StatusText = "Обход включён";
                    break;
                case EngineStatus.Stopped:
                    AnimationState = MeditationState.Stopped;
                    StatusText = "Движок остановлен";
                    break;
                case EngineStatus.Error:
                    AnimationState = MeditationState.Error;
                    if (!string.IsNullOrWhiteSpace(e.Detail))
                        StatusText = "Ошибка движка: " + e.Detail;
                    break;
            }
        });
    }

    private void OnOutputReceived(object? sender, string line)
    {
        _dispatcher.Post(() =>
        {
            _logLines.Add(line ?? string.Empty);
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);
        });
    }

    private void OnSelectPreset(object? parameter)
    {
        if (parameter is Preset preset)
            SelectedPreset = preset;
    }

    private void OnGroupIsExpandedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(PresetGroupViewModel.IsExpanded) ||
            sender is not PresetGroupViewModel group)
            return;

        if (group.IsExpanded)
            _collapsedGroups.Remove(group.Id);
        else
            _collapsedGroups.Add(group.Id);

        _settings = _settings with { CollapsedGroups = [.. _collapsedGroups] };
        SaveSettings();
    }

    private void OnOpenLogs()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsStore.Path);
            var logDirectory = Path.Combine(directory ?? string.Empty, "logs");
            Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private async Task ToggleAutostartAsync()
    {
        var enable = !_isAutostartEnabled;

        try
        {
            if (enable)
                _autostart.Enable();
            else
                _autostart.Disable();

            IsAutostartEnabled = enable;
            _settings = _settings with { Autostart = enable };
            SaveSettings();
        }
        catch (Exception ex)
        {
            IsAutostartEnabled = !enable;
            _settings = _settings with { Autostart = IsAutostartEnabled };
            SaveSettings();
            HandleError(ex);
        }
    }
}

