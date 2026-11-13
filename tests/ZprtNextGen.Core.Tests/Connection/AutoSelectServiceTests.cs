using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Connection;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Tests.Connection;

public class AutoSelectServiceTests
{
    private static Preset P(string id, string name = "Обход") =>
        new(id, "general", name, "", "--wf-tcp=80");

    private sealed class ScriptedTester : IPresetTester
    {
        private readonly Func<Preset, int, PresetTestResult?> _factory;

        public ScriptedTester(Func<Preset, int, PresetTestResult?> factory) => _factory = factory;

        public List<string> Tested { get; } = new();
        public int ProgressReports { get; private set; }

        public Task<IReadOnlyList<PresetTestResult>> TestAllAsync(
            IReadOnlyList<Preset> presets, string engineRoot, GameFilterMode filter,
            IProgress<PresetTestProgress>? progress, CancellationToken ct)
        {
            var results = new List<PresetTestResult>();
            var total = presets.Count;

            for (var i = 0; i < presets.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                var preset = presets[i];
                var result = _factory(preset, i)
                    ?? throw new InvalidOperationException("scenario exhausted");

                Tested.Add(preset.Id);
                results.Add(result);
                ProgressReports++;
                progress?.Report(new PresetTestProgress(i + 1, total, preset, result));
            }

            return Task.FromResult<IReadOnlyList<PresetTestResult>>(results);
        }
    }

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

    [Fact]
    public async Task TestAsync_PicksLowestWorstLatency()
    {
        var tester = new ScriptedTester((preset, _) => preset.Id switch
        {
            "p1" => Qualified("p1", 100, 300),
            "p2" => Qualified("p2", 200, 150),
            "p3" => Qualified("p3", 50, 400),
            _ => Failed(preset.Id),
        });
        var sut = new AutoSelectService(tester);

        var outcome = await sut.TestAsync(
            [P("p1"), P("p2"), P("p3")], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.False(outcome.Canceled);
        Assert.Equal(3, outcome.Results.Count);
        Assert.Equal("p2", outcome.Best!.PresetId); // max(200,150)=200 — лучший
    }

    [Fact]
    public async Task TestAsync_IgnoresUnqualifiedPresets()
    {
        var tester = new ScriptedTester((preset, _) => preset.Id switch
        {
            "fast-youtube" => new PresetTestResult("fast-youtube", "YouTube", false, null, "нет Discord")
            {
                YoutubeOk = true,
                DiscordOk = false,
                YoutubeMs = 5,
            },
            "ok" => Qualified("ok", 120, 130),
            _ => Failed(preset.Id),
        });
        var sut = new AutoSelectService(tester);

        var outcome = await sut.TestAsync(
            [P("fast-youtube"), P("ok")], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.Equal("ok", outcome.Best!.PresetId);
    }

    [Fact]
    public async Task TestAsync_AllFailed_ReturnsNullBest()
    {
        var tester = new ScriptedTester((preset, _) => Failed(preset.Id));
        var sut = new AutoSelectService(tester);

        var outcome = await sut.TestAsync(
            [P("p1"), P("p2")], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.Null(outcome.Best);
        Assert.Equal(2, outcome.Results.Count);
    }

    [Fact]
    public async Task TestAsync_ReportsProgressAndRunningBest()
    {
        var tester = new ScriptedTester((preset, _) => preset.Id switch
        {
            "p1" => Qualified("p1", 100, 100),
            "p2" => Qualified("p2", 80, 90),
            _ => Failed(preset.Id),
        });
        var sut = new AutoSelectService(tester);
        var reports = new List<AutoSelectProgress>();

        var outcome = await sut.TestAsync(
            [P("p1"), P("p2")], @"C:\E", GameFilterMode.Disabled,
            new InlineProgress<AutoSelectProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(2, reports.Count);
        Assert.Equal(1, reports[0].TestedCount);
        Assert.True(reports[0].HasQualifiedCandidate);
        Assert.Equal("p1", reports[0].BestResult!.PresetId);
        Assert.Equal("p2", reports[1].BestResult!.PresetId);
        Assert.Equal("p2", outcome.Best!.PresetId);
    }

    [Fact]
    public async Task TestAsync_Cancellation_ReturnsPartialResults()
    {
        using var cts = new CancellationTokenSource();
        var tester = new ScriptedTester((preset, index) =>
        {
            if (index == 2)
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }

            return Qualified(preset.Id, 100 + index, 100 + index);
        });
        var sut = new AutoSelectService(tester);

        var outcome = await sut.TestAsync(
            [P("p1"), P("p2"), P("p3")], @"C:\E", GameFilterMode.Disabled, null, cts.Token);

        Assert.True(outcome.Canceled);
        Assert.Equal(2, outcome.Results.Count);
        Assert.Equal("p1", outcome.Best!.PresetId); // p1 и p2 равны — тай-брейк по id
    }

    [Fact]
    public async Task TestAsync_TieBreaksBySumThenId()
    {
        var tester = new ScriptedTester((preset, _) => preset.Id switch
        {
            "b" => Qualified("b", 100, 200),
            "a" => Qualified("a", 150, 150),
            _ => Failed(preset.Id),
        });
        var sut = new AutoSelectService(tester);

        var outcome = await sut.TestAsync(
            [P("b"), P("a")], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);

        Assert.Equal("a", outcome.Best!.PresetId); // одинаковый score 150, меньше сумма
    }
}
