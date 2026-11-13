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

/// <summary>Р—Р°РІРёСЃРёРјРѕСЃС‚Рё <see cref="SettingsViewModel"/>.</summary>
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
/// РќР°СЃС‚СЂРѕР№РєРё: РєР°Рє РїРѕРґРєР»СЋС‡Р°С‚СЊСЃСЏ, РёРіСЂРѕРІРѕР№ РїСЂРѕС„РёР»СЊ, РјРѕРё РїСЂРµСЃРµС‚С‹, Р·Р°РїСѓСЃРє Рё С‚СЂРµР№,
/// РґРІРёР¶РѕРє Рё РѕР±РЅРѕРІР»РµРЅРёСЏ, РґРёР°РіРЅРѕСЃС‚РёРєР°. РЎРѕС…СЂР°РЅСЏРµС‚ config.json СЃСЂР°Р·Сѓ РїСЂРё РёР·РјРµРЅРµРЅРёРё.
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
    private string _saveStatus = "РќР°СЃС‚СЂРѕР№РєРё СЃРѕС…СЂР°РЅСЏСЋС‚СЃСЏ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё";
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

    /// <summary>РџРѕСЃР»Рµ РѕР±РЅРѕРІР»РµРЅРёСЏ СЃР°РјРѕР№ РїСЂРѕРіСЂР°РјРјС‹ РЅСѓР¶РµРЅ РїРµСЂРµР·Р°РїСѓСЃРє.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>РџРѕР»СЊР·РѕРІР°С‚РµР»СЊСЃРєР°СЏ РѕС€РёР±РєР° (РїРѕРєР°Р·С‹РІР°РµС‚СЃСЏ РІ РѕРєРЅРµ РЅР°СЃС‚СЂРѕРµРє).</summary>
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
        ? "РџСЂРёР»РѕР¶РµРЅРёРµ РїСЂРѕРІРµСЂРёС‚ РІСЃРµ РѕР±С…РѕРґС‹ Рё РІС‹Р±РµСЂРµС‚ СЃР°РјС‹Р№ Р±С‹СЃС‚СЂС‹Р№. Р­С‚Рѕ Р·Р°РЅРёРјР°РµС‚ РїР°СЂСѓ РјРёРЅСѓС‚ Рё РЅСѓР¶РЅРѕ С‚РѕР»СЊРєРѕ РїСЂРё РїРµСЂРІРѕРј Р·Р°РїСѓСЃРєРµ РёР»Рё РїРѕСЃР»Рµ СЃР±РѕСЏ."
        : "РџСЂРёР»РѕР¶РµРЅРёРµ Р±СѓРґРµС‚ РІРєР»СЋС‡Р°С‚СЊ РІС‹Р±СЂР°РЅРЅС‹Р№ РѕР±С…РѕРґ. РђРІС‚РѕРїРѕРґР±РѕСЂ РјРѕР¶РЅРѕ Р·Р°РїСѓСЃС‚РёС‚СЊ РєРЅРѕРїРєРѕР№ В«РџСЂРѕС‚РµСЃС‚РёСЂРѕРІР°С‚СЊ Р°Р»СЊС‚С‹В».";

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
                return "РџСЂРѕС„РёР»СЊ РµС‰С‘ РЅРµ РїРѕРґРѕР±СЂР°РЅ вЂ” РІРєР»СЋС‡Рё РѕР±С…РѕРґ РЅР° РіР»Р°РІРЅРѕРј СЌРєСЂР°РЅРµ.";

            var youtube = profile.YoutubeMs is { } yt ? $"YouTube {yt} РјСЃ" : "YouTube вЂ”";
            var discord = profile.DiscordMs is { } dc ? $"Discord {dc} РјСЃ" : "Discord вЂ”";
            return $"{profile.Name} В· {youtube} В· {discord}";
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

    /// <summary>РўРѕРЅРєР°СЏ РЅР°СЃС‚СЂРѕР№РєР° С„РёР»СЊС‚СЂР°, РєРѕРіРґР° РёРіСЂРѕРІРѕР№ РїСЂРѕС„РёР»СЊ РІС‹РєР»СЋС‡РµРЅ.</summary>
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

    /// <summary>РџСЂРѕС‡РёС‚Р°С‚СЊ РЅР°СЃС‚СЂРѕР№РєРё Р·Р°РЅРѕРІРѕ (РїРѕСЃР»Рµ РјРёРіСЂР°С†РёРё/РІРЅРµС€РЅРµРіРѕ РёР·РјРµРЅРµРЅРёСЏ).</summary>
    public void Reload()
    {
        _config = _coordinator.Config ?? _config;
        LoadFromConfig();
        RefreshPresets();
    }

    /// <summary>Р’С‹Р±СЂР°С‚СЊ РїР°РїРєСѓ РґРІРёР¶РєР° РІСЂСѓС‡РЅСѓСЋ.</summary>
    public void ChangeEnginePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var validation = _locateEngine(path);
        if (!validation.IsValid)
        {
            EngineStatusText = "РџР°РїРєР° РЅРµ РїРѕРґС…РѕРґРёС‚: " + string.Join(", ", validation.MissingFiles);
            return;
        }

        _config = _config with { Engine = new EngineSettings { Path = path } };
        EnginePath = validation.EngineRoot ?? path;
        EngineStatusText = "Р”РІРёР¶РѕРє РЅР°Р№РґРµРЅ";
        SaveAndApply();
    }

    /// <summary>РџРѕРєР°Р·Р°С‚СЊ С„Р°РєС‚РёС‡РµСЃРєРёР№ РїСѓС‚СЊ РґРІРёР¶РєР°, РЅР°Р№РґРµРЅРЅС‹Р№ РїСЂРёР»РѕР¶РµРЅРёРµРј.</summary>
    public void SetResolvedEngineRoot(string? engineRoot)
    {
        if (string.IsNullOrWhiteSpace(engineRoot))
            return;

        EnginePath = engineRoot;
        EngineStatusText = "Р”РІРёР¶РѕРє: " + engineRoot;
    }

    /// <summary>Р”РѕР±Р°РІРёС‚СЊ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊСЃРєРёР№ РїСЂРµСЃРµС‚.</summary>
    public void AddCustomPreset(CustomPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var presets = _config.CustomPresets.ToList();
        presets.Add(preset);
        CommitCustomPresets(presets);
    }

    /// <summary>РР·РјРµРЅРёС‚СЊ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊСЃРєРёР№ РїСЂРµСЃРµС‚ РїРѕ РёРґРµРЅС‚РёС„РёРєР°С‚РѕСЂСѓ.</summary>
    public void UpdateCustomPreset(string id, CustomPreset updated)
    {
        var presets = _config.CustomPresets
            .Select(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ? updated : item)
            .ToList();
        CommitCustomPresets(presets);
    }

    /// <summary>РЈРґР°Р»РёС‚СЊ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊСЃРєРёР№ РїСЂРµСЃРµС‚.</summary>
    public void RemoveCustomPreset(string id)
    {
        var presets = _config.CustomPresets
            .Where(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        CommitCustomPresets(presets);
    }

    /// <summary>РўРµРєСЃС‚ Р¶СѓСЂРЅР°Р»Р° РґР»СЏ РєРѕРїРёСЂРѕРІР°РЅРёСЏ.</summary>
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
                ? "Р”РІРёР¶РѕРє РёС‰РµС‚СЃСЏ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё"
                : "Р”РІРёР¶РѕРє: " + EnginePath;
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
            _logger.Error("РџСЂРѕРІРµСЂРєР° Р°РІС‚РѕР·Р°РїСѓСЃРєР°: " + ex.Message, ex);
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
            _logger.Error("РђРІС‚РѕР·Р°РїСѓСЃРє: " + ex.Message, ex);

            var actual = IsAutostartEnabled;
            try { actual = await _autostart.IsEnabledAsync().ConfigureAwait(false); }
            catch { /* РѕСЃС‚Р°РІР»СЏРµРј РїСЂРµР¶РЅРµРµ Р·РЅР°С‡РµРЅРёРµ */ }

            _dispatcher.Post(() =>
            {
                _initializing = true;
                IsAutostartEnabled = actual;
                _initializing = false;
                SaveStatus = "РќРµ СѓРґР°Р»РѕСЃСЊ РёР·РјРµРЅРёС‚СЊ Р°РІС‚РѕР·Р°РїСѓСЃРє: " + ex.Message;
                ErrorRaised?.Invoke(this,
                    "РќРµ СѓРґР°Р»РѕСЃСЊ РёР·РјРµРЅРёС‚СЊ Р°РІС‚РѕР·Р°РїСѓСЃРє. Р—Р°РїСѓСЃС‚Рё РїСЂРѕРіСЂР°РјРјСѓ РѕС‚ Р°РґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂР° Рё РїРѕРїСЂРѕР±СѓР№ СЃРЅРѕРІР°.");
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
            PresetTestStatus = "Р”РІРёР¶РѕРє РЅРµ РЅР°Р№РґРµРЅ. РЈРєР°Р¶Рё РїР°РїРєСѓ РґРІРёР¶РєР°.";
            return;
        }

        if (_coordinator.Snapshot.State == ConnectionState.Connected &&
            !_confirm("РўРµРєСѓС‰РёР№ РѕР±С…РѕРґ Р±СѓРґРµС‚ РІСЂРµРјРµРЅРЅРѕ РІС‹РєР»СЋС‡РµРЅ РЅР° РІСЂРµРјСЏ РїСЂРѕРІРµСЂРєРё. РџСЂРѕРґРѕР»Р¶РёС‚СЊ?"))
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
                PresetTestStatus = "РџСЂРµСЃРµС‚С‹ РЅРµ РЅР°Р№РґРµРЅС‹.";
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
                PresetTestStatus = $"РџСЂРѕРІРµСЂРµРЅРѕ {results.Count}, СЂР°Р±РѕС‚Р°СЋС‚ РѕР±Р° СЃРµСЂРІРёСЃР° Сѓ {ok}.";
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Post(() => PresetTestStatus = PresetTestResults.Count > 0
                ? $"РџСЂРѕРІРµСЂРєР° РѕСЃС‚Р°РЅРѕРІР»РµРЅР°. РЈР¶Рµ РЅР°Р№РґРµРЅРѕ СЂР°Р±РѕС‡РёС…: {PresetTestResults.Count(row => row.Qualified)}."
                : "РџСЂРѕРІРµСЂРєР° РѕСЃС‚Р°РЅРѕРІР»РµРЅР°.");
        }
        catch (Exception ex)
        {
            _logger.Error("РўРµСЃС‚ РѕР±С…РѕРґРѕРІ: " + ex.Message, ex);
            _dispatcher.Post(() => PresetTestStatus = "РћС€РёР±РєР° РїСЂРѕРІРµСЂРєРё: " + ex.Message);
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
        PresetTestStatus = $"Р’С‹Р±СЂР°РЅ РѕР±С…РѕРґ: {preset.Name}. РџРµСЂРµР·Р°РїСѓСЃС‚Рё РѕР±С…РѕРґ, С‡С‚РѕР±С‹ РїСЂРёРјРµРЅРёС‚СЊ.";
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
        PresetTestStatus = $"В«{preset.Name}В» СЃРѕС…СЂР°РЅС‘РЅ РєР°Рє Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРёР№ РїСЂРѕС„РёР»СЊ.";
    }

    private async Task CheckUpdatesAsync()
    {
        IsMaintenanceBusy = true;
        try
        {
            UpdateProgress = 0;
            UpdateStatusText = "РџСЂРѕРІРµСЂСЏРµРј РѕР±РЅРѕРІР»РµРЅРёСЏвЂ¦";

            var engineLatest = await _gitHub.GetLatestAsync(
                "Flowseal/zapret-discord-youtube", CancellationToken.None).ConfigureAwait(false);
            UpdateProgress = 50;

            var appLatest = await _gitHub.GetLatestAsync(
                "Benny4ip/ZPRT_NEXT_GEN", CancellationToken.None).ConfigureAwait(false);
            UpdateProgress = 100;

            var text = string.Empty;
            if (engineLatest is not null)
            {
                text += VersionComparer.IsNewer(engineLatest.Tag, _catalog.EngineVersion)
                    ? $"Р”РѕСЃС‚СѓРїРЅР° РІРµСЂСЃРёСЏ РґРІРёР¶РєР° {engineLatest.Tag} (СѓСЃС‚Р°РЅРѕРІР»РµРЅР° {_catalog.EngineVersion}). "
                    : $"Р”РІРёР¶РѕРє Р°РєС‚СѓР°Р»РµРЅ (v{_catalog.EngineVersion}). ";
            }
            else
            {
                text += "РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕРІРµСЂРёС‚СЊ РґРІРёР¶РѕРє. ";
            }

            if (appLatest is not null)
            {
                text += VersionComparer.IsNewer(appLatest.Tag, AppVersion)
                    ? $"Р”РѕСЃС‚СѓРїРЅР° РІРµСЂСЃРёСЏ РїСЂРѕРіСЂР°РјРјС‹ {appLatest.Tag} (СѓСЃС‚Р°РЅРѕРІР»РµРЅР° v{AppVersion})."
                    : $"РџСЂРѕРіСЂР°РјРјР° Р°РєС‚СѓР°Р»СЊРЅР° (v{AppVersion}).";
            }
            else
            {
                text += "РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕРІРµСЂРёС‚СЊ РїСЂРѕРіСЂР°РјРјСѓ.";
            }

            _dispatcher.Post(() => UpdateStatusText = text.Trim());
        }
        catch (Exception ex)
        {
            _logger.Error("РџСЂРѕРІРµСЂРєР° РѕР±РЅРѕРІР»РµРЅРёР№: " + ex.Message, ex);
            _dispatcher.Post(() => UpdateStatusText = "РћС€РёР±РєР° РїСЂРѕРІРµСЂРєРё РѕР±РЅРѕРІР»РµРЅРёР№: " + ex.Message);
        }
        finally
        {
            IsMaintenanceBusy = false;
            UpdateProgress = 0;
        }
    }

    private async Task UpdateEngineAsync()
    {
        if (IsPortable)
        {
            UpdateStatusText = "Р”РІРёР¶РѕРє РІСЃС‚СЂРѕРµРЅ РІ portable-РІРµСЂСЃРёСЋ. РћР±РЅРѕРІРёС‚Рµ РїСЂРѕРіСЂР°РјРјСѓ С†РµР»РёРєРѕРј.";
            return;
        }

        var engineRoot = _locateEngine(_config.Engine.Path).EngineRoot;
        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            UpdateStatusText = "Р”РІРёР¶РѕРє РЅРµ РЅР°Р№РґРµРЅ.";
            return;
        }

        if (_coordinator.Snapshot.State == ConnectionState.Connected)
        {
            UpdateStatusText = "РћСЃС‚Р°РЅРѕРІРё РѕР±С…РѕРґ РїРµСЂРµРґ РѕР±РЅРѕРІР»РµРЅРёРµРј РґРІРёР¶РєР°.";
            return;
        }

        IsMaintenanceBusy = true;
        try
        {
            UpdateStatusText = "РџСЂРѕРІРµСЂСЏРµРј РѕР±РЅРѕРІР»РµРЅРёСЏ РґРІРёР¶РєР°вЂ¦";
            var release = await _gitHub.GetLatestAsync(
                "Flowseal/zapret-discord-youtube", CancellationToken.None).ConfigureAwait(false);

            if (release is null)
            {
                UpdateStatusText = "РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕРІРµСЂРёС‚СЊ РѕР±РЅРѕРІР»РµРЅРёСЏ РґРІРёР¶РєР°.";
                return;
            }

            if (!VersionComparer.IsNewer(release.Tag, _catalog.EngineVersion))
            {
                UpdateStatusText = $"РЈР¶Рµ РїРѕСЃР»РµРґРЅСЏСЏ РІРµСЂСЃРёСЏ РґРІРёР¶РєР° (v{_catalog.EngineVersion}).";
                return;
            }

            UpdateStatusText = $"РЎРєР°С‡РёРІР°РЅРёРµ Рё СѓСЃС‚Р°РЅРѕРІРєР° v{release.Tag}вЂ¦";

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
                ?? $"Р”РІРёР¶РѕРє РѕР±РЅРѕРІР»С‘РЅ РґРѕ v{result.NewVersion} ({result.PresetCount} РїСЂРµСЃРµС‚РѕРІ).");
        }
        catch (Exception ex)
        {
            _logger.Error("РћР±РЅРѕРІР»РµРЅРёРµ РґРІРёР¶РєР°: " + ex.Message, ex);
            _dispatcher.Post(() => UpdateStatusText = "РћС€РёР±РєР° РѕР±РЅРѕРІР»РµРЅРёСЏ РґРІРёР¶РєР°: " + ex.Message);
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
            UpdateStatusText = "РџСЂРѕРІРµСЂСЏРµРј РѕР±РЅРѕРІР»РµРЅРёСЏ РїСЂРѕРіСЂР°РјРјС‹вЂ¦";
            var release = await _gitHub.GetLatestAsync(
                "Benny4ip/ZPRT_NEXT_GEN", CancellationToken.None).ConfigureAwait(false);

            if (release is null)
            {
                UpdateStatusText = "РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕРІРµСЂРёС‚СЊ РѕР±РЅРѕРІР»РµРЅРёСЏ РїСЂРѕРіСЂР°РјРјС‹.";
                return;
            }

            if (!VersionComparer.IsNewer(release.Tag, AppVersion))
            {
                UpdateStatusText = $"РџСЂРѕРіСЂР°РјРјР° СѓР¶Рµ Р°РєС‚СѓР°Р»СЊРЅР° (v{AppVersion}).";
                return;
            }

            UpdateStatusText = $"РџРѕРґРіРѕС‚РѕРІРєР° РѕР±РЅРѕРІР»РµРЅРёСЏ РґРѕ v{release.Tag}вЂ¦";
            var plan = await _appUpdater.PrepareAsync(
                release, AppContext.BaseDirectory, null, CancellationToken.None, IsPortable).ConfigureAwait(false);

            _appUpdater.Launch(plan);
            UpdateStatusText = $"РћР±РЅРѕРІР»РµРЅРёРµ РґРѕ v{release.Tag} РїРѕРґРіРѕС‚РѕРІР»РµРЅРѕ. РџРµСЂРµР·Р°РїСѓСЃРєвЂ¦";
            _dispatcher.Post(() => RestartRequested?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            _logger.Error("РћР±РЅРѕРІР»РµРЅРёРµ РїСЂРѕРіСЂР°РјРјС‹: " + ex.Message, ex);
            _dispatcher.Post(() => UpdateStatusText = "РћС€РёР±РєР° РѕР±РЅРѕРІР»РµРЅРёСЏ РїСЂРѕРіСЂР°РјРјС‹: " + ex.Message);
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
            ErrorRaised?.Invoke(this, "РќРµ СѓРґР°Р»РѕСЃСЊ РѕС‚РєСЂС‹С‚СЊ Р±СЂР°СѓР·РµСЂ.");
        }
    }

    private async Task CheckConnectivityAsync()
    {
        IsCheckingConnectivity = true;
        ConnectivityStatus = "РџСЂРѕРІРµСЂСЏРµРј YouTube Рё DiscordвЂ¦";

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
                    ? "YouTube Рё Discord РґРѕСЃС‚СѓРїРЅС‹."
                    : report.IsConnected
                        ? "РћРґРёРЅ РёР· СЃРµСЂРІРёСЃРѕРІ РЅРµРґРѕСЃС‚СѓРїРµРЅ. РџРѕРїСЂРѕР±СѓР№ РґСЂСѓРіРѕР№ РѕР±С…РѕРґ."
                        : "РћР±Р° СЃРµСЂРІРёСЃР° РЅРµРґРѕСЃС‚СѓРїРЅС‹. РџСЂРѕРІРµСЂСЊ РёРЅС‚РµСЂРЅРµС‚.";
            });
        }
        catch (Exception ex)
        {
            _logger.Error("РџСЂРѕРІРµСЂРєР° СЃРІСЏР·Рё: " + ex.Message, ex);
            _dispatcher.Post(() => ConnectivityStatus = "РћС€РёР±РєР° РїСЂРѕРІРµСЂРєРё СЃРІСЏР·Рё: " + ex.Message);
        }
        finally
        {
            IsCheckingConnectivity = false;
        }
    }

    private async Task FullDiscordResetAsync()
    {
        if (!_confirm("РџРѕР»РЅС‹Р№ СЃР±СЂРѕСЃ Discord СѓРґР°Р»РёС‚ cookies Рё Р»РѕРєР°Р»СЊРЅС‹Рµ РґР°РЅРЅС‹Рµ Р°РєРєР°СѓРЅС‚Р° вЂ” РїСЂРёРґС‘С‚СЃСЏ РІРѕР№С‚Рё Р·Р°РЅРѕРІРѕ.\n\nРџСЂРѕРґРѕР»Р¶РёС‚СЊ?"))
            return;

        IsFixingDiscord = true;
        DiscordStatus = "РџРѕР»РЅС‹Р№ СЃР±СЂРѕСЃ DiscordвЂ¦";

        try
        {
            var result = await _discordFix.FixAsync(fullReset: true, CancellationToken.None).ConfigureAwait(false);
            _dispatcher.Post(() => DiscordStatus = result.Status switch
            {
                DiscordFixStatus.Success => "Р“РѕС‚РѕРІРѕ. Discord РїРµСЂРµР·Р°РїСѓС‰РµРЅ, РІРѕР№РґРё РІ Р°РєРєР°СѓРЅС‚ Р·Р°РЅРѕРІРѕ.",
                DiscordFixStatus.NotInstalled => "Discord РЅРµ РЅР°Р№РґРµРЅ РЅР° СЌС‚РѕРј РєРѕРјРїСЊСЋС‚РµСЂРµ.",
                _ => result.ErrorMessage ?? "РќРµ СѓРґР°Р»РѕСЃСЊ РїРµСЂРµР·Р°РїСѓСЃС‚РёС‚СЊ Discord.",
            });
        }
        catch (Exception ex)
        {
            _logger.Error("РџРѕР»РЅС‹Р№ СЃР±СЂРѕСЃ Discord: " + ex.Message, ex);
            _dispatcher.Post(() => DiscordStatus = "РћС€РёР±РєР° РїРѕР»РЅРѕРіРѕ СЃР±СЂРѕСЃР°: " + ex.Message);
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
            _logger.Error("РћС‚РєСЂС‹С‚РёРµ Р»РѕРіРѕРІ: " + ex.Message, ex);
            ErrorRaised?.Invoke(this, "РќРµ СѓРґР°Р»РѕСЃСЊ РѕС‚РєСЂС‹С‚СЊ РїР°РїРєСѓ Р»РѕРіРѕРІ.");
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
            _logger.Error("РџРµСЂРµР·Р°РїСѓСЃРє РѕР±С…РѕРґР°: " + ex.Message, ex);
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
        SaveStatus = "РњРѕРё РїСЂРµСЃРµС‚С‹ СЃРѕС…СЂР°РЅРµРЅС‹";
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
            SaveStatus = "РЎРѕС…СЂР°РЅРµРЅРѕ";
            OnPropertyChanged(nameof(CurrentProfileText));
        }
        catch (Exception ex)
        {
            _logger.Error("РЎРѕС…СЂР°РЅРµРЅРёРµ РЅР°СЃС‚СЂРѕРµРє: " + ex.Message, ex);
            SaveStatus = "РћС€РёР±РєР° СЃРѕС…СЂР°РЅРµРЅРёСЏ: " + ex.Message;
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
            _logger.Error("Р§С‚РµРЅРёРµ Р»РѕРіРѕРІ: " + ex.Message, ex);
            return string.Empty;
        }
    }

    private void RefreshCommands()
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
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

