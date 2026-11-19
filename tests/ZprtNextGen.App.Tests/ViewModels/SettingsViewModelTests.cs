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
        string? tempDir = null,
        FakeAutostart? autostart = null)
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
            autostart ?? new FakeAutostart(),
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

    [Fact]
    public void DetailedGameFilter_WhenProfileDisabled_ReportsDisabled()
    {
        var vm = CreateSut(isPortable: false);
        vm.GameFilter = GameFilterMode.All;
        vm.IsGameProfileEnabled = false;

        Assert.Equal(GameFilterMode.Disabled, vm.DetailedGameFilter);
    }

    [Fact]
    public void DetailedGameFilter_SetDisabled_DisablesActualFilter()
    {
        var vm = CreateSut(isPortable: false);
        vm.GameFilter = GameFilterMode.Tcp;

        vm.DetailedGameFilter = GameFilterMode.Disabled;

        Assert.Equal(GameFilterMode.Disabled, vm.GameFilter);
        Assert.False(vm.IsGameProfileEnabled);
        Assert.Equal(GameFilterMode.Disabled, vm.DetailedGameFilter);
    }

    [Fact]
    public void DetailedGameFilter_SetDisabled_PersistsDisabled()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), "ZprtNextGen.App.Tests", Guid.NewGuid().ToString("N"));
        var vm = CreateSut(isPortable: false, tempDir: tempDir);
        vm.GameFilter = GameFilterMode.Tcp;

        vm.DetailedGameFilter = GameFilterMode.Disabled;

        var saved = new ConfigStore(Path.Combine(tempDir, "config.json")).Load();
        Assert.Equal(GameFilterMode.Disabled, saved.GameFilter);
    }

    [Fact]
    public void DetailedGameFilter_SetTcp_KeepsProfileOff()
    {
        var vm = CreateSut(isPortable: false);
        vm.GameFilter = GameFilterMode.All;

        vm.DetailedGameFilter = GameFilterMode.Tcp;

        Assert.Equal(GameFilterMode.Tcp, vm.GameFilter);
        Assert.False(vm.IsGameProfileEnabled);
    }

    [Fact]
    public void IsManualMode_ReflectsAutoMode()
    {
        var vm = CreateSut(isPortable: false);

        vm.IsAutoMode = true;
        Assert.False(vm.IsManualMode);

        vm.IsManualMode = true;
        Assert.False(vm.IsAutoMode);
        Assert.True(vm.IsManualMode);
    }

    [Fact]
    public void Constructor_ReadsAutostartOnce()
    {
        var autostart = new FakeAutostart { Enabled = true };
        var vm = CreateSut(isPortable: false, autostart: autostart);

        Assert.Equal(1, autostart.IsEnabledCalls);
        Assert.True(vm.IsAutostartEnabled);
    }

    [Fact]
    public void Reload_DoesNotReadAutostartAgain()
    {
        var autostart = new FakeAutostart();
        var vm = CreateSut(isPortable: false, autostart: autostart);

        vm.Reload();

        Assert.Equal(1, autostart.IsEnabledCalls);
    }

    [Fact]
    public void SelectedSection_DefaultsToMain()
    {
        var vm = CreateSut(isPortable: false);

        Assert.Equal(SettingsSection.Main, vm.SelectedSection);
    }

    [Fact]
    public void SelectedSection_CanBeChanged()
    {
        var vm = CreateSut(isPortable: false);
        var notified = false;
        vm.PropertyChanged += (_, args) =>
            notified |= args.PropertyName == nameof(SettingsViewModel.SelectedSection);

        vm.SelectedSection = SettingsSection.Diagnostics;

        Assert.Equal(SettingsSection.Diagnostics, vm.SelectedSection);
        Assert.True(notified);
    }

    [Fact]
    public void OpenAboutCommand_RaisesAboutRequested()
    {
        var vm = CreateSut(isPortable: false);
        object? sender = null;
        var raised = 0;
        vm.AboutRequested += (s, _) =>
        {
            sender = s;
            raised++;
        };

        vm.OpenAboutCommand.Execute(null);

        Assert.Equal(1, raised);
        Assert.Same(vm, sender);
    }

    private static async Task WaitForAsync(AsyncRelayCommand command)
    {
        for (var attempt = 0; attempt < 400 && command.IsRunning; attempt++)
            await Task.Delay(5);
    }
}
