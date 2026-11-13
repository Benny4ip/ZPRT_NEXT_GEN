#nullable enable

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ZprtNextGen.App.Services;
using ZprtNextGen.App.ViewModels;
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

namespace ZprtNextGen.App;

/// <summary>
/// Точка входа: один экземпляр, права администратора, portable-движок, конфиг,
/// каталог пресетов, координатор подключения, трей и жизненный цикл окон.
/// </summary>
public partial class App : Application
{
    private const string ShowSignalName = @"Local\ZPRT_NEXT_GEN_show";

    private SingleInstanceGuard? _guard;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private DashboardViewModel? _dashboard;
    private SettingsViewModel? _settings;
    private ConnectionCoordinator? _coordinator;
    private HealthMonitor? _healthMonitor;
    private EngineProcessManager? _engineProcess;
    private EventWaitHandle? _showSignal;
    private Thread? _showSignalThread;
    private IAppLogger? _logger;
    private bool _shuttingDown;
    private bool _isHandlingException;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = AppStartupOptions.Parse(e.Args);

        // Права администратора нужны для WinDivert. Проверяем раньше мьютекса.
        if (!SingleInstanceGuard.IsElevated())
        {
            MessageBox.Show("Запустите приложение от имени администратора", "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        _guard = new SingleInstanceGuard("main");
        if (!_guard.IsOwner)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        StartShowSignalListener();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logsDirectory = Path.Combine(appData, "ZPRT_NEXT_GEN", "logs");
        var logger = new AppLogger(logsDirectory);
        _logger = logger;
        logger.Info(
            $"Запуск ZPRT NEXT GEN v{typeof(App).Assembly.GetName().Version}, аргументы: {string.Join(' ', e.Args)}.");

        InstallGlobalExceptionHandlers(logger, logsDirectory);

        try
        {
            Start(options, logger, logsDirectory);
        }
        catch (Exception ex)
        {
            logger.Error("Не удалось запустить приложение: " + ex, ex);
            MessageBox.Show(
                "Не удалось запустить ZPRT NEXT GEN:\n" + ex.Message +
                $"\n\nПодробности в журнале:\n{logsDirectory}",
                "ZPRT NEXT GEN",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Start(AppStartupOptions options, IAppLogger logger, string logsDirectory)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var embeddedEngine = PortableEngineInstaller.ReadEmbeddedEngine(typeof(App).Assembly);
        var isPortable = embeddedEngine is { Length: > 0 };

        if (options.PortableCheck)
        {
            RunPortableCheck(embeddedEngine, version, logger);
            return;
        }

        // Portable: распаковать встроенный движок в кэш %LOCALAPPDATA%.
        PortableEngineResult? portableEngine = null;
        if (isPortable)
        {
            var installer = new PortableEngineInstaller(embeddedEngine, version, logger: logger);
            portableEngine = installer.InstallAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (portableEngine is not null)
                logger.Info("Portable-движок: " + portableEngine.EngineRoot);
        }

        var baseDirectory = AppContext.BaseDirectory;
        var configStore = ConfigStore.CreateDefault();
        var config = configStore.Load();

        var presetsPath = portableEngine?.PresetsPath
            ?? Path.Combine(baseDirectory, "presets", "presets.json");
        var baseCatalog = LoadCatalog(presetsPath, logger);
        var catalog = PresetCatalogBuilder.Build(baseCatalog, config.CustomPresets);

        var resolveEngine = portableEngine is not null
            ? () => new EngineValidation(true, portableEngine.EngineRoot, [])
            : new Func<EngineValidation>(() => EngineLocator.Locate(config.Engine.Path, baseDirectory));

        var initialEngine = resolveEngine();
        if (!initialEngine.IsValid)
        {
            logger.Error(
                "Движок не найден. Отсутствуют файлы: " + string.Join(", ", initialEngine.MissingFiles) +
                $". EnginePath: '{config.Engine.Path ?? "не задан"}'.");
        }

        var connectivity = ConnectivityService.CreateDefault();
        var engineProcess = new EngineProcessManager();
        _engineProcess = engineProcess;

        var gate = new EngineOperationGate();
        var tester = new PresetTester(engineProcess, connectivity);
        var coordinator = new ConnectionCoordinator(new ConnectionCoordinatorDependencies(
            catalog,
            configStore,
            engineProcess,
            connectivity,
            connectivity,
            tester,
            gate,
            logger,
            resolveEngine,
            ConfirmStopForeign: _ => Task.FromResult(Confirm(
                "Обход уже запущен другим способом. Остановить его и запустить обход из этого приложения?")),
            Policy: new AutoSelectPolicy
            {
                StartupDelay = TimeSpan.FromSeconds(1.5),
                CheckTimeout = TimeSpan.FromSeconds(4),
                VerifyDelay = TimeSpan.FromMilliseconds(1500),
                MaxStartAttempts = 3,
                HealthFailures = config.Connection.HealthCheck.FailuresBeforeRetest,
                HealthCooldown = TimeSpan.FromSeconds(config.Connection.HealthCheck.RetestCooldownSeconds),
            }));
        _coordinator = coordinator;

        var dispatcher = new WpfDispatcher();
        var autostart = new ScheduledTaskAutostart(Environment.ProcessPath ?? string.Empty);
        var discordFix = DiscordFixService.CreateDefault(logger);

        var dashboard = new DashboardViewModel(new DashboardDependencies(
            coordinator,
            configStore,
            discordFix,
            logger,
            dispatcher,
            logsDirectory));
        _dashboard = dashboard;

        var settings = new SettingsViewModel(new SettingsDependencies(
            configStore,
            coordinator,
            baseCatalog,
            engineProcess,
            connectivity,
            autostart,
            discordFix,
            tester,
            GitHubReleaseClient.CreateDefault(),
            new EngineUpdater(HttpFileDownloader.CreateDefault()),
            new AppUpdater(HttpFileDownloader.CreateDefault()),
            logger,
            dispatcher,
            version,
            presetsPath,
            isPortable,
            path => EngineLocator.Locate(path, baseDirectory),
            coordinator.UpdateCatalog,
            Confirm,
            logsDirectory));
        _settings = settings;
        settings.SetResolvedEngineRoot(initialEngine.EngineRoot);

        // Главное окно.
        var mainWindow = new MainWindow(dashboard);
        _mainWindow = mainWindow;
        MainWindow = mainWindow;

        Icon? icon = null;
        try
        {
            var iconPath = Path.Combine(baseDirectory, "assets", "icon.ico");
            if (File.Exists(iconPath))
            {
                icon = new Icon(iconPath);
            }
            else
            {
                var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    icon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("Иконка приложения: " + ex.Message, ex);
        }

        _tray = new TrayService(
            icon,
            show: ShowMainWindow,
            toggle: () => dashboard.ToggleCommand.Execute(null),
            fixDiscord: () => dashboard.FixDiscordCommand.Execute(null),
            exit: ExitApplication);

        dashboard.SettingsRequested += (_, _) => ShowSettingsWindow();
        dashboard.NotificationRequested += (_, text) =>
            _tray?.ShowBalloon("ZPRT NEXT GEN", text);
        settings.RestartRequested += (_, _) => Shutdown();

        coordinator.StateChanged += (_, snapshot) =>
            dispatcher.Post(() => UpdateTrayState(snapshot));
        dashboard.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DashboardViewModel.IsFixingDiscord))
                UpdateTrayState(coordinator.Snapshot);
        };

        UpdateTrayState(coordinator.Snapshot);

        if (!options.StartInTray)
            ShowMainWindow();
        else
            _tray.ShowBalloon("ZPRT NEXT GEN", "Приложение запущено в трее.");

        _ = InitializeAsync(options, config, logger);
    }

    private async Task InitializeAsync(AppStartupOptions options, AppConfig config, IAppLogger logger)
    {
        if (_coordinator is null || _dashboard is null)
            return;

        try
        {
            await _coordinator.InitializeAsync(CancellationToken.None);
            _dashboard.Refresh();
            _settings?.Reload();

            var health = config.Connection.HealthCheck;
            if (health.Enabled)
            {
                _healthMonitor = new HealthMonitor(
                    TimeSpan.FromSeconds(Math.Clamp(health.IntervalSeconds, 10, 3600)),
                    ct => _coordinator.CheckHealthAsync(ct),
                    logger);
                _healthMonitor.Start();
            }

            if (options.AutoConnect)
            {
                var usesSavedProfile = config.Connection.Mode == ConnectionMode.Manual
                    || config.Connection.AutoProfile is not null;

                if (!usesSavedProfile)
                {
                    _tray?.ShowBalloon("ZPRT NEXT GEN",
                        "Нет сохранённого профиля. Открой приложение и нажми «Включить».");
                    return;
                }

                logger.Info("Автозапуск: поднимаем сохранённый обход.");
                await _coordinator.ConnectAsync(CancellationToken.None);
                return;
            }

            // Первый запуск: если нет сохранённого профиля и не ручной режим, сразу запускаем тестирование всех 22 обходов
            if (!options.StartInTray && config.Connection.Mode == ConnectionMode.Auto && config.Connection.AutoProfile is null)
            {
                logger.Info("Первый запуск приложения: автоматический старт подбора обхода.");
                await _coordinator.ConnectAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.Error("Инициализация подключения: " + ex.Message, ex);
        }
    }

    /// <summary>Показать главное окно на переднем плане.</summary>
    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        _mainWindow.Activate();

        var topmost = _mainWindow.Topmost;
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = topmost;
        _mainWindow.Focus();
    }

    private void ShowSettingsWindow()
    {
        if (_settings is null)
            return;

        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settings);
        if (_mainWindow is { IsVisible: true })
            window.Owner = _mainWindow;

        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.Show();
        window.Activate();
    }

