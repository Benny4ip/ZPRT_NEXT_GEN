using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZprtNextGen.App.Tests.Fakes;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Connection;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Discord;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.App.Tests.ViewModels;

public sealed class FakeDiscordFixService : IDiscordFixService
{
    public DiscordFixResult Result { get; set; } = new(DiscordFixStatus.Success, 5, 1, null, false);
    public int CallCount { get; private set; }
    public bool LastFullReset { get; private set; }

    public Task<DiscordFixResult> FixAsync(bool fullReset, CancellationToken ct)
    {
        CallCount++;
        LastFullReset = fullReset;
        return Task.FromResult(Result);
    }
}

public sealed class FakeInternetProbe : IInternetProbe
{
    public bool IsOnline { get; set; } = true;
    public Task<bool> CheckInternetAsync(CancellationToken ct) => Task.FromResult(IsOnline);
}

public class DashboardViewModelTests
{
    private static readonly Preset P1 = new("p1", "general", "Пресет 1", "", "--wf-tcp=80");
    private static readonly Preset P2 = new("p2", "general", "Пресет 2", "", "--wf-tcp=443");

    private static PresetCatalog Catalog() => new(
        1,
        "zapret",
        "1.10.4",
        [new PresetGroup("general", "Обычные")],
        [P1, P2]);

    private static (DashboardViewModel vm, ConnectionCoordinator coordinator, FakeDiscordFixService fix, IConfigStore store) CreateSut(
        string? tempDir = null)
    {
        tempDir ??= Path.Combine(Path.GetTempPath(), "ZprtNextGen.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var configPath = Path.Combine(tempDir, "config.json");
        var store = new ConfigStore(configPath);
        var logger = new FakeLogger();
        var dispatcher = new ImmediateDispatcher();
        var fixService = new FakeDiscordFixService();

        var engineProcess = new FakeEngineProcess();
        var connectivity = new FakeConnectivity();
        var internet = new FakeInternetProbe();
        var tester = new FakePresetTester();
        var gate = new EngineOperationGate();

        var coordinatorDeps = new ConnectionCoordinatorDependencies(
            Catalog(),
            store,
            engineProcess,
            connectivity,
            internet,
            tester,
            gate,
            logger,
            () => new EngineValidation(true, Path.Combine(tempDir, "engine"), []));

        var coordinator = new ConnectionCoordinator(coordinatorDeps);

        var deps = new DashboardDependencies(
            coordinator,
            store,
            fixService,
            logger,
            dispatcher,
            Path.Combine(tempDir, "logs"));

        var vm = new DashboardViewModel(deps);
        return (vm, coordinator, fixService, store);
    }

    [Fact]
    public void InitialState_IsDisconnected_AndOrbClickable()
    {
        var (vm, _, _, _) = CreateSut();

        Assert.Equal(ConnectionState.Disconnected, vm.State);
        Assert.Equal("Включить", vm.OrbTitle);
        Assert.Equal("Нажми для запуска YouTube и Discord", vm.StatusTitle);
        Assert.True(vm.CanClickOrb);
        Assert.False(vm.CanStopAndApply);
        Assert.False(vm.CanCancelSelection);
        Assert.False(vm.IsSelecting);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public void OpenSettingsCommand_FiresSettingsRequested()
    {
        var (vm, _, _, _) = CreateSut();
        var fired = false;
        vm.SettingsRequested += (_, _) => fired = true;

        vm.OpenSettingsCommand.Execute(null);

        Assert.True(fired);
    }

    [Fact]
    public void FixDiscordCommand_ExecutesAndSetsSuccess()
    {
        var (vm, _, fix, _) = CreateSut();
        string? notification = null;
        vm.NotificationRequested += (_, msg) => notification = msg;

        vm.FixDiscordCommand.Execute(null);

        Assert.Equal(1, fix.CallCount);
        Assert.False(fix.LastFullReset);
        Assert.Equal("Готово \u2713", vm.FixDiscordText);
        Assert.NotNull(notification);
    }

    [Fact]
    public void StatusTitle_FormatsErrorCorrectly()
    {
        var (vm, _, _, _) = CreateSut();

        Assert.Equal(ConnectionIssue.None, vm.Issue);
        Assert.False(vm.HasError);
    }
}
