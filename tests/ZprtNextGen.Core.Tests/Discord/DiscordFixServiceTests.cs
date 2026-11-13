using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Discord;

namespace ZprtNextGen.Core.Tests.Discord;

public class DiscordFixServiceTests
{
    private static (string Root, string Roaming, string Local) NewRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"));
        var roaming = Path.Combine(root, "Roaming");
        var local = Path.Combine(root, "Local");
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(local);
        return (root, roaming, local);
    }

    private static void CreateDiscordInstall(string local, bool withUpdate = true, string appVersion = "app-1.0.0")
    {
        var discord = Path.Combine(local, "Discord");
        Directory.CreateDirectory(Path.Combine(discord, appVersion));
        File.WriteAllText(Path.Combine(discord, appVersion, "Discord.exe"), "stub");
        if (withUpdate)
        {
            File.WriteAllText(Path.Combine(discord, "Update.exe"), "stub");
        }
    }

    private static void CreateRoamingData(string roaming, params string[] entries)
    {
        var discord = Path.Combine(roaming, "Discord");
        foreach (var entry in entries)
        {
            var path = Path.Combine(discord, entry);
            if (Path.HasExtension(path))
            {
                Directory.CreateDirectory(discord);
                File.WriteAllText(path, "stub");
            }
            else
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "file.bin"), "stub");
            }
        }
    }

    private sealed class FakeProcessController : IDiscordProcessController
    {
        public List<string> Started { get; } = new();
        public int StopCalls;
        public bool StartSucceeds { get; set; } = true;
        public int StoppedCount { get; set; } = 1;

        public Task<int> StopAsync(IReadOnlyList<string> installRoots, CancellationToken ct)
        {
            StopCalls++;
            return Task.FromResult(StoppedCount);
        }

        public Task<bool> StartAsync(string executable, string arguments, CancellationToken ct)
        {
            Started.Add(executable + " " + arguments);
            return Task.FromResult(StartSucceeds);
        }
    }

    private static DiscordFixService CreateService(string roaming, string local, FakeProcessController controller)
        => new(new DiscordFixPaths(roaming, local), controller);

    [Fact]
    public async Task FixAsync_NotInstalled_ReturnsNotInstalledAndDoesNotTouchAnything()
    {
        var (_, roaming, local) = NewRoots();
        CreateRoamingData(roaming, "Cache");
        var controller = new FakeProcessController();
        var sut = CreateService(roaming, local, controller);

        var result = await sut.FixAsync(fullReset: false, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.NotInstalled, result.Status);
        Assert.Equal(0, controller.StopCalls);
        Assert.True(Directory.Exists(Path.Combine(roaming, "Discord", "Cache")));
    }

    [Fact]
    public async Task FixAsync_SafeMode_DeletesCacheButKeepsSessionData()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local);
        CreateRoamingData(roaming,
            "Cache", "Code Cache", "GPUCache", "DawnCache", "ShaderCache", "Service Worker",
            "Cookies", "Local Storage", "Session Storage", "IndexedDB");

        var controller = new FakeProcessController();
        var sut = CreateService(roaming, local, controller);

        var result = await sut.FixAsync(fullReset: false, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.Success, result.Status);
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Cache")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Code Cache")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Service Worker")));
        Assert.True(Directory.Exists(Path.Combine(roaming, "Discord", "Cookies")));
        Assert.True(Directory.Exists(Path.Combine(roaming, "Discord", "Local Storage")));
        Assert.True(Directory.Exists(Path.Combine(roaming, "Discord", "Session Storage")));
        Assert.True(Directory.Exists(Path.Combine(roaming, "Discord", "IndexedDB")));
        Assert.Equal(1, controller.StopCalls);
        Assert.Equal(6, result.RemovedEntries);
    }

    [Fact]
    public async Task FixAsync_FullReset_DeletesAccountBoundData()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local);
        CreateRoamingData(roaming, "Cache", "Cookies", "Local Storage", "Session Storage", "IndexedDB", "Network");

        var sut = CreateService(roaming, local, new FakeProcessController());

        var result = await sut.FixAsync(fullReset: true, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.Success, result.Status);
        Assert.True(result.FullReset);
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Cache")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Cookies")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Local Storage")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Session Storage")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "IndexedDB")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "Discord", "Network")));
    }

    [Fact]
    public async Task FixAsync_RestartsViaUpdateExe()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local);
        CreateRoamingData(roaming, "Cache");
        var controller = new FakeProcessController();
        var sut = CreateService(roaming, local, controller);

        await sut.FixAsync(fullReset: false, CancellationToken.None);

        var start = Assert.Single(controller.Started);
        Assert.Contains(Path.Combine(local, "Discord", "Update.exe"), start, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--processStart", start, StringComparison.Ordinal);
        Assert.Contains("Discord.exe", start, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixAsync_FallsBackToAppExe_WhenUpdateMissing()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local, withUpdate: false);
        CreateRoamingData(roaming, "Cache");
        var controller = new FakeProcessController();
        var sut = CreateService(roaming, local, controller);

        var result = await sut.FixAsync(fullReset: false, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.Success, result.Status);
        var start = Assert.Single(controller.Started);
        Assert.Contains("Discord.exe", start, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app-1.0.0", start, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixAsync_StartFailure_ReturnsFailed()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local);
        CreateRoamingData(roaming, "Cache");
        var controller = new FakeProcessController { StartSucceeds = false };
        var sut = CreateService(roaming, local, controller);

        var result = await sut.FixAsync(fullReset: false, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task FixAsync_MissingRoamingDirectories_StillSucceeds()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local);
        var controller = new FakeProcessController();
        var sut = CreateService(roaming, local, controller);

        var result = await sut.FixAsync(fullReset: false, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.Success, result.Status);
        Assert.Equal(0, result.RemovedEntries);
        Assert.Single(controller.Started);
    }

    [Fact]
    public async Task FixAsync_DeletesOnlyKnownDiscordClients()
    {
        var (_, roaming, local) = NewRoots();
        CreateDiscordInstall(local);
        Directory.CreateDirectory(Path.Combine(local, "DiscordPTB"));
        File.WriteAllText(Path.Combine(local, "DiscordPTB", "Update.exe"), "stub");
        CreateRoamingData(roaming, "Cache");

        var controller = new FakeProcessController();
        var sut = CreateService(roaming, local, controller);

        var result = await sut.FixAsync(fullReset: false, CancellationToken.None);

        Assert.Equal(DiscordFixStatus.Success, result.Status);
        Assert.Equal(2, controller.Started.Count);
    }
}