    private void UpdateTrayState(ConnectionSnapshot snapshot)
    {
        if (_tray is null)
            return;

        var tooltip = snapshot.State switch
        {
            ConnectionState.Connected => string.IsNullOrWhiteSpace(snapshot.PresetName)
                ? "ZPRT NEXT GEN — обход включён"
                : $"ZPRT NEXT GEN — обход включён ({snapshot.PresetName})",
            ConnectionState.Selecting => "ZPRT NEXT GEN — подбираем обход",
            ConnectionState.Connecting => "ZPRT NEXT GEN — подключаемся",
            ConnectionState.Stopping => "ZPRT NEXT GEN — выключаем обход",
            ConnectionState.Error => "ZPRT NEXT GEN — нужна помощь",
            _ => "ZPRT NEXT GEN — обход выключен",
        };

        var toggleText = snapshot.State switch
        {
            ConnectionState.Connected => "Выключить обход",
            ConnectionState.Selecting or ConnectionState.Connecting => "Отменить подбор",
            _ => "Включить обход",
        };

        _tray.UpdateStatus(tooltip);
        _tray.UpdateMenu(toggleText, snapshot.IsBusy, _dashboard?.IsFixingDiscord ?? false);
    }

    private void ExitApplication()
    {
        if (_shuttingDown)
            return;

        _shuttingDown = true;
        _mainWindow?.RequestExit();

        if (_mainWindow is null)
            Shutdown();
    }

