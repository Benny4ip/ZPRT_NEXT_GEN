﻿#nullable enable

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
using System.Windows.Input;
using ZprtNextGen.Core.Connection;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Discord;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;
using ZprtNextGen.Core.Startup;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Зависимости <see cref="SettingsViewModel"/>.</summary>
public sealed record SettingsDependencies(
    IConfigStore ConfigStore,
    ConnectionCoordinator Coordinator,
    PresetCatalog BaseCatalog,
    IEngineProcessManager EngineProcess,
    IConnectivityService Connectivity,
    IAutostartService Autostart,
    IDiscordFixService DiscordFix,
    IPresetTester PresetTester,
    IGitHubReleaseClient GitHub,
    IEngineUpdater EngineUpdater,
    IAppUpdater AppUpdater,
    IAppLogger Logger,
    IUiDispatcher Dispatcher,
    string AppVersion,
    string PresetsPath,
    bool IsPortable,
    Func<string?, EngineValidation> LocateEngine,
    Action<PresetCatalog> ApplyCatalog,
    Func<string, bool> Confirm,
    string LogsDirectory);

/// <summary>
/// Настройки: как подключаться, игровой профиль, мои пресеты, запуск и трей,
/// движок и обновления, диагностика. Сохраняет config.json сразу при изменении.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IConfigStore _configStore;
    private readonly ConnectionCoordinator _coordinator;
    private readonly PresetCatalog _baseCatalog;
    private readonly IEngineProcessManager _engineProcess;
    private readonly IConnectivityService _connectivity;
    private readonly IAutostartService _autostart;
    private readonly IDiscordFixService _discordFix;
    private readonly IPresetTester _presetTester;
    private readonly IGitHubReleaseClient _gitHub;
    private readonly IEngineUpdater _engineUpdater;
    private readonly IAppUpdater _appUpdater;
    private readonly IAppLogger _logger;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<string?, EngineValidation> _locateEngine;
    private readonly Action<PresetCatalog> _applyCatalog;
    private readonly Func<string, bool> _confirm;
    private readonly string _logsDirectory;

    private readonly AsyncRelayCommand _testAllPresetsCommand;
    private readonly RelayCommand _cancelTestCommand;
    private readonly AsyncRelayCommand _usePresetCommand;
    private readonly RelayCommand _makeAutomaticCommand;
    private readonly AsyncRelayCommand _toggleAutostartCommand;
    private readonly AsyncRelayCommand _checkUpdatesCommand;
    private readonly AsyncRelayCommand _updateEngineCommand;
    private readonly AsyncRelayCommand _updateAppCommand;
    private readonly RelayCommand _openGitHubCommand;
    private readonly AsyncRelayCommand _checkConnectivityCommand;
    private readonly AsyncRelayCommand _fullDiscordResetCommand;
    private readonly RelayCommand _openLogsCommand;
    private readonly AsyncRelayCommand _restartConnectionCommand;

    private AppConfig _config;
    private PresetCatalog _catalog;
    private GameFilterMode _lastDetailedFilter = GameFilterMode.Disabled;
    private bool _initializing;
    private bool _isAutoMode;
    private Preset? _selectedManualPreset;
    private bool _isGameProfileEnabled;
    private GameFilterMode _gameFilter;
    private string _enginePath = string.Empty;
    private string _engineStatusText = string.Empty;
    private bool _isAutostartEnabled;
    private bool _minimizeToTray;
    private bool _reduceMotion;
    private string _saveStatus = "Настройки сохраняются автоматически";
    private bool _needsRestart;
    private string _updateStatusText = string.Empty;
    private double _updateProgress;
    private bool _isMaintenanceBusy;
    private string _presetTestStatus = string.Empty;
    private double _presetTestProgress;
    private bool _isTestingPresets;
    private CancellationTokenSource? _testCts;
    private string _connectivityStatus = string.Empty;
    private bool _isCheckingConnectivity;
    private IReadOnlyList<ConnectivityRowViewModel> _connectivityRows = [];
    private bool _isFixingDiscord;
    private string _discordStatus = string.Empty;

    public SettingsViewModel(SettingsDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _configStore = dependencies.ConfigStore;
        _coordinator = dependencies.Coordinator;
        _baseCatalog = dependencies.BaseCatalog;
        _engineProcess = dependencies.EngineProcess;
        _connectivity = dependencies.Connectivity;
        _autostart = dependencies.Autostart;
        _discordFix = dependencies.DiscordFix;
        _presetTester = dependencies.PresetTester;
        _gitHub = dependencies.GitHub;
        _engineUpdater = dependencies.EngineUpdater;
        _appUpdater = dependencies.AppUpdater;
        _logger = dependencies.Logger;
        _dispatcher = dependencies.Dispatcher;
        _locateEngine = dependencies.LocateEngine;
        _applyCatalog = dependencies.ApplyCatalog;
        _confirm = dependencies.Confirm;
        _logsDirectory = dependencies.LogsDirectory;
        AppVersion = dependencies.AppVersion;
        PresetsPath = dependencies.PresetsPath;
        IsPortable = dependencies.IsPortable;

        _config = _coordinator.Config ?? new AppConfig();
        _catalog = PresetCatalogBuilder.Build(_baseCatalog, _config.CustomPresets);

        _testAllPresetsCommand = new AsyncRelayCommand(async _ => await TestAllPresetsAsync(), () => !IsTestingPresets);
        _cancelTestCommand = new RelayCommand(_ => _testCts?.Cancel(), _ => IsTestingPresets);
        _usePresetCommand = new AsyncRelayCommand(async p => await UsePresetAsync(p as string), () => !IsTestingPresets);
        _makeAutomaticCommand = new RelayCommand(p => MakeAutomatic(p as string), _ => !IsTestingPresets);
        _toggleAutostartCommand = new AsyncRelayCommand(
            async p => await SetAutostartAsync(p as bool?), () => !IsBusy);
        _checkUpdatesCommand = new AsyncRelayCommand(async _ => await CheckUpdatesAsync(), () => !IsMaintenanceBusy);
        _updateEngineCommand = new AsyncRelayCommand(async _ => await UpdateEngineAsync(), () => !IsMaintenanceBusy && !IsPortable);
        _updateAppCommand = new AsyncRelayCommand(async _ => await UpdateAppAsync(), () => !IsMaintenanceBusy);
        _openGitHubCommand = new RelayCommand(_ => OpenGitHub());
        _checkConnectivityCommand = new AsyncRelayCommand(async _ => await CheckConnectivityAsync(), () => !IsCheckingConnectivity);
        _fullDiscordResetCommand = new AsyncRelayCommand(async _ => await FullDiscordResetAsync(), () => !IsFixingDiscord);
        _openLogsCommand = new RelayCommand(_ => OpenLogs());
        _restartConnectionCommand = new AsyncRelayCommand(async _ => await RestartConnectionAsync(), () => !IsBusy);

        LoadFromConfig();
        RefreshPresets();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>После обновления самой программы нужен перезапуск.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Пользовательская ошибка (показывается в окне настроек).</summary>
    public event EventHandler<string>? ErrorRaised;

    public string AppVersion { get; }

    public string PresetsPath { get; }

    public bool IsPortable { get; }

    public string EngineVersion => _catalog.EngineVersion;

    public IReadOnlyList<Preset> ManualPresets { get; private set; } = [];

    public ObservableCollection<PresetTestRowViewModel> PresetTestResults { get; } = new();

    public ObservableCollection<CustomPreset> CustomPresets { get; } = new();

    public string LogText => ReadLogText();

    public bool IsAutoMode
    {
        get => _isAutoMode;
        set
        {
            if (!SetField(ref _isAutoMode, value) || _initializing)
                return;

            _config = _config with
            {
                Connection = _config.Connection with
                {
                    Mode = value ? ConnectionMode.Auto : ConnectionMode.Manual,
                },
            };
            SaveAndApply();
            OnPropertyChanged(nameof(AutoModeHint));
            MarkNeedsRestart();
        }
    }

    public string AutoModeHint => IsAutoMode
        ? "Приложение проверит все обходы и выберет самый быстрый. Это занимает пару минут и нужно только при первом запуске или после сбоя."
        : "Приложение будет включать выбранный обход. Автоподбор можно запустить кнопкой «Протестировать альты».";

    public Preset? SelectedManualPreset
    {
        get => _selectedManualPreset;
        set
        {
            if (!SetField(ref _selectedManualPreset, value) || _initializing || value is null)
                return;

            _config = _config with
            {
                Connection = _config.Connection with { ManualPresetId = value.Id },
            };
            SaveAndApply();
            MarkNeedsRestart();
        }
    }

    public string CurrentProfileText
    {
        get
        {
            var profile = _config.Connection.AutoProfile;
            if (profile is null)
                return "Профиль ещё не подобран — включи обход на главном экране.";

            var youtube = profile.YoutubeMs is { } yt ? $"YouTube {yt} мс" : "YouTube —";
            var discord = profile.DiscordMs is { } dc ? $"Discord {dc} мс" : "Discord —";
            return $"{profile.Name} · {youtube} · {discord}";
        }
    }

    public bool IsGameProfileEnabled
    {
        get => _isGameProfileEnabled;
        set
        {
            if (!SetField(ref _isGameProfileEnabled, value) || _initializing)
                return;

            GameFilter = value ? GameFilterMode.All : GameFilterMode.Disabled;
        }
    }

    public GameFilterMode GameFilter
    {
        get => _gameFilter;
        set
        {
            if (!SetField(ref _gameFilter, value) || _initializing)
                return;

            if (value is GameFilterMode.Tcp or GameFilterMode.Udp or GameFilterMode.All)
                _lastDetailedFilter = value;

            _initializing = true;
            IsGameProfileEnabled = value == GameFilterMode.All;
            _initializing = false;

            _config = _config with { GameFilter = value };
            SaveAndApply();
            MarkNeedsRestart();
        }
    }

    /// <summary>Тонкая настройка фильтра, когда игровой профиль выключен.</summary>
    public GameFilterMode DetailedGameFilter
    {
        get => _lastDetailedFilter;
        set
        {
            if (value == GameFilterMode.Disabled)
                IsGameProfileEnabled = false;
            else
                GameFilter = value;
            OnPropertyChanged();
        }
    }

    public string EnginePath
    {
        get => _enginePath;
        private set => SetField(ref _enginePath, value);
    }

    public string EngineStatusText
    {
        get => _engineStatusText;
        private set => SetField(ref _engineStatusText, value);
    }

    public bool IsAutostartEnabled
    {
        get => _isAutostartEnabled;
        private set => SetField(ref _isAutostartEnabled, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (!SetField(ref _minimizeToTray, value) || _initializing)
                return;

            _config = _config with
            {
                Behavior = _config.Behavior with { MinimizeToTray = value },
            };
            SaveAndApply();
        }
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (!SetField(ref _reduceMotion, value) || _initializing)
                return;

            _config = _config with
            {
                Behavior = _config.Behavior with { ReduceMotion = value },
            };
            SaveAndApply();
        }
    }

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetField(ref _saveStatus, value);
    }

    public bool NeedsRestart
    {
        get => _needsRestart;
        private set => SetField(ref _needsRestart, value);
    }

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
        private set
        {
            if (SetField(ref _isMaintenanceBusy, value))
                RefreshCommands();
        }
    }

    public bool IsBusy => _coordinator.Snapshot.IsBusy;

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
        private set
        {
            if (SetField(ref _isTestingPresets, value))
                RefreshCommands();
        }
    }

    public string ConnectivityStatus
    {
        get => _connectivityStatus;
        private set => SetField(ref _connectivityStatus, value);
    }

    public bool IsCheckingConnectivity
    {
        get => _isCheckingConnectivity;
        private set
        {
            if (SetField(ref _isCheckingConnectivity, value))
                RefreshCommands();
        }
    }

    public IReadOnlyList<ConnectivityRowViewModel> ConnectivityRows
    {
        get => _connectivityRows;
        private set => SetField(ref _connectivityRows, value);
    }

    public bool IsFixingDiscord
    {
        get => _isFixingDiscord;
        private set
        {
            if (SetField(ref _isFixingDiscord, value))
                RefreshCommands();
        }
    }

    public string DiscordStatus
    {
        get => _discordStatus;
        private set => SetField(ref _discordStatus, value);
    }

    public ICommand TestAllPresetsCommand => _testAllPresetsCommand;

    public ICommand CancelPresetTestCommand => _cancelTestCommand;

    public ICommand UsePresetCommand => _usePresetCommand;

    public ICommand MakeAutomaticCommand => _makeAutomaticCommand;

    public ICommand ToggleAutostartCommand => _toggleAutostartCommand;

    public ICommand CheckUpdatesCommand => _checkUpdatesCommand;

    public ICommand UpdateEngineCommand => _updateEngineCommand;

    public ICommand UpdateAppCommand => _updateAppCommand;

    public ICommand OpenGitHubCommand => _openGitHubCommand;

    public ICommand CheckConnectivityCommand => _checkConnectivityCommand;

    public ICommand FullDiscordResetCommand => _fullDiscordResetCommand;

    public ICommand OpenLogsCommand => _openLogsCommand;

    public ICommand RestartConnectionCommand => _restartConnectionCommand;

    /// <summary>Прочитать настройки заново (после миграции/внешнего изменения).</summary>
    public void Reload()
    {
        _config = _coordinator.Config ?? _config;
        LoadFromConfig();
        RefreshPresets();
    }

    /// <summary>Выбрать папку движка вручную.</summary>
    public void ChangeEnginePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var validation = _locateEngine(path);
        if (!validation.IsValid)
        {
            EngineStatusText = "Папка не подходит: " + string.Join(", ", validation.MissingFiles);
            return;
        }

        _config = _config with { Engine = new EngineSettings { Path = path } };
        EnginePath = validation.EngineRoot ?? path;
        EngineStatusText = "Движок найден";
        SaveAndApply();
    }

    /// <summary>Показать фактический путь движка, найденный приложением.</summary>
    public void SetResolvedEngineRoot(string? engineRoot)
    {
        if (string.IsNullOrWhiteSpace(engineRoot))
            return;

        EnginePath = engineRoot;
        EngineStatusText = "Движок: " + engineRoot;
    }

    /// <summary>Добавить пользовательский пресет.</summary>
    public void AddCustomPreset(CustomPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var presets = _config.CustomPresets.ToList();
        presets.Add(preset);
        CommitCustomPresets(presets);
    }

    /// <summary>Изменить пользовательский пресет по идентификатору.</summary>
    public void UpdateCustomPreset(string id, CustomPreset updated)
    {
        var presets = _config.CustomPresets
            .Select(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ? updated : item)
            .ToList();
        CommitCustomPresets(presets);
    }

    /// <summary>Удалить пользовательский пресет.</summary>
    public void RemoveCustomPreset(string id)
    {
        var presets = _config.CustomPresets
            .Where(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        CommitCustomPresets(presets);
    }

    /// <summary>Текст журнала для копирования.</summary>
    public string BuildLogText() => ReadLogText();

    private void LoadFromConfig()
    {
        _initializing = true;
        try
        {
            IsAutoMode = _config.Connection.Mode == ConnectionMode.Auto;
            GameFilter = _config.GameFilter;
            _lastDetailedFilter = _config.GameFilter is GameFilterMode.Tcp or GameFilterMode.Udp or GameFilterMode.All
                ? _config.GameFilter
                : GameFilterMode.All;
            IsGameProfileEnabled = _config.GameFilter == GameFilterMode.All;
            MinimizeToTray = _config.Behavior.MinimizeToTray;
            ReduceMotion = _config.Behavior.ReduceMotion;
            EnginePath = _config.Engine.Path ?? _coordinator.Config?.Engine.Path ?? string.Empty;

            CustomPresets.Clear();
            foreach (var preset in _config.CustomPresets)
                CustomPresets.Add(preset);

            EngineStatusText = string.IsNullOrWhiteSpace(EnginePath)
                ? "Движок ищется автоматически"
                : "Движок: " + EnginePath;
        }
        finally
        {
            _initializing = false;
        }

        _ = RefreshAutostartAsync();
        OnPropertyChanged(nameof(CurrentProfileText));
        OnPropertyChanged(nameof(AutoModeHint));
    }

    private void RefreshPresets()
    {
        _catalog = PresetCatalogBuilder.Build(_baseCatalog, _config.CustomPresets);

        ManualPresets = _catalog.Presets
            .OrderBy(preset => preset.Id, PresetNaming.IdComparer)
            .ToList();

        _selectedManualPreset = ManualPresets.FirstOrDefault(
            preset => preset.Id.Equals(_config.Connection.ManualPresetId, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(ManualPresets));
        OnPropertyChanged(nameof(SelectedManualPreset));
        OnPropertyChanged(nameof(EngineVersion));
    }

    private async Task RefreshAutostartAsync()
    {
        try
        {
            var enabled = await _autostart.IsEnabledAsync().ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                _initializing = true;
                IsAutostartEnabled = enabled;
                _initializing = false;
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Проверка автозапуска: " + ex.Message, ex);
        }
    }

    private async Task SetAutostartAsync(bool? requested)
    {
        var enable = requested ?? !IsAutostartEnabled;

        try
        {
            if (enable)
                await _autostart.EnableAsync().ConfigureAwait(false);
            else
                await _autostart.DisableAsync().ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                _initializing = true;
                IsAutostartEnabled = enable;
                _initializing = false;

                _config = _config with
                {
                    Behavior = _config.Behavior with { Autostart = enable },
                };
                SaveAndApply();
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Автозапуск: " + ex.Message, ex);

            var actual = IsAutostartEnabled;
            try { actual = await _autostart.IsEnabledAsync().ConfigureAwait(false); }
            catch { /* оставляем прежнее значение */ }

            _dispatcher.Post(() =>
            {
                _initializing = true;
                IsAutostartEnabled = actual;
                _initializing = false;
                SaveStatus = "Не удалось изменить автозапуск: " + ex.Message;
                ErrorRaised?.Invoke(this,
                    "Не удалось изменить автозапуск. Запусти программу от администратора и попробуй снова.");
            });
        }
    }

    private async Task TestAllPresetsAsync()
    {
        if (IsTestingPresets)
            return;

        var validation = _locateEngine(_config.Engine.Path);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.EngineRoot))
        {
            PresetTestStatus = "Движок не найден. Укажи папку движка.";
            return;
        }

        if (_coordinator.Snapshot.State == ConnectionState.Connected &&
            !_confirm("Текущий обход будет временно выключен на время проверки. Продолжить?"))
        {
            return;
        }

        IsTestingPresets = true;
        PresetTestResults.Clear();
        PresetTestStatus = string.Empty;
        PresetTestProgress = 0;
        _testCts = new CancellationTokenSource();

        try
        {
            if (_coordinator.Snapshot.State != ConnectionState.Disconnected)
                await _coordinator.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

            var presets = ManualPresets;
            if (presets.Count == 0)
            {
                PresetTestStatus = "Пресеты не найдены.";
                return;
            }

            var results = await _presetTester.TestAllAsync(
                presets,
                validation.EngineRoot,
                GameFilter,
                new InlineProgress<PresetTestProgress>(report => _dispatcher.Post(() =>
                {
                    PresetTestProgress = report.Total == 0 ? 0 : report.Index / (double)report.Total * 100;

                    if (report.Result is null)
                        return;

                    var existing = PresetTestResults
                        .FirstOrDefault(row => row.PresetId.Equals(report.Result.PresetId, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                        PresetTestResults.Remove(existing);

                    PresetTestResults.Add(new PresetTestRowViewModel(report.Result));
                })),
                _testCts.Token).ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                PresetTestResults.Clear();
                foreach (var result in results
                             .OrderByDescending(item => item.Qualified)
                             .ThenBy(item => item.ScoreMs ?? long.MaxValue))
                {
                    PresetTestResults.Add(new PresetTestRowViewModel(result));
                }

                var ok = results.Count(item => item.Qualified);
                PresetTestStatus = $"Проверено {results.Count}, работают оба сервиса у {ok}.";
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Post(() => PresetTestStatus = PresetTestResults.Count > 0
                ? $"Проверка остановлена. Уже найдено рабочих: {PresetTestResults.Count(row => row.Qualified)}."
                : "Проверка остановлена.");
        }
        catch (Exception ex)
        {
            _logger.Error("Тест обходов: " + ex.Message, ex);
            _dispatcher.Post(() => PresetTestStatus = "Ошибка проверки: " + ex.Message);
        }
        finally
        {
            _testCts?.Dispose();
            _testCts = null;
            IsTestingPresets = false;
        }
    }

    private Task UsePresetAsync(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            return Task.CompletedTask;

        var preset = ManualPresets.FirstOrDefault(
            item => item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
            return Task.CompletedTask;

        _config = _config with
        {
            Connection = _config.Connection with
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = preset.Id,
            },
        };

        _initializing = true;
        IsAutoMode = false;
        _selectedManualPreset = preset;
        _initializing = false;

        OnPropertyChanged(nameof(SelectedManualPreset));
        SaveAndApply();
        MarkNeedsRestart();
        PresetTestStatus = $"Выбран обход: {preset.Name}. Перезапусти обход, чтобы применить.";
        return Task.CompletedTask;
    }

    private void MakeAutomatic(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            return;

        var row = PresetTestResults.FirstOrDefault(
            item => item.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        var preset = ManualPresets.FirstOrDefault(
            item => item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (row is null || preset is null)
            return;

        var latency = Math.Max(row.YoutubeMs ?? 0, row.DiscordMs ?? 0);
        var profile = new AutoProfile(
            preset.Id,
            preset.Name,
            latency,
            row.YoutubeMs,
            row.DiscordMs,
            DateTimeOffset.UtcNow,
            _catalog.EngineVersion,
            string.Empty);

        _config = _config with
        {
            Connection = _config.Connection with
            {
                Mode = ConnectionMode.Auto,
                AutoProfile = profile,
            },
        };

        _initializing = true;
        IsAutoMode = true;
        _initializing = false;

        SaveAndApply();
        OnPropertyChanged(nameof(CurrentProfileText));
        MarkNeedsRestart();
        PresetTestStatus = $"«{preset.Name}» сохранён как автоматический профиль.";
    }

    private async Task CheckUpdatesAsync()
    {
        IsMaintenanceBusy = true;
        try
        {
            UpdateProgress = 0;
            UpdateStatusText = "Проверяем обновления…";

            var engineLatest = await _gitHub.GetLatestAsync(
                "Flowseal/zapret-discord-youtube", CancellationToken.None);
            UpdateProgress = 50;

            var appLatest = await _gitHub.GetLatestAsync(
                "Benny4ip/ZPRT_NEXT_GEN", CancellationToken.None);
            UpdateProgress = 100;

            var text = string.Empty;
            if (engineLatest is not null)
            {
                text += VersionComparer.IsNewer(engineLatest.Tag, _catalog.EngineVersion)
                    ? $"Доступна версия движка {engineLatest.Tag} (установлена {_catalog.EngineVersion}). "
                    : $"Движок актуален (v{_catalog.EngineVersion}). ";
            }
            else
            {
                text += "Не удалось проверить движок. ";
            }

            if (appLatest is not null)
            {
                text += VersionComparer.IsNewer(appLatest.Tag, AppVersion)
                    ? $"Доступна версия программы {appLatest.Tag} (установлена v{AppVersion})."
                    : $"Программа актуальна (v{AppVersion}).";
            }
            else
            {
                text += "Не удалось проверить программу.";
            }

            _dispatcher.Post(() => UpdateStatusText = text.Trim());
        }
        catch (Exception ex)
        {
            _logger.Error("Проверка обновлений: " + ex.Message, ex);
            _dispatcher.Post(() => UpdateStatusText = "Ошибка проверки обновлений: " + ex.Message);
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                IsMaintenanceBusy = false;
                UpdateProgress = 0;
            });
        }
    }

    private async Task UpdateEngineAsync()
    {
        if (IsPortable)
        {
            UpdateStatusText = "Движок встроен в portable-версию. Обновите программу целиком.";
            return;
        }

        var engineRoot = _locateEngine(_config.Engine.Path).EngineRoot;
        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            UpdateStatusText = "Движок не найден.";
            return;
        }

        if (_coordinator.Snapshot.State == ConnectionState.Connected)
        {
            UpdateStatusText = "Останови обход перед обновлением движка.";
            return;
        }

        IsMaintenanceBusy = true;
        try
        {
            UpdateStatusText = "Проверяем обновления движка…";
            var release = await _gitHub.GetLatestAsync(
                "Flowseal/zapret-discord-youtube", CancellationToken.None).ConfigureAwait(false);

            if (release is null)
            {
                UpdateStatusText = "Не удалось проверить обновления движка.";
                return;
            }

            if (!VersionComparer.IsNewer(release.Tag, _catalog.EngineVersion))
            {
                UpdateStatusText = $"Уже последняя версия движка (v{_catalog.EngineVersion}).";
                return;
            }

            UpdateStatusText = $"Скачивание и установка v{release.Tag}…";

            var result = await _engineUpdater.UpdateAsync(
                release,
                engineRoot,
                PresetsPath,
                async () =>
                {
                    await _engineProcess.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    _engineProcess.KillForeignInstances();
                    await Task.Delay(300).ConfigureAwait(false);
                },
                new Progress<string>(message => _dispatcher.Post(() => UpdateStatusText = message)),
                CancellationToken.None).ConfigureAwait(false);

            _dispatcher.Post(() => UpdateStatusText = result.Message
                ?? $"Движок обновлён до v{result.NewVersion} ({result.PresetCount} пресетов).");
        }
        catch (Exception ex)
        {
            _logger.Error("Обновление движка: " + ex.Message, ex);
            _dispatcher.Post(() => UpdateStatusText = "Ошибка обновления движка: " + ex.Message);
        }
        finally
        {
            IsMaintenanceBusy = false;
        }
    }

    private async Task UpdateAppAsync()
    {
        IsMaintenanceBusy = true;
        try
        {
            UpdateStatusText = "Проверяем обновления программы…";
            var release = await _gitHub.GetLatestAsync(
                "Benny4ip/ZPRT_NEXT_GEN", CancellationToken.None).ConfigureAwait(false);

            if (release is null)
            {
                UpdateStatusText = "Не удалось проверить обновления программы.";
                return;
            }

            if (!VersionComparer.IsNewer(release.Tag, AppVersion))
            {
                UpdateStatusText = $"Программа уже актуальна (v{AppVersion}).";
                return;
            }

            UpdateStatusText = $"Подготовка обновления до v{release.Tag}…";
            var plan = await _appUpdater.PrepareAsync(
                release, AppContext.BaseDirectory, null, CancellationToken.None, IsPortable).ConfigureAwait(false);

            _appUpdater.Launch(plan);
            UpdateStatusText = $"Обновление до v{release.Tag} подготовлено. Перезапуск…";
            _dispatcher.Post(() => RestartRequested?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            _logger.Error("Обновление программы: " + ex.Message, ex);
            _dispatcher.Post(() => UpdateStatusText = "Ошибка обновления программы: " + ex.Message);
        }
        finally
        {
            IsMaintenanceBusy = false;
        }
    }

    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/Benny4ip/ZPRT_NEXT_GEN/releases")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Error("GitHub releases: " + ex.Message, ex);
            ErrorRaised?.Invoke(this, "Не удалось открыть браузер.");
        }
    }

    private async Task CheckConnectivityAsync()
    {
        IsCheckingConnectivity = true;
        ConnectivityStatus = "Проверяем YouTube и Discord…";

        try
        {
            var report = await _connectivity.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                ConnectivityRows = report.Results
                    .Select(result => new ConnectivityRowViewModel(
                        result.Target.Id,
                        result.Target.Service == ServiceKind.Discord ? "Discord" : "YouTube",
                        result.Success,
                        result.ElapsedMs,
                        result.Error,
                        result.TimedOut))
                    .ToList();

                ConnectivityStatus = report.BothOk
                    ? "YouTube и Discord доступны."
                    : report.IsConnected
                        ? "Один из сервисов недоступен. Попробуй другой обход."
                        : "Оба сервиса недоступны. Проверь интернет.";
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Проверка связи: " + ex.Message, ex);
            _dispatcher.Post(() => ConnectivityStatus = "Ошибка проверки связи: " + ex.Message);
        }
        finally
        {
            IsCheckingConnectivity = false;
        }
    }

    private async Task FullDiscordResetAsync()
    {
        if (!_confirm("Полный сброс Discord удалит cookies и локальные данные аккаунта — придётся войти заново.\n\nПродолжить?"))
            return;

        IsFixingDiscord = true;
        DiscordStatus = "Полный сброс Discord…";

        try
        {
            var result = await _discordFix.FixAsync(fullReset: true, CancellationToken.None).ConfigureAwait(false);
            _dispatcher.Post(() => DiscordStatus = result.Status switch
            {
                DiscordFixStatus.Success => "Готово. Discord перезапущен, войди в аккаунт заново.",
                DiscordFixStatus.NotInstalled => "Discord не найден на этом компьютере.",
                _ => result.ErrorMessage ?? "Не удалось перезапустить Discord.",
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Полный сброс Discord: " + ex.Message, ex);
            _dispatcher.Post(() => DiscordStatus = "Ошибка полного сброса: " + ex.Message);
        }
        finally
        {
            IsFixingDiscord = false;
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
            _logger.Error("Открытие логов: " + ex.Message, ex);
            ErrorRaised?.Invoke(this, "Не удалось открыть папку логов.");
        }
    }

    private async Task RestartConnectionAsync()
    {
        NeedsRestart = false;
        try
        {
            await _coordinator.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            await _coordinator.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Перезапуск обхода: " + ex.Message, ex);
        }
    }

    private void CommitCustomPresets(IReadOnlyList<CustomPreset> presets)
    {
        _config = ConfigStore.Normalize(
            _config with { CustomPresets = presets.ToList() },
            _catalog.Presets.Select(preset => preset.Id).ToList());

        CustomPresets.Clear();
        foreach (var preset in _config.CustomPresets)
            CustomPresets.Add(preset);

        RefreshPresets();
        _applyCatalog?.Invoke(PresetCatalogBuilder.Build(_baseCatalog, _config.CustomPresets));
        SaveAndApply();
        SaveStatus = "Мои пресеты сохранены";
    }

    private void MarkNeedsRestart()
    {
        if (_coordinator.Snapshot.State == ConnectionState.Connected)
            NeedsRestart = true;
    }

    private void SaveAndApply()
    {
        try
        {
            _configStore.Save(_config);
            _coordinator.ApplyConfig(_config);
            SaveStatus = "Сохранено";
            OnPropertyChanged(nameof(CurrentProfileText));
        }
        catch (Exception ex)
        {
            _logger.Error("Сохранение настроек: " + ex.Message, ex);
            SaveStatus = "Ошибка сохранения: " + ex.Message;
        }
    }

    private string ReadLogText()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_logsDirectory) || !Directory.Exists(_logsDirectory))
                return string.Empty;

            var newest = Directory
                .EnumerateFiles(_logsDirectory, "app-*.log")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return newest is null ? string.Empty : File.ReadAllText(newest);
        }
        catch (Exception ex)
        {
            _logger.Error("Чтение логов: " + ex.Message, ex);
            return string.Empty;
        }
    }

    private void RefreshCommands()
    {
        _dispatcher.Post(() =>
        {
            _testAllPresetsCommand.RaiseCanExecuteChanged();
            _cancelTestCommand.RaiseCanExecuteChanged();
            _usePresetCommand.RaiseCanExecuteChanged();
            _makeAutomaticCommand.RaiseCanExecuteChanged();
            _toggleAutostartCommand.RaiseCanExecuteChanged();
            _checkUpdatesCommand.RaiseCanExecuteChanged();
            _updateEngineCommand.RaiseCanExecuteChanged();
            _updateAppCommand.RaiseCanExecuteChanged();
            _checkConnectivityCommand.RaiseCanExecuteChanged();
            _fullDiscordResetCommand.RaiseCanExecuteChanged();
            _restartConnectionCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(IsBusy));
        });
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler is null)
            return;

        _dispatcher.Post(() => handler.Invoke(this, new PropertyChangedEventArgs(propertyName)));
    }
}
