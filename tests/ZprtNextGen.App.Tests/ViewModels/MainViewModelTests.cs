using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZprtNextGen.App.Tests.Fakes;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Presentation;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Settings;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.Tests.ViewModels;

public class MainViewModelTests
{
    private const string PresetArguments = "--wf-tcp=80,{{GameFilterTCP}}";

    private static PresetCatalog Catalog(params Preset[] presets)
    {
        var groups = presets
            .Select(p => p.GroupId)
            .Distinct()
            .Select(id => new PresetGroup(id, "Группа " + id))
            .ToList();

        return new PresetCatalog(1, "winws", "1.0", groups, presets.ToList());
    }

    private static Preset Preset(string id, string groupId, string arguments) =>
        new(id, groupId, "Пресет " + id, "Описание", arguments);

    private static MainViewModelDependencies BuildDependencies(
        AppSettings? settings = null,
        PresetCatalog? catalog = null,
        EngineValidation? engine = null,
        FakeEngineProcess? engineProcess = null,
        FakeConnectivity? connectivity = null,
        FakeAutostart? autostart = null,
        FakeLogger? logger = null,
        Func<string, bool>? confirm = null,
        Func<string?, EngineValidation>? locateEngine = null)
    {
        catalog ??= Catalog(
            Preset("general", "g1", "--wf-tcp=80,12"),
            Preset("general-alt5", "g2", PresetArguments));

        return new MainViewModelDependencies(
            catalog,
            new FakeSettingsStore { Settings = settings ?? new AppSettings() },
            engine ?? new EngineValidation(true, @"C:\E", []),
            engineProcess ?? new FakeEngineProcess(),
            connectivity ?? new FakeConnectivity(),
            autostart ?? new FakeAutostart(),
            logger ?? new FakeLogger(),
            new ImmediateDispatcher(),
            confirm ?? (_ => true),
            locateEngine);
    }

    private static (MainViewModel Vm, FakeSettingsStore Store, FakeEngineProcess EngineProcess,
        FakeConnectivity Connectivity, FakeAutostart Autostart, FakeLogger Logger) CreateVm(
        MainViewModelDependencies? dependencies = null,
        MaintenanceDependencies? maintenance = null)
    {
        var deps = dependencies ?? BuildDependencies();
        var store = (FakeSettingsStore)deps.Settings;
        var engineProcess = (FakeEngineProcess)deps.EngineProcess;
        var connectivity = (FakeConnectivity)deps.Connectivity;
        var autostart = (FakeAutostart)deps.Autostart;
        var logger = (FakeLogger)deps.Logger;
        var vm = new MainViewModel(deps, maintenance);
        return (vm, store, engineProcess, connectivity, autostart, logger);
    }

    [Fact]
    public void Initialize_SelectsSavedPresetAndBuildsGroups()
    {
        var (vm, store, _, _, _, _) = CreateVm(BuildDependencies(
            settings: new AppSettings { PresetId = "general-alt5", CollapsedGroups = ["g1"] }));

        vm.InitializeAsync().GetAwaiter().GetResult();

        Assert.Equal("general-alt5", vm.SelectedPreset?.Id);
        Assert.Equal(2, vm.Groups.Count);
        Assert.False(vm.Groups[0].IsExpanded); // g1 свёрнута в настройках
        Assert.True(vm.Groups[1].IsExpanded);
        Assert.Single(vm.Groups[0].Presets);
        Assert.Single(vm.Groups[1].Presets);
    }