    private static PresetCatalog LoadCatalog(string path, IAppLogger logger)
    {
        if (!File.Exists(path))
        {
            logger.Error("Каталог пресетов не найден: " + path);
            return new PresetCatalog(0, string.Empty, string.Empty, [], []);
        }

        try
        {
            return PresetCatalogLoader.Load(path);
        }
        catch (Exception ex)
        {
            logger.Error($"Каталог пресетов '{path}' повреждён: {ex.Message}", ex);
            return new PresetCatalog(0, string.Empty, string.Empty, [], []);
        }
    }

    private void RunPortableCheck(byte[]? embeddedEngine, string version, IAppLogger logger)
    {
        if (embeddedEngine is not { Length: > 0 })
        {
            MessageBox.Show(
                "В этой сборке нет встроенного движка (Standard). Portable-проверка не требуется.",
                "ZPRT NEXT GEN",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        var cacheRoot = Path.Combine(
            Path.GetTempPath(), "ZprtNextGen-portable-check-" + Guid.NewGuid().ToString("N")[..8]);

        var ok = false;
        string message;

        try
        {
            var installer = new PortableEngineInstaller(embeddedEngine, version, cacheRoot, logger);
            var result = installer.InstallAsync(CancellationToken.None).GetAwaiter().GetResult();
            var winws = result is null ? null : Path.Combine(result.EngineRoot, "bin", "winws.exe");
            ok = winws is not null && File.Exists(winws)
                && File.Exists(Path.Combine(result!.EngineRoot, "presets", "presets.json"));

            message = ok
                ? "Portable-движок распакован и проверен: " + result!.EngineRoot
                : "Portable-движок распакован не полностью.";
        }
        catch (Exception ex)
        {
            logger.Error("Portable-проверка: " + ex.Message, ex);
            message = "Portable-проверка не удалась: " + ex.Message;
        }
        finally
        {
            try
            {
                if (Directory.Exists(cacheRoot))
                    Directory.Delete(cacheRoot, recursive: true);
            }
            catch (Exception ex)
            {
                logger.Error("Очистка временной папки проверки: " + ex.Message, ex);
            }
        }

        Environment.ExitCode = ok ? 0 : 1;
        Shutdown(ok ? 0 : 1);
    }

    /// <summary>Слушать запросы второго экземпляра «показать окно».</summary>
    private void StartShowSignalListener()
    {
        try
        {
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        }
        catch (Exception)
        {
            return;
        }

        _showSignalThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _showSignal.WaitOne();
                }
                catch
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(ShowMainWindow), DispatcherPriority.Normal);
            }
        })
        {
            IsBackground = true,
            Name = "ZPRT_NEXT_GEN: показать окно",
        };
        _showSignalThread.Start();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
            signal.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            MessageBox.Show("ZPRT NEXT GEN уже запущен", "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static bool Confirm(string message)
    {
        var result = MessageBox.Show(message, "ZPRT NEXT GEN",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private void InstallGlobalExceptionHandlers(IAppLogger logger, string logsDirectory)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (_isHandlingException)
                return;

            _isHandlingException = true;
            try
            {
                var ex = args.Exception;
                logger.Error("Необработанное исключение WPF: " + ex, ex);
                var root = ex.GetBaseException();
                var detail = ReferenceEquals(root, ex) ? ex.Message : $"{ex.Message}\n\nПричина: {root.Message}";
                MessageBox.Show(
                    $"ZPRT NEXT GEN столкнулся с ошибкой:\n{ex.GetType().Name}: {detail}" +
                    $"\n\nПодробности в журнале:\n{logsDirectory}",
                    "ZPRT NEXT GEN — ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isHandlingException = false;
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                logger.Error("Необработанное исключение домена: " + ex, ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            logger.Error("Необработанное исключение задачи: " + args.Exception, args.Exception);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_coordinator is not null)
        {
            try
            {
                Task.Run(() => _coordinator.DisconnectAsync(CancellationToken.None))
                    .Wait(TimeSpan.FromSeconds(4));
            }
            catch (Exception ex)
            {
                _logger?.Error("Остановка при выходе: " + ex.Message, ex);
            }
        }

        if (_healthMonitor is not null)
        {
            try
            {
                _healthMonitor.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // Монитор уже мог быть остановлен.
            }
        }

        _engineProcess?.Dispose();
        _tray?.Dispose();
        _showSignal?.Dispose();
        _guard?.Dispose();

        base.OnExit(e);
    }
}



