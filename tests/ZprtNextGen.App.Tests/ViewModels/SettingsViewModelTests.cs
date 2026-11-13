#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZprtNextGen.App.Tests.Fakes;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Connection;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;
using ZprtNextGen.Core.Startup;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static readonly Preset P1 = new("p1", "general", "Пресет 1", "", "--wf-tcp=80");

    private static PresetCatalog Catalog() => new(
        1,
        "zapret",
        "1.10.4",
        [new PresetGroup("general", "Обычные")],
        [P1]);

    private static SettingsViewModel CreateSut(
        bool isPortable,
        FakeGitHubReleaseClient? gitHub = null,
        string? tempDir = null)
    {
        tempDir ??= Path.Combine(Path.GetTempPath(), "ZprtNextGen.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var store = new ConfigStore(Path.Combine(tempDir, "config.json"));
        var logger = new FakeLogger();
        var dispatcher = new ImmediateDispatcher();
        var engineProcess = new FakeEngineProcess();
        var connectivity = new FakeConnectivity();
        var engineRoot = Path.Combine(tempDir, "engine");

        var coordinator = new ConnectionCoordinator(new ConnectionCoordinatorDependencies(
            Catalog(),
            store,
            engineProcess,
            connectivity,
            new FakeInternetProbe(),
            new FakePresetTester(),
            new EngineOperationGate(),
            logger,
            () => new EngineValidation(true, engineRoot, [])));

        var dependencies = new SettingsDependencies(
            store,
            coordinator,
            Catalog(),
            engineProcess,
            connectivity,
            new FakeAutostart(),
            new FakeDiscordFixService(),
            new FakePresetTester(),
            gitHub ?? new FakeGitHubReleaseClient(),
            new FakeEngineUpdater(),
            new FakeAppUpdater(),
            logger,
            dispatcher,
            "1.0.2-beta-FIX",
            Path.Combine(tempDir, "presets", "presets.json"),
            isPortable,
            _ => new EngineValidation(true, engineRoot, []),
            _ => { },
            _ => true,
            Path.Combine(tempDir, "logs"));

        return new SettingsViewModel(dependencies);
    }

    [Fact]
    public void PortableBuild_HasNoEngineSettings()
    {
        var vm = CreateSut(isPortable: true);

        Assert.True(vm.IsPortable);
        Assert.False(vm.IsStandard);
        Assert.Equal("Обновления", vm.EngineSectionTitle);
    }

    [Fact]
    public void StandardBuild_ShowsEngineSettings()
    {
        var vm = CreateSut(isPortable: false);

        Assert.False(vm.IsPortable);
        Assert.True(vm.IsStandard);
        Assert.Equal("Движок и обновления", vm.EngineSectionTitle);
    }

    [Fact]
    public async Task PortableBuild_CheckUpdates_DoesNotQueryEngineRepository()
    {
        var gitHub = new FakeGitHubReleaseClient();
        var vm = CreateSut(isPortable: true, gitHub);

        vm.CheckUpdatesCommand.Execute(null);
        await WaitForAsync((AsyncRelayCommand)vm.CheckUpdatesCommand);

        Assert.DoesNotContain("Flowseal/zapret-discord-youtube", gitHub.RequestedRepos);
        Assert.Contains("Benny4ip/ZPRT_NEXT_GEN", gitHub.RequestedRepos);
        Assert.Contains("встроен", vm.UpdateStatusText);
    }

    [Fact]
    public async Task StandardBuild_CheckUpdates_QueriesEngineRepository()
    {
        var gitHub = new FakeGitHubReleaseClient();
        var vm = CreateSut(isPortable: false, gitHub);

        vm.CheckUpdatesCommand.Execute(null);
        await WaitForAsync((AsyncRelayCommand)vm.CheckUpdatesCommand);

        Assert.Contains("Flowseal/zapret-discord-youtube", gitHub.RequestedRepos);
        Assert.Contains("Benny4ip/ZPRT_NEXT_GEN", gitHub.RequestedRepos);
    }

    private static async Task WaitForAsync(AsyncRelayCommand command)
    {
        for (var attempt = 0; attempt < 400 && command.IsRunning; attempt++)
            await Task.Delay(5);
    }
}
