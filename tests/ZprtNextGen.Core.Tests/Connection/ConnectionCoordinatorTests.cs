using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Connection;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;
using ZprtNextGen.Core.Tests.Fakes;

namespace ZprtNextGen.Core.Tests.Connection;

public class ConnectionCoordinatorTests
{
    private static readonly Preset P1 = new("p1", "general", "Первый", "", "--wf-tcp=80");
    private static readonly Preset P2 = new("p2", "general", "Второй", "", "--wf-tcp=443");
    private static readonly Preset P3 = new("p3", "general", "Третий", "", "--wf-tcp=8080");

    private static PresetCatalog Catalog() => new(
        1,
        "zapret",
        "1.10.4",
        [new PresetGroup("general", "Обычные")],
        [P1, P2, P3]);

    private static ConnectivityReport Ok(long youtube = 100, long discord = 110) => new(
    [
        new ConnectivityResult(
            new ConnectivityTarget("youtube", "YouTube", "https://www.youtube.com", true, ServiceKind.YouTube),
            true, youtube, null),
        new ConnectivityResult(
            new ConnectivityTarget("discord", "Discord", "https://discord.com", true, ServiceKind.Discord),
            true, discord, null),
    ]);

    private static ConnectivityReport Fail() => new(
    [
        new ConnectivityResult(
            new ConnectivityTarget("youtube", "YouTube", "https://www.youtube.com", true, ServiceKind.YouTube),
            false, 4000, "таймаут", TimedOut: true),
        new ConnectivityResult(
            new ConnectivityTarget("discord", "Discord", "https://discord.com", true, ServiceKind.Discord),
            false, 4000, "таймаут", TimedOut: true),
    ]);

    private static PresetTestResult Qualified(string id, long youtube, long discord) =>
        new(id, "Обход " + id, true, Math.Max(youtube, discord), null)
        {
            YoutubeOk = true,
            DiscordOk = true,
            YoutubeMs = youtube,
            DiscordMs = discord,
        };

    private static PresetTestResult Failed(string id) =>
        new(id, "Обход " + id, false, null, "нет связи");

    private sealed class FakeConfigStore : IConfigStore
    {
        public string Path => "config.json";
        public AppConfig Config { get; set; } = new();
        public int SaveCalls;

        public AppConfig Load(IReadOnlyCollection<string>? knownPresetIds = null) => Config;

        public void Save(AppConfig config)
        {
            Config = config;
            SaveCalls++;
        }
    }

    private sealed class QueueConnectivity : IConnectivityService
    {
        private readonly ConnectivityReport[] _reports;
        private int _index;

        public QueueConnectivity(params ConnectivityReport[] reports)
        {
            _reports = reports.Length == 0 ? [Ok()] : reports;
        }

        public int Calls { get; private set; }

