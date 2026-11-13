using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Tests.Fakes;

namespace ZprtNextGen.Core.Tests.Presets;

public class PresetTesterTests
{
    private static readonly Preset[] Presets =
    {
        new("p1", "g", "Пресет 1", "Один", "--wf-tcp=80"),
        new("p2", "g", "Пресет 2", "Два", "--wf-tcp=443"),
    };

    [Fact]
    public async Task TestAllAsync_RunsEachPreset_StopsEngineBetween()
    {
        var engine = new FakeEngineProcess();
        var connectivity = new FakeConnectivity();
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(Presets, @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal("Успех", results[0].StatusText);
        Assert.Equal(2, engine.StopCalls);
        Assert.Equal(2, connectivity.CheckCalls);
        Assert.Equal(EngineStatus.Stopped, engine.Status);
        Assert.Equal(2, engine.Starts.Count);
        Assert.Equal(Path.Combine(@"C:\E", "bin", "winws.exe"), engine.Starts[0].ExecutablePath);
        Assert.Equal(Path.Combine(@"C:\E", "bin"), engine.Starts[0].WorkingDirectory);
    }

    [Fact]
    public async Task TestAllAsync_StartThrows_ContinuesWithNextAndStops()
    {
        var engine = new FakeEngineProcess { ThrowStartsRemaining = 1 };
        var connectivity = new FakeConnectivity();
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(Presets, @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.Contains("start failed", results[0].Detail);
        Assert.True(results[1].Success);
        Assert.Equal(1, engine.StopCalls); // первый пресет не запустился — нечего останавливать
        Assert.Equal(EngineStatus.Stopped, engine.Status);
    }

    [Fact]
    public async Task TestAllAsync_ConnectivityFails_ResultNotSuccessful()
    {
        var engine = new FakeEngineProcess();
        var connectivity = new FakeConnectivity { Report = new ConnectivityReport(Array.Empty<ConnectivityResult>()) };
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(Presets, @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.All(results, r => Assert.False(r.Success));
        Assert.Equal(2, engine.StopCalls);
    }

    [Fact]
    public async Task TestAllAsync_PreCancelled_ThrowsAndEngineStopped()
    {
        var engine = new FakeEngineProcess();
        var connectivity = new FakeConnectivity();
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.TestAllAsync(Presets, @"C:\E", GameFilterMode.Disabled, null, cts.Token));

        Assert.Equal(EngineStatus.Stopped, engine.Status);
        Assert.Empty(engine.Starts);
    }

    [Fact]
    public async Task TestAllAsync_ForeignInstanceRunning_FailsWithoutStartingEngine()
    {
        var engine = new FakeEngineProcess { ForeignRunning = true };
        var connectivity = new FakeConnectivity();
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(Presets, @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Contains("извне", r.Detail);
        });
        Assert.Empty(engine.Starts);
        Assert.Equal(0, engine.StopCalls);
    }

    [Fact]
    public async Task TestAllAsync_UsesWorstLatencyOfBothServices()
    {
        var engine = new FakeEngineProcess();
        var connectivity = new FakeConnectivity
        {
            Report = new ConnectivityReport(
            [
                new ConnectivityResult(
                    new ConnectivityTarget("discord", "Discord", "https://discord.com", true, ServiceKind.Discord),
                    true, 37, null),
                new ConnectivityResult(
                    new ConnectivityTarget("youtube", "YouTube", "https://www.youtube.com", true, ServiceKind.YouTube),
                    true, 84, null),
            ])
        };
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(
            [Presets[0]], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.True(result.Qualified);
        Assert.Equal(84, result.ElapsedMs); // score = max(YouTube, Discord)
        Assert.Equal(84, result.ScoreMs);
        Assert.Equal(84, result.YoutubeMs);
        Assert.Equal(37, result.DiscordMs);
        Assert.Equal("84 мс", result.LatencyText);
    }

    [Fact]
    public async Task TestAllAsync_OnlyOneServiceAvailable_IsNotQualified()
    {
        var engine = new FakeEngineProcess();
        var connectivity = new FakeConnectivity
        {
            Report = new ConnectivityReport(
            [
                new ConnectivityResult(
                    new ConnectivityTarget("youtube", "YouTube", "https://www.youtube.com", true, ServiceKind.YouTube),
                    true, 84, null),
                new ConnectivityResult(
                    new ConnectivityTarget("discord", "Discord", "https://discord.com", true, ServiceKind.Discord),
                    false, 5000, "timeout", TimedOut: true),
            ])
        };
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(
            [Presets[0]], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.False(result.Qualified);
        Assert.True(result.YoutubeOk);
        Assert.False(result.DiscordOk);
        Assert.Null(result.ScoreMs);
        Assert.Contains("Связь не подтверждена", result.Detail);
    }

    [Fact]
    public async Task TestAllAsync_Timeout_ReportsTimeoutInsteadOfLatency()
    {
        var engine = new FakeEngineProcess();
        var connectivity = new FakeConnectivity
        {
            Report = new ConnectivityReport(
            [
                new ConnectivityResult(
                    new ConnectivityTarget("discord", "Discord", "https://discord.com", true),
                    false, 5000, "Превышено время ожидания", TimedOut: true),
            ])
        };
        var sut = new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10));

        var results = await sut.TestAllAsync(
            [Presets[0]], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Null(result.ElapsedMs);
        Assert.Equal("таймаут", result.LatencyText);
        Assert.Equal("Ошибка: Связь не подтверждена", result.StatusText);
    }

    [Fact]
    public void StatusText_SuccessAndFailure()
    {
        var ok = new PresetTestResult("p1", "Пресет 1", true, 120, null);
        Assert.Equal("Успех", ok.StatusText);

        var fail = new PresetTestResult("p2", "Пресет 2", false, null, "таймаут");
        Assert.Equal("Ошибка: таймаут", fail.StatusText);
        Assert.Equal("—", fail.LatencyText);
    }
}
