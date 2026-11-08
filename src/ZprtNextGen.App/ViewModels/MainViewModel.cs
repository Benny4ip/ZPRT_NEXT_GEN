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

/// <summary>
/// Модель представления главного окна: тонкая обёртка над асинхронными методами
/// collaborators. Весь пользовательский текст — на русском языке.
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private const int MaxLogLines = 500;

    private readonly PresetCatalog _catalog;
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

    public MainViewModel(MainViewModelDependencies dependencies)
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

        _toggleCommand = new AsyncRelayCommand(async () => await ToggleAsync(), () => !IsBusy && CanStart);
        _testConnectionCommand = new AsyncRelayCommand(async () => await TestConnectionAsync(), () => !IsBusy);
        _selectPresetCommand = new RelayCommand(OnSelectPreset, _ => !IsBusy);
        _openLogsCommand = new RelayCommand(_ => OnOpenLogs());
        _toggleAutostartCommand = new AsyncRelayCommand(async () => await ToggleAutostartAsync(), () => !IsBusy);
        _exitCommand = new RelayCommand(_ => Application.Current?.Shutdown(), _ => Application.Current is not null);

        _engineProcess.OutputReceived += OnOutputReceived;
    }

    public ICommand ToggleCommand => _toggleCommand;

    public ICommand TestConnectionCommand => _testConnectionCommand;

    public ICommand SelectPresetCommand => _selectPresetCommand;

    public ICommand OpenLogsCommand => _openLogsCommand;

    public ICommand ToggleAutostartCommand => _toggleAutostartCommand;

    public ICommand ExitCommand => _exitCommand;

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
        if (IsBusy || !CanStart)
            return;

        try
        {
            IsBusy = true;

            if (IsRunning)
            {
                await _engineProcess.StopAsync(CancellationToken.None);
                AnimationState = MeditationState.Stopped;
                StatusText = "Движок остановлен";
                return;
            }

            if (_engineProcess.IsForeignInstanceRunning())
            {
                if (!_confirm(
                        "Обход уже запущен другим способом. Остановите его вручную или закройте оригинальное окно."))
                {
                    StatusText = "Обход уже запущен извне.";
                    return;
                }

                StatusText = "Остановите обход вручную";
                return;
            }

            var preset = _selectedPreset
                ?? throw new InvalidOperationException("Пресет не выбран.");
            var root = _engine.EngineRoot
                ?? throw new InvalidOperationException("Корень движка не определён.");

            var arguments = PresetArgumentBuilder.Build(preset, root, _settings.GameFilter);
            var binDirectory = Path.Combine(root, "bin");

            await _engineProcess.StartAsync(
                Path.Combine(binDirectory, "winws.exe"),
                arguments,
                binDirectory,
                CancellationToken.None);

            await TestConnectionAsync();
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
    public async Task TestConnectionAsync()
    {
        try
        {
            var report = await _connectivity.CheckAsync(CancellationToken.None);

            _dispatcher.Post(() =>
            {
                ConnectivityRows = report.Results
                    .Select(result => new ConnectivityRowViewModel
                    {
                        Name = result.Target.DisplayName,
                        Success = result.Success,
                        ElapsedMs = result.ElapsedMs,
                        Error = result.Error
                    })
                    .ToList();

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
    }

    private void SaveSettings() => _settingsStore.Save(_settings);

    private void HandleError(Exception ex)
    {
        _logger.Error(ex.Message, ex);
        AnimationState = MeditationState.Error;
        StatusText = $"Ошибка: {ex.Message}";
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
