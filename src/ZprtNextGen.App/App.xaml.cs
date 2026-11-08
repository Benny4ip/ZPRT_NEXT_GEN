#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
/// Interaction logic for App.xaml.
/// Временная композиция (заглушка): будет заменена реальной в Task 17.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dependencies = new MainViewModelDependencies(
            Catalog: StubCatalog,
            Settings: new StubSettingsStore(),
            Engine: new EngineValidation(true, @"C:\winws", []),
            EngineProcess: new StubEngineProcessManager(),
            Connectivity: new StubConnectivityService(),
            Autostart: new StubAutostartService(),
            Logger: new StubLogger(),
            Dispatcher: new StubUiDispatcher(),
            Confirm: _ => true);

        var mainWindow = new MainWindow(new MainViewModel(dependencies));
        mainWindow.Show();
    }

    private static readonly PresetCatalog StubCatalog = new(
        1,
        "winws",
        "1.0",
        new List<PresetGroup> { new("general", "Общий") },
        new List<Preset> { new("general", "general", "Общий", "Стандартный набор правил", string.Empty) });

    private sealed class StubSettingsStore : ISettingsStore
    {
        public string Path =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZPRT_NEXT_GEN", "settings.stub.json");

        public AppSettings Load() => new();

        public void Save(AppSettings settings) { }
    }

    private sealed class StubEngineProcessManager : IEngineProcessManager
    {
        public EngineStatus Status => EngineStatus.Stopped;
        public int? OwnedProcessId => null;
        public event EventHandler<EngineStatusChangedEventArgs> StatusChanged = delegate { };
        public event EventHandler<string> OutputReceived = delegate { };

        public Task StartAsync(string executablePath, string arguments, string workingDirectory, CancellationToken ct) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        public bool IsForeignInstanceRunning() => false;

        public void Dispose() { }
    }

    private sealed class StubConnectivityService : IConnectivityService
    {
        public Task<ConnectivityReport> CheckAsync(CancellationToken ct) =>
            Task.FromResult(new ConnectivityReport(Array.Empty<ConnectivityResult>()));
    }

    private sealed class StubAutostartService : IAutostartService
    {
        public bool IsEnabled() => false;
        public void Enable() { }
        public void Disable() { }
    }

    private sealed class StubLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class StubUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
