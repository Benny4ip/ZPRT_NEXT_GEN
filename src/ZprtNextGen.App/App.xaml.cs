#nullable enable

using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ZprtNextGen.App.Services;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;
using ZprtNextGen.Core.Startup;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App;

/// <summary>
/// Точка входа приложения: single instance, проверка прав администратора,
/// загрузка настроек и каталога пресетов, композиция сервисов, жизненный цикл окна и трея.
/// </summary>
public partial class App : Application
{
    private SingleInstanceGuard? _guard;
    private TrayService? _tray;
    private MainViewModel? _vm;
    private MainWindow? _mainWindow;
    private bool _vmShutdown;
    private bool _isHandlingException;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Один экземпляр приложения.
        _guard = new SingleInstanceGuard("main");
        if (!_guard.IsOwner)
        {
            MessageBox.Show("ZPRT NEXT GEN уже запущен", "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 2. Права администратора (требуются для WinDivert).
        if (!SingleInstanceGuard.IsElevated())
        {
            MessageBox.Show("Запустите приложение от имени администратора", "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        // 3. Логгер (раньше, чтобы фиксировать ошибки старта и пересоздавать папку логов).
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logger = new AppLogger(Path.Combine(appData, "ZPRT_NEXT_GEN", "logs"));
        logger.Info($"Запуск ZPRT NEXT GEN v{typeof(App).Assembly.GetName().Version}, base='{AppContext.BaseDirectory}'.");

        // 4. Настройки.
        var settingsStore = SettingsStore.CreateDefault();
        var settings = settingsStore.Load();

        // 5. Каталог пресетов.
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "presets", "presets.json");
        PresetCatalog catalog;
        if (!File.Exists(catalogPath))
        {
            logger.Error($"Каталог пресетов не найден: {catalogPath}");
            MessageBox.Show($"Каталог пресетов не найден: {catalogPath}", "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Error);
            catalog = new PresetCatalog(0, string.Empty, string.Empty, [], []);
        }
        else
        {
            catalog = PresetCatalogLoader.Load(catalogPath);
        }

        // 6. Движок.
        var engine = EngineLocator.Locate(settings.EnginePath, AppContext.BaseDirectory);
        if (!engine.IsValid)
        {
            logger.Error($"Движок не найден. Отсутствуют файлы: {string.Join(", ", engine.MissingFiles)}. Искали от '{AppContext.BaseDirectory}'.");
            MessageBox.Show(
                $"Движок не найден. Отсутствуют файлы: {string.Join(", ", engine.MissingFiles)}",
                "ZPRT NEXT GEN",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // 7. Сервисы.
        var connectivity = ConnectivityService.CreateDefault();
        var engineProcess = new EngineProcessManager();
        var autostart = new ScheduledTaskAutostart(Environment.ProcessPath ?? string.Empty);
        var dispatcher = new WpfDispatcher();

        // Глобальные обработчики необработанных исключений: вместо молчаливого вылета
        // показываем окно с текстом ошибки и пишем в лог.
        DispatcherUnhandledException += (s, args) =>
        {
            args.Handled = true;
            if (_isHandlingException)
                return;
            _isHandlingException = true;
            try
            {
                var ex = args.Exception;
                logger.Error($"Необработанное исключение WPF: {ex}", ex);
                var root = ex.GetBaseException();
                var detail = ReferenceEquals(root, ex) ? ex.Message : $"{ex.Message}\n\nПричина: {root.Message}";
                MessageBox.Show($"ZPRT NEXT GEN stopped working:\n{ex.GetType().Name}: {detail}\n\nПодробности в журнале:\n{Path.Combine(appData, "ZPRT_NEXT_GEN", "logs")}", "ZPRT NEXT GEN — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isHandlingException = false;
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (_isHandlingException)
                return;
            _isHandlingException = true;
            try
            {
                var ex = (Exception)args.ExceptionObject;
                logger.Error($"Необработанное исключение домена: {ex}", ex);
                try { MessageBox.Show($"ZPRT NEXT GEN crashed:\n{ex.GetType().Name}: {ex.Message}", "ZPRT NEXT GEN — ошибка", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            }
            finally
            {
                _isHandlingException = false;
            }
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            args.SetObserved();
            logger.Error($"Необработанное исключение задачи: {args.Exception}", args.Exception);
        };

        // 7. Модель представления, главное окно, трей.
        var dependencies = new MainViewModelDependencies(
            Catalog: catalog,
            Settings: settingsStore,
            Engine: engine,
            EngineProcess: engineProcess,
            Connectivity: connectivity,
            Autostart: autostart,
            Logger: logger,
            Dispatcher: dispatcher,
            Confirm: ConfirmEngineAction,
            LocateEngine: path => EngineLocator.Locate(path, AppContext.BaseDirectory));

        var maintenance = new MaintenanceDependencies(
            GitHubReleaseClient.CreateDefault(),
            new EngineUpdater(HttpFileDownloader.CreateDefault()),
            new AppUpdater(HttpFileDownloader.CreateDefault()),
            new PresetTester(engineProcess, connectivity),
            typeof(App).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            catalogPath);

        _vm = new MainViewModel(dependencies, maintenance);
        _vm.RestartRequested += (_, _) => Application.Current.Shutdown();
        _mainWindow = new MainWindow(_vm);

        Icon? icon = null;
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico");
            icon = new Icon(iconPath);
        }
        catch (Exception ex)
        {
            logger?.Error($"Не удалось загрузить иконку приложения: {ex.Message}", ex);
        }

        _tray = new TrayService(
            icon!,
            toggle: () =>
            {
                try
                {
                    _ = _vm!.ToggleAsync();
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message, ex);
                }
            },
            show: () =>
            {
                _mainWindow!.Show();
                if (_mainWindow.WindowState == WindowState.Minimized)
                    _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            },
            exit: () => _mainWindow!.RequestExit());

        _mainWindow.Show();
    }

    private static bool ConfirmEngineAction(string message)
    {
        var result = MessageBox.Show(message, "ZPRT NEXT GEN",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_vm is not null && !_vmShutdown)
        {
            _vmShutdown = true;
            try
            {
                // Не блокируем UI-поток при выходе: процесс всё равно завершается.
                _ = _vm.ShutdownAsync(_vm.IsRunning);
            }
            catch
            {
                // Завершение при выходе: ошибки уже зарегистрированы логгером.
            }
        }

        _tray?.Dispose();
        _guard?.Dispose();

        base.OnExit(e);
    }
}