        public Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null)
        {
            Calls++;
            var index = Math.Min(_index++, _reports.Length - 1);
            return Task.FromResult(_reports[index]);
        }
    }

    private sealed class BlockingConnectivity : IConnectivityService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Ok();
        }
    }

    private sealed class FakeInternetProbe(bool available = true) : IInternetProbe
    {
        public bool Available { get; set; } = available;
        public int Calls;

        public Task<bool> CheckInternetAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Available);
        }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Errors { get; } = new();

        public void Info(string message) { }

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }

    private sealed class ScriptedTester(Func<Preset, int, PresetTestResult> factory) : IPresetTester
    {
        public int Calls { get; private set; }
        public List<string> Tested { get; } = new();

        public Task<IReadOnlyList<PresetTestResult>> TestAllAsync(
            IReadOnlyList<Preset> presets, string engineRoot, GameFilterMode filter,
            IProgress<PresetTestProgress>? progress, CancellationToken ct)
        {
            Calls++;
            var results = new List<PresetTestResult>(presets.Count);

            for (var i = 0; i < presets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var preset = presets[i];
                var result = factory(preset, i);
                results.Add(result);
                Tested.Add(preset.Id);
                progress?.Report(new PresetTestProgress(i + 1, presets.Count, preset, result));
            }

            return Task.FromResult<IReadOnlyList<PresetTestResult>>(results);
        }
    }

    /// <summary>Один результат и бесконечное ожидание отмены — для досрочного применения.</summary>
    private sealed class BlockingTester(PresetTestResult first) : IPresetTester
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<PresetTestResult>> TestAllAsync(
            IReadOnlyList<Preset> presets, string engineRoot, GameFilterMode filter,
            IProgress<PresetTestProgress>? progress, CancellationToken ct)
        {
            Calls++;
            progress?.Report(new PresetTestProgress(1, presets.Count, presets[0], first));
            Reported.TrySetResult();

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return [first];
        }
    }

    private sealed class Harness
    {
        public FakeConfigStore Config { get; } = new();
        public FakeEngineProcess Engine { get; } = new();
        public IConnectivityService Connectivity { get; set; } = new QueueConnectivity(Ok());
        public FakeInternetProbe Internet { get; } = new();
        public IPresetTester Tester { get; set; } = new ScriptedTester((_, _) => Failed("p1"));
        public EngineOperationGate Gate { get; } = new();
        public FakeLogger Logger { get; } = new();
        public EngineValidation EngineValidation { get; set; } = new(true, @"C:\Engine", []);
        public Func<CancellationToken, Task<bool>>? ConfirmStopForeign { get; set; }
        public AutoSelectPolicy Policy { get; } = new()
        {
            StartupDelay = TimeSpan.Zero,
            VerifyDelay = TimeSpan.Zero,
            HealthCooldown = TimeSpan.FromMinutes(5),
        };

        public ConnectionCoordinator Build() => new(new ConnectionCoordinatorDependencies(
            Catalog(),
            Config,
            Engine,
            Connectivity,
            Internet,
            Tester,
            Gate,
            Logger,
            () => EngineValidation,
            ConfirmStopForeign,
            Policy));
    }

    private static async Task<ConnectionCoordinator> InitializedAsync(Harness harness, CancellationToken ct = default)
    {
        var sut = harness.Build();
        await sut.InitializeAsync(ct);
        return sut;
    }

    [Fact]
    public async Task Initialize_NoProfile_StaysDisconnectedInAutoMode()
    {
        var harness = new Harness();
        var sut = await InitializedAsync(harness);

        Assert.Equal(ConnectionState.Disconnected, sut.Snapshot.State);
        Assert.True(sut.Snapshot.IsAutoMode);
        Assert.Equal(ConnectionIssue.None, sut.Snapshot.Issue);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
    }

    [Fact]
    public async Task Initialize_LegacyManualConfig_KeepsManualMode()
    {
        var harness = new Harness();
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = "p2",
            },
        };

        var sut = await InitializedAsync(harness);

        Assert.False(sut.Snapshot.IsAutoMode);
        Assert.Equal(ConnectionState.Disconnected, sut.Snapshot.State);
    }

    [Fact]
    public async Task Connect_AutoWithoutProfile_SelectsAllAndConnectsBest()
    {
        var harness = new Harness
        {
            Connectivity = new QueueConnectivity(Ok(200, 150)),
            Tester = new ScriptedTester((preset, _) => preset.Id switch
            {
                "p1" => Qualified("p1", 100, 300),
                "p2" => Qualified("p2", 200, 150),
                _ => Failed(preset.Id),
            }),
        };
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Equal("p2", sut.Snapshot.PresetId);
        Assert.Equal(200, sut.Snapshot.LatencyMs);
        Assert.Equal(3, harness.Tester is ScriptedTester s ? s.Tested.Count : 0);
        Assert.Equal(1, harness.Config.SaveCalls);
        Assert.Equal("p2", harness.Config.Config.Connection.AutoProfile!.PresetId);
        Assert.Equal(200, harness.Config.Config.Connection.AutoProfile.LatencyMs);
        Assert.True(harness.Engine.Starts.Count >= 1);
    }

    [Fact]
    public async Task Connect_AutoWithSavedProfile_TriesSavedBeforeFullScan()
    {
        var harness = new Harness();
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Auto,
                AutoProfile = new AutoProfile(
                    "p3", "Третий", 120, 110, 120,
                    DateTimeOffset.UtcNow, "1.10.4", "sha256:x"),
            },
        };
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Equal("p3", sut.Snapshot.PresetId);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
        Assert.Single(harness.Engine.Starts);
    }

    [Fact]
    public async Task Connect_SavedProfileFails_FallsBackToSelection()
    {
        var harness = new Harness();
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Auto,
                AutoProfile = new AutoProfile(
                    "p1", "Первый", 120, 110, 120,
                    DateTimeOffset.UtcNow, "1.10.4", "sha256:x"),
            },
        };
        // Первые две проверки — сохранённый профиль (оба раза неудача), затем кандидат работает.
        harness.Connectivity = new QueueConnectivity(Fail(), Fail(), Ok());
        harness.Tester = new ScriptedTester((_, _) => Qualified("p2", 90, 95));

        var sut = await InitializedAsync(harness);
        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Equal("p2", sut.Snapshot.PresetId);
        Assert.True(harness.Tester is ScriptedTester s && s.Calls == 1);
        Assert.Equal("p2", harness.Config.Config.Connection.AutoProfile!.PresetId);
    }

    [Fact]
    public async Task Connect_NoInternet_ErrorAndNoScan()
    {
        var harness = new Harness();
        harness.Internet.Available = false;
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, sut.Snapshot.State);
        Assert.Equal(ConnectionIssue.NoInternet, sut.Snapshot.Issue);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
        Assert.Empty(harness.Engine.Starts);
    }

    [Fact]
    public async Task Connect_EngineMissing_ErrorEngineMissing()
    {
        var harness = new Harness
        {
            EngineValidation = new EngineValidation(false, null, [@"bin\winws.exe"]),
        };
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, sut.Snapshot.State);
        Assert.Equal(ConnectionIssue.EngineMissing, sut.Snapshot.Issue);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
    }

    [Fact]
    public async Task Connect_ForeignInstanceDeclined_ErrorForeignInstance()
    {
        var harness = new Harness { ConfirmStopForeign = null };
        harness.Engine.ForeignRunning = true;
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, sut.Snapshot.State);
        Assert.Equal(ConnectionIssue.ForeignInstance, sut.Snapshot.Issue);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
    }

    [Fact]
    public async Task Connect_ForeignInstanceConfirmed_KillsAndContinues()
    {
        var harness = new Harness
        {
            ConfirmStopForeign = _ => Task.FromResult(true),
            Tester = new ScriptedTester((_, _) => Qualified("p1", 100, 100)),
        };
        harness.Engine.ForeignRunning = true;
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
    }

    [Fact]
    public async Task Connect_AllPresetsFail_ErrorAllPresetsFailed()
    {
        var harness = new Harness
        {
            Tester = new ScriptedTester((preset, _) => Failed(preset.Id)),
        };
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, sut.Snapshot.State);
        Assert.Equal(ConnectionIssue.AllPresetsFailed, sut.Snapshot.Issue);
        Assert.Null(sut.Snapshot.PresetId);
        Assert.Null(harness.Config.Config.Connection.AutoProfile);
    }

    [Fact]
    public async Task StopAndApplyBest_CancelsScan_AndConnectsFirstQualified()
    {
        var blocking = new BlockingTester(Qualified("p1", 100, 110));
        var harness = new Harness { Tester = blocking };
        var sut = await InitializedAsync(harness);

        var connect = sut.ConnectAsync(CancellationToken.None);
        await blocking.Reported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ConnectionState.Selecting, sut.Snapshot.State);
        Assert.True(sut.Snapshot.HasQualifiedCandidate);

        await sut.StopAndApplyBestAsync(CancellationToken.None);
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Equal("p1", sut.Snapshot.PresetId);
    }

    [Fact]
    public async Task CancelSelection_ReturnsDisconnected_AndDoesNotConnect()
    {
        var blocking = new BlockingTester(Qualified("p1", 100, 110));
        var harness = new Harness { Tester = blocking };
        var sut = await InitializedAsync(harness);

        var connect = sut.ConnectAsync(CancellationToken.None);
        await blocking.Reported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sut.CancelSelectionAsync(CancellationToken.None);
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ConnectionState.Disconnected, sut.Snapshot.State);
        Assert.Empty(harness.Engine.Starts);
    }

    [Fact]
    public async Task Disconnect_StopsEngineAndReturnsDisconnected()
    {
        var harness = new Harness
        {
            Tester = new ScriptedTester((_, _) => Qualified("p1", 100, 100)),
        };
        var sut = await InitializedAsync(harness);
        await sut.ConnectAsync(CancellationToken.None);

        await sut.DisconnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Disconnected, sut.Snapshot.State);
        Assert.True(harness.Engine.StopCalls >= 1);
    }

    [Fact]
    public async Task Toggle_ConnectsThenDisconnects()
    {
        var harness = new Harness
        {
            Tester = new ScriptedTester((_, _) => Qualified("p1", 100, 100)),
        };
        var sut = await InitializedAsync(harness);

        await sut.ToggleAsync(CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);

        await sut.ToggleAsync(CancellationToken.None);
        Assert.Equal(ConnectionState.Disconnected, sut.Snapshot.State);
    }

    [Fact]
    public async Task Connect_ManualMode_UsesManualPresetAndSkipsScan()
    {
        var harness = new Harness();
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = "p2",
            },
        };
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Equal("p2", sut.Snapshot.PresetId);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
        Assert.Single(harness.Engine.Starts);
        Assert.Contains("--wf-tcp=443", harness.Engine.Starts[0].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_ManualPresetFails_ErrorWithoutScan()
    {
        var harness = new Harness { Connectivity = new QueueConnectivity(Fail()) };
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = "p2",
            },
        };
        var sut = await InitializedAsync(harness);

        await sut.ConnectAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Error, sut.Snapshot.State);
        Assert.Equal(ConnectionIssue.AllPresetsFailed, sut.Snapshot.Issue);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
    }

    [Fact]
    public async Task CheckHealth_AutoMode_RecoversAfterTwoFailures()
    {
        var harness = new Harness
        {
            // Подключение сохранённого профиля, две неудачи проверки,
            // неудача восстановления, затем рабочий кандидат.
            Connectivity = new QueueConnectivity(Ok(), Fail(), Fail(), Fail(), Ok()),
            Tester = new ScriptedTester((_, _) => Qualified("p2", 90, 95)),
        };
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Auto,
                AutoProfile = new AutoProfile(
                    "p1", "Первый", 120, 110, 120,
                    DateTimeOffset.UtcNow, "1.10.4", "sha256:x"),
            },
        };
        var sut = await InitializedAsync(harness);
        await sut.ConnectAsync(CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);

        await sut.CheckHealthAsync(CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);

        await sut.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Equal("p2", sut.Snapshot.PresetId);
        Assert.Equal(1, harness.Tester is ScriptedTester s ? s.Calls : 0);
        Assert.True(harness.Engine.StopCalls >= 1);
    }

    [Fact]
    public async Task CheckHealth_ManualMode_OnlyNotifies()
    {
        var harness = new Harness
        {
            Connectivity = new QueueConnectivity(Ok(), Fail(), Fail(), Fail()),
        };
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = "p1",
            },
        };
        var sut = await InitializedAsync(harness);
        await sut.ConnectAsync(CancellationToken.None);

        var notices = new List<string>();
        sut.Notification += (_, text) => notices.Add(text);

        await sut.CheckHealthAsync(CancellationToken.None);
        await sut.CheckHealthAsync(CancellationToken.None);
        await sut.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sut.Snapshot.State);
        Assert.Single(notices);
        Assert.Equal(0, harness.Tester is ScriptedTester s ? s.Calls : 0);
    }

    [Fact]
    public async Task StateChanged_ReportsSelectingProgress()
    {
        var harness = new Harness
        {
            Tester = new ScriptedTester((preset, _) => preset.Id switch
            {
                "p1" => Failed("p1"),
                "p2" => Qualified("p2", 80, 90),
                _ => Failed(preset.Id),
            }),
        };
        var sut = await InitializedAsync(harness);
        var states = new List<ConnectionSnapshot>();
        sut.StateChanged += (_, snapshot) => states.Add(snapshot);

        await sut.ConnectAsync(CancellationToken.None);

        var selecting = states.Where(s => s.State == ConnectionState.Selecting).ToList();
        Assert.NotEmpty(selecting);
        Assert.Contains(selecting, s => s.TestedCount == 1 && s.TotalCount == 3);
        Assert.Contains(selecting, s => s.HasQualifiedCandidate && s.BestPresetName == "Обход p2");
        Assert.Equal(ConnectionState.Connected, states[^1].State);
    }

    [Fact]
    public async Task Connect_CancelledDuringConnecting_StopsEngineAndReturnsDisconnected()
    {
        var connectivity = new BlockingConnectivity();
        var harness = new Harness { Connectivity = connectivity };
        harness.Config.Config = new AppConfig
        {
            Connection = new ConnectionSettings { Mode = ConnectionMode.Manual, ManualPresetId = "p1" },
        };
        var sut = await InitializedAsync(harness);

        using var cts = new CancellationTokenSource();
        var connect = sut.ConnectAsync(cts.Token);
        await connectivity.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ConnectionState.Disconnected, sut.Snapshot.State);
        Assert.True(harness.Engine.StopCalls >= 1);
    }
}
