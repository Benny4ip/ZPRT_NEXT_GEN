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

        // 3. Настройки.
        var settingsStore = SettingsStore.CreateDefault();
        var settings = settingsStore.Load();

        // 4. Каталог пресетов.
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "presets", "presets.json");
        PresetCatalog catalog;
        if (!File.Exists(catalogPath))
        {
            MessageBox.Show($"Каталог пресетов не найден: {catalogPath}", "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Error);
            catalog = new PresetCatalog(0, string.Empty, string.Empty, [], []);
        }
        else
        {
            catalog = PresetCatalogLoader.Load(catalogPath);
        }

        // 5. Движок.
        var engine = EngineLocator.Locate(settings.EnginePath, AppContext.BaseDirectory);
        if (!engine.IsValid)
        {
            MessageBox.Show(
                $"Движок не найден. Отсутствуют файлы: {string.Join(", ", engine.MissingFiles)}",
                "ZPRT NEXT GEN",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // 6. Сервисы.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var connectivity = ConnectivityService.CreateDefault();
        var engineProcess = new EngineProcessManager();
        var autostart = new ScheduledTaskAutostart(Environment.ProcessPath ?? string.Empty);
        var logger = new AppLogger(Path.Combine(appData, "logs"));
        var dispatcher = new WpfDispatcher();

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

        _vm = new MainViewModel(dependencies);
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
            icon,
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