    [Fact]
    public void Initialize_InvalidEngine_CanStartFalse()
    {
        var engine = new EngineValidation(false, null, new[] { @"bin\winws.exe", @"bin\WinDivert.dll" });
        var (vm, _, _, _, _, _) = CreateVm(BuildDependencies(engine: engine));

        vm.InitializeAsync().GetAwaiter().GetResult();

        Assert.False(vm.CanStart);
        Assert.True(vm.StatusText.Contains("движок", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChangeEnginePath_Valid_UpdatesEngineAndPersists()
    {
        var (vm, store, engineProcess, _, _, _) = CreateVm(BuildDependencies(
            locateEngine: _ => new EngineValidation(true, @"C:\NewEngine", [])));

        vm.ChangeEnginePathAsync(@"C:\NewEngine").GetAwaiter().GetResult();

        Assert.Equal(@"C:\NewEngine", vm.EnginePath);
        Assert.True(vm.CanStart);
        Assert.Equal("Движок найден", vm.StatusText);
        Assert.Equal(@"C:\NewEngine", store.Saved.Last().EnginePath);
    }

    [Fact]
    public void ChangeEnginePath_Invalid_KeepsCurrentEngine()
    {
        var (vm, store, _, _, _, _) = CreateVm(BuildDependencies(
            locateEngine: _ => new EngineValidation(false, null, new[] { @"bin\winws.exe" })));

        var beforePath = vm.EnginePath;
        var beforeCanStart = vm.CanStart;

        vm.ChangeEnginePathAsync(@"C:\Bad").GetAwaiter().GetResult();

        Assert.Equal(beforePath, vm.EnginePath);
        Assert.Equal(beforeCanStart, vm.CanStart);
        Assert.Contains("Папка не подходит", vm.StatusText);
        Assert.Empty(store.Saved); // сохранение не выполнялось
    }

    [Fact]
    public void Toggle_WhenStopped_StartsEngineWithResolvedArguments()
    {
        var (vm, _, engineProcess, connectivity, _, _) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();
        vm.SelectPresetCommand.Execute(Catalog(Preset("general-alt5", "g2", PresetArguments)).Presets.First());

        vm.GameFilter = GameFilterMode.Tcp;
        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Single(engineProcess.Starts);
        var start = engineProcess.Starts[0];
        Assert.Equal(@"C:\E\bin\winws.exe", start.ExecutablePath);
        Assert.Contains("--wf-tcp=80,1024-65535", start.Arguments);
        Assert.Equal(@"C:\E\bin", start.WorkingDirectory);
        Assert.Equal(1, connectivity.CheckCalls);
    }

    [Fact]
    public void Toggle_WhenRunning_StopsEngine()
    {
        var (vm, _, engineProcess, _, _, _) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();
        engineProcess.Status = EngineStatus.Running;

        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Equal(1, engineProcess.StopCalls);
        Assert.Equal(MeditationState.Stopped, vm.AnimationState);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Toggle_StartFailure_SetsErrorStateAndLogs()
    {
        var (vm, _, engineProcess, _, _, logger) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();
        engineProcess.ShouldThrowOnStart = true;

        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Equal(MeditationState.Error, vm.AnimationState);
        Assert.Contains("Не удалось запустить", vm.StatusText);
        Assert.Single(logger.Errors);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Toggle_AfterStart_RunsConnectivityAndConnects()
    {
        var (vm, _, engineProcess, connectivity, _, _) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();

        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Equal(1, connectivity.CheckCalls);
        Assert.Equal(MeditationState.Connected, vm.AnimationState);
        Assert.True(vm.IsRunning);
    }

    [Fact]
    public void Toggle_AfterStart_ConnectivityFail_SetsError()
    {
        var (vm, _, _, connectivity, _, _) = CreateVm(BuildDependencies(
            connectivity: new FakeConnectivity
            {
                Report = new ConnectivityReport(new[]
                {
                    new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), false, 100, "timeout"),
                    new ConnectivityResult(new ConnectivityTarget("s", "Secondary", "https://example.net", false), true, 50, null)
                })
            }));

        vm.InitializeAsync().GetAwaiter().GetResult();
        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Equal(MeditationState.Error, vm.AnimationState);
        Assert.Contains("Связь не подтверждена", vm.StatusText);
    }

    [Fact]
    public void TestConnection_WhenRunning_UpdatesRows()
    {
        var (vm, _, engineProcess, connectivity, _, _) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();
        engineProcess.Status = EngineStatus.Running;

        connectivity.Report = new ConnectivityReport(new[]
        {
            new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), false, 100, "timeout"),
            new ConnectivityResult(new ConnectivityTarget("s", "Secondary", "https://example.net", false), true, 50, null)
        });

        vm.TestConnectionAsync().GetAwaiter().GetResult();

        Assert.Equal(2, vm.ConnectivityRows.Count);
        Assert.Equal("Primary", vm.ConnectivityRows[0].Name);
        Assert.False(vm.ConnectivityRows[0].Success);
        Assert.Equal("Secondary", vm.ConnectivityRows[1].Name);
        Assert.True(vm.ConnectivityRows[1].Success);
        // Связь подтверждена только при успехе первичной цели.
        Assert.Equal(MeditationState.Error, vm.AnimationState);
    }

    [Fact]
    public void SelectPreset_PersistsToSettings()
    {
        var (vm, store, _, _, _, _) = CreateVm(BuildDependencies(
            settings: new AppSettings { PresetId = "general" }));
        vm.InitializeAsync().GetAwaiter().GetResult();

        vm.SelectPresetCommand.Execute(Catalog(Preset("general-alt5", "g2", PresetArguments))
            .Presets.First(p => p.Id == "general-alt5"));

        Assert.Equal("general-alt5", vm.SelectedPreset?.Id);
        Assert.Equal("general-alt5", store.Saved.Last().PresetId);
    }

    [Fact]
    public void LogLines_CappedAt500()
    {
        var (vm, _, engineProcess, _, _, _) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();

        for (var i = 0; i < 600; i++)
            engineProcess.RaiseOutput($"line {i}");

        Assert.Equal(500, vm.LogLines.Count);
        Assert.Equal("line 100", vm.LogLines.First());
        Assert.Equal("line 599", vm.LogLines.Last());
    }

    [Fact]
    public void Toggle_ForeignProcessAndUserDeclines_DoesNotStart()
    {
        var (vm, _, engineProcess, _, _, _) = CreateVm(BuildDependencies(
            confirm: new ConfirmAlways(false).Invoke));
        var engineProcessFake = (FakeEngineProcess)engineProcess;
        engineProcessFake.IsForeignInstanceRunningResult = true;

        vm.InitializeAsync().GetAwaiter().GetResult();
        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Empty(engineProcessFake.Starts);
        Assert.Contains("извне", vm.StatusText);
    }

    [Fact]
    public void Toggle_ForeignProcessAndUserAccepts_KillsForeignAndStarts()
    {
        var (vm, _, engineProcess, _, _, _) = CreateVm(BuildDependencies(
            confirm: new ConfirmAlways(true).Invoke));
        var engineProcessFake = (FakeEngineProcess)engineProcess;
        engineProcessFake.IsForeignInstanceRunningResult = true;

        vm.InitializeAsync().GetAwaiter().GetResult();
        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.Equal(1, engineProcessFake.KillForeignCalls);
        var start = Assert.Single(engineProcessFake.Starts);
        Assert.EndsWith("winws.exe", start.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toggle_Start_DoesNotWaitForConnectivity()
    {
        var gate = new TaskCompletionSource<ConnectivityReport>();
        var connectivity = new FakeConnectivity { Gate = gate };
        var (vm, _, _, _, _, _) = CreateVm(BuildDependencies(connectivity: connectivity));
        vm.InitializeAsync().GetAwaiter().GetResult();

        var toggle = vm.ToggleAsync();

        Assert.True(toggle.Wait(TimeSpan.FromSeconds(5)), "ToggleAsync не должен ждать проверку связи");
        Assert.True(vm.IsRunning);
        Assert.False(vm.IsBusy);
        Assert.Equal(1, connectivity.CheckCalls);

        gate.SetResult(connectivity.Report);
    }

    [Fact]
    public void Toggle_Start_NotifiesIsRunningChange()
    {
        var (vm, _, _, _, _, _) = CreateVm();
        vm.InitializeAsync().GetAwaiter().GetResult();

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ToggleAsync().GetAwaiter().GetResult();

        Assert.True(vm.IsRunning);
        Assert.Contains(nameof(MainViewModel.IsRunning), raised);
    }

    [Fact]
    public void ToggleAutostart_On_PersistsAndRevertsOnFailure()
    {
        var autostart = new FakeAutostart { EnableException = new System.InvalidOperationException(" boom ") };
        var (vm, store, _, _, _, logger) = CreateVm(BuildDependencies(autostart: autostart));
        vm.InitializeAsync().GetAwaiter().GetResult();

        vm.ToggleAutostartCommand.Execute(null);

        Assert.False(vm.IsAutostartEnabled); // откат UI
        Assert.False(store.Saved.Last().Autostart);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public void HandleError_RaisesErrorRaisedWithMessage()
    {
        var (vm, _, _, _, _, _) = CreateVm(BuildDependencies(
            locateEngine: _ => throw new InvalidOperationException("boom")));

        string? captured = null;
        vm.ErrorRaised += (_, msg) => captured = msg;

        vm.ChangeEnginePathAsync(@"C:\X").GetAwaiter().GetResult();

        Assert.Equal("boom", captured);
        Assert.Equal("boom", vm.LastErrorMessage);
    }

    [Fact]
    public void CheckUpdatesAsync_NewerEngineAvailable_ReportsInStatus()
    {
        var gitHub = new FakeGitHubReleaseClient
        {
            Releases =
            {
                ["Flowseal/zapret-discord-youtube"] = new ReleaseInfo("1.10.4", "1.10.4", null, []),
                ["Benny4ip/ZPRT_NEXT_GEN"] = new ReleaseInfo("0.2.0", "0.2.0", null, []),
            }
        };
        var deps = BuildDependencies();
        var (vm, _, _, _, _, _) = CreateVm(deps, new MaintenanceDependencies(
            gitHub, new FakeEngineUpdater(), new FakeAppUpdater(), new FakePresetTester(), "0.1.0", @"C:\P\presets.json"));
        vm.InitializeAsync().GetAwaiter().GetResult();

        vm.CheckUpdatesAsync().GetAwaiter().GetResult();

        Assert.Contains("1.10.4", vm.UpdateStatusText);
        Assert.Contains("0.2.0", vm.UpdateStatusText);
        Assert.Equal("0.1.0", vm.AppVersion);
    }

    [Fact]
    public void CheckUpdatesAsync_EverythingCurrent_ReportsCurrent()
    {
        var gitHub = new FakeGitHubReleaseClient
        {
            Releases =
            {
                ["Flowseal/zapret-discord-youtube"] = new ReleaseInfo("1.0", "1.0", null, []),
                ["Benny4ip/ZPRT_NEXT_GEN"] = new ReleaseInfo("0.1.0", "0.1.0", null, []),
            }
        };
        var deps = BuildDependencies();
        var (vm, _, _, _, _, _) = CreateVm(deps, new MaintenanceDependencies(
            gitHub, new FakeEngineUpdater(), new FakeAppUpdater(), new FakePresetTester(), "0.1.0", @"C:\P\presets.json"));
        vm.InitializeAsync().GetAwaiter().GetResult();

        vm.CheckUpdatesAsync().GetAwaiter().GetResult();

        Assert.Contains("актуален", vm.UpdateStatusText);
    }

    [Fact]
    public async Task TestAllPresetsAsync_FillsResultsAndReportsStatus()
    {
        var tester = new FakePresetTester
        {
            Results =
            {
                new PresetTestResult("general-alt5", "Альтернатива 5", true, 120, null),
                new PresetTestResult("general-alt6", "Альтернатива 6", false, 50, "таймаут"),
            }
        };
        var catalog = new PresetCatalog(
            1, "winws", "1.0",
            new[] { new PresetGroup("general", "Обычные") },
            new[]
            {
                new Preset("general-alt5", "general", "Альтернатива 5", "Описание", "--wf-tcp=80"),
                new Preset("general-alt6", "general", "Альтернатива 6", "Описание", "--wf-tcp=443"),
            });
        var deps = BuildDependencies(catalog: catalog);
        var (vm, _, engineProcess, _, _, _) = CreateVm(deps, new MaintenanceDependencies(
            new FakeGitHubReleaseClient(), new FakeEngineUpdater(), new FakeAppUpdater(), tester, "0.1.0", @"C:\P\presets.json"));
        vm.InitializeAsync().GetAwaiter().GetResult();

        await vm.TestAllPresetsAsync();

        Assert.Equal(2, vm.PresetTestResults.Count);
        // После сортировки по пингу — первым самый быстрый (50 мс — Альтернатива 6)
        Assert.Equal("Альтернатива 6", vm.PresetTestResults[0].Name);
        Assert.False(vm.PresetTestResults[0].Success);
        Assert.Equal(50, vm.PresetTestResults[0].ElapsedMs);
        Assert.Equal("Альтернатива 5", vm.PresetTestResults[1].Name);
        Assert.True(vm.PresetTestResults[1].Success);
        Assert.Equal(120, vm.PresetTestResults[1].ElapsedMs);
        Assert.Equal(2, tester.LastPresets.Count);
        Assert.All(tester.LastPresets, p => Assert.True(p.Id.StartsWith("general-alt")));
        Assert.Equal(@"C:\E", tester.LastEngineRoot);
        Assert.False(vm.IsTestingPresets);
        Assert.Equal("Проверено 2, успешно 1.", vm.PresetTestStatus);
    }

    [Fact]
    public async Task CancelPresetTestCommand_CancelsTesterToken()
    {
        var tester = new FakePresetTester { Hang = true };
        var catalog = new PresetCatalog(
            1, "winws", "1.0",
            new[] { new PresetGroup("general", "Обычные") },
            new[] { new Preset("general-alt5", "general", "Альтернатива 5", "Описание", "--wf-tcp=80") });
        var deps = BuildDependencies(catalog: catalog);
        var (vm, _, _, _, _, _) = CreateVm(deps, new MaintenanceDependencies(
            new FakeGitHubReleaseClient(), new FakeEngineUpdater(), new FakeAppUpdater(), tester, "0.1.0", @"C:\P\presets.json"));
        vm.InitializeAsync().GetAwaiter().GetResult();

        var testTask = vm.TestAllPresetsAsync();
        await Task.Yield();
        Assert.True(vm.IsTestingPresets);
        vm.CancelPresetTestCommand.Execute(null);
        await testTask;

        Assert.True(tester.LastCt.IsCancellationRequested);
        Assert.Contains("отменена", vm.PresetTestStatus);
        Assert.False(vm.IsTestingPresets);
    }

    [Fact]
    public async Task UpdateAppAsync_NewerRelease_LaunchesUpdaterBeforeRestart()
    {
        var gitHub = new FakeGitHubReleaseClient
        {
            Releases = { ["Benny4ip/ZPRT_NEXT_GEN"] = new ReleaseInfo("0.2.0", "0.2.0", null, []) }
        };
        var updater = new FakeAppUpdater();
        var deps = BuildDependencies();
        var (vm, _, _, _, _, _) = CreateVm(deps, new MaintenanceDependencies(
            gitHub, new FakeEngineUpdater(), updater, new FakePresetTester(), "0.1.0", @"C:\P\presets.json"));
        vm.InitializeAsync().GetAwaiter().GetResult();

        bool restarted = false;
        vm.RestartRequested += (_, _) => restarted = true;

        await vm.UpdateAppAsync();

        Assert.True(restarted, "приложение должно запросить перезапуск");
        Assert.True(updater.Launched, "скрипт обновления должен быть запущен");
        Assert.Same(updater.Plan, updater.LaunchedPlan);
    }

    [Fact]
    public void OpenLogsCommand_MissingDirectory_SetsErrorStatus()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var (vm, store, _, _, _, _) = CreateVm(BuildDependencies(
            settings: new AppSettings { EnginePath = @"C:\E" },
            catalog: Catalog(Preset("general", "g1", "--wf-tcp=80,12"))));
        store.Path = Path.Combine(missingDirectory, "settings.json"); // папки нет → logs тоже нет

        vm.OpenLogsCommand.Execute(null);

        Assert.Contains("Ошибка", vm.StatusText);
    }
}
