using System.Diagnostics;
using System.Net;
using System.Net.Http;
using ZprtNextGen.Core.Diagnostics;

namespace ZprtNextGen.Core.Tests.Diagnostics;

public class ConnectivityServiceTests
{
    private static readonly string[] s_hosts =
    [
        "www.youtube.com",
        "redirector.googlevideo.com",
        "i.ytimg.com",
        "discord.com",
        "gateway.discord.gg",
        "cdn.discordapp.com"
    ];

    private sealed class FakeHandler : HttpMessageHandler
    {
        private sealed class Plan
        {
            public required HttpStatusCode Status { get; init; }
            public TimeSpan? Delay { get; init; }
            public Exception? Throw { get; init; }
        }

        private readonly Dictionary<string, Plan> _plans = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Host, DateTimeOffset Started)> _starts = new();
        private readonly object _lock = new();

        public void Add(string host, HttpStatusCode status, TimeSpan? delay = null, Exception? throwException = null)
        {
            _plans[host] = new Plan { Status = status, Delay = delay, Throw = throwException };
        }

        public IReadOnlyList<(string Host, DateTimeOffset Started)> Starts
        {
            get
            {
                lock (_lock) { return _starts.ToList(); }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            var host = request.RequestUri?.Host ?? string.Empty;
            lock (_lock) { _starts.Add((host, DateTimeOffset.UtcNow)); }

            var plan = _plans.TryGetValue(host, out var p) ? p : new Plan { Status = HttpStatusCode.OK };
            if (plan.Throw is not null)
            {
                throw plan.Throw;
            }

            if (plan.Delay is { } delay && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            return new HttpResponseMessage(plan.Status);
        }
    }

    private static ConnectivityService CreateService(FakeHandler handler)
    {
        var client = new HttpClient(handler);
        return new ConnectivityService(client);
    }

    [Fact]
    public async Task CheckAsync_AllSuccess_ReportHasBothServices()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.OK);
        }

        var service = CreateService(handler);
        var report = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(6, report.Results.Count);
        Assert.Equal(
            new[] { "youtube", "youtube-cdn", "youtube-img", "discord-api", "discord-gateway", "discord-cdn" },
            report.Results.Select(r => r.Target.Id));
        Assert.Equal(2, report.Results.Count(r => r.Target.Service == ServiceKind.YouTube && r.Target.IsPrimary));
        Assert.Equal(2, report.Results.Count(r => r.Target.Service == ServiceKind.Discord && r.Target.IsPrimary));
        Assert.True(report.YouTubeOk);
        Assert.True(report.DiscordOk);
        Assert.True(report.BothOk);
    }

    [Fact]
    public async Task Report_YouTubeOnly_IsNotBothOk()
    {
        var handler = new FakeHandler();
        handler.Add("www.youtube.com", HttpStatusCode.OK);
        handler.Add("redirector.googlevideo.com", HttpStatusCode.OK);
        handler.Add("i.ytimg.com", HttpStatusCode.OK);
        handler.Add("discord.com", HttpStatusCode.ServiceUnavailable);
        handler.Add("gateway.discord.gg", HttpStatusCode.ServiceUnavailable);
        handler.Add("cdn.discordapp.com", HttpStatusCode.ServiceUnavailable);

        var report = await CreateService(handler).CheckAsync(CancellationToken.None);

        Assert.True(report.YouTubeOk);
        Assert.False(report.DiscordOk);
        Assert.False(report.BothOk);
        Assert.NotNull(report.YouTubeLatencyMs);
        Assert.Null(report.DiscordLatencyMs);
        Assert.Null(report.ScoreMs);
    }

    [Fact]
    public async Task Report_LatencyIsMinPerService_ScoreIsMax()
    {
        var handler = new FakeHandler();
        handler.Add("www.youtube.com", HttpStatusCode.OK, TimeSpan.FromMilliseconds(120));
        handler.Add("redirector.googlevideo.com", HttpStatusCode.OK, TimeSpan.FromMilliseconds(80));
        handler.Add("i.ytimg.com", HttpStatusCode.OK, TimeSpan.FromMilliseconds(10));
        handler.Add("discord.com", HttpStatusCode.OK, TimeSpan.FromMilliseconds(150));
        handler.Add("gateway.discord.gg", HttpStatusCode.OK, TimeSpan.FromMilliseconds(95));
        handler.Add("cdn.discordapp.com", HttpStatusCode.OK, TimeSpan.FromMilliseconds(1));

        var report = await CreateService(handler).CheckAsync(CancellationToken.None);

        Assert.True(report.YouTubeOk);
        Assert.True(report.DiscordOk);
        Assert.InRange(report.YouTubeLatencyMs!.Value, 60, 250);
        Assert.InRange(report.DiscordLatencyMs!.Value, 80, 300);
        Assert.Equal(
            Math.Max(report.YouTubeLatencyMs.Value, report.DiscordLatencyMs.Value),
            report.ScoreMs);
    }

    [Fact]
    public async Task CheckAsync_404_IsSuccess()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.NotFound);
        }

        var report = await CreateService(handler).CheckAsync(CancellationToken.None);

        Assert.All(report.Results, r => Assert.True(r.Success));
        Assert.True(report.BothOk);
    }

    [Fact]
    public async Task CheckAsync_500_IsFailure()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.InternalServerError);
        }

        var report = await CreateService(handler).CheckAsync(CancellationToken.None);

        Assert.All(report.Results, r => Assert.False(r.Success));
        Assert.False(report.BothOk);
    }

    [Fact]
    public async Task CheckAsync_PrimaryFailure_RecordedWithError()
    {
        var handler = new FakeHandler();
        handler.Add("discord.com", HttpStatusCode.OK, throwException: new HttpRequestException("connect failed"));
        foreach (var host in s_hosts.Where(h => h != "discord.com"))
        {
            handler.Add(host, HttpStatusCode.OK);
        }

        var report = await CreateService(handler).CheckAsync(CancellationToken.None);

        var failed = Assert.Single(report.Results, r => r.Target.Id == "discord-api");
        Assert.False(failed.Success);
        Assert.NotNull(failed.Error);
        Assert.Contains("connect failed", failed.Error, StringComparison.Ordinal);
        Assert.True(report.DiscordOk); // gateway ещё работает
    }

    [Fact]
    public async Task CheckAsync_Timeout_CompletesAsFailure()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.OK, TimeSpan.FromSeconds(5));
        }

        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        var sw = Stopwatch.StartNew();
        var report = await service.CheckAsync(cts.Token);
        sw.Stop();

        Assert.False(report.IsConnected);
        Assert.Equal(6, report.Results.Count);
        Assert.All(report.Results, r =>
        {
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
            Assert.True(r.TimedOut);
        });
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 0, 5000);
    }

    [Fact]
    public async Task CheckAsync_WithTargetId_ChecksOnlyThatTarget()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.OK);
        }

        var service = CreateService(handler);
        var report = await service.CheckAsync(CancellationToken.None, "youtube");

        var result = Assert.Single(report.Results);
        Assert.Equal("youtube", result.Target.Id);
        Assert.True(result.Success);
        Assert.Single(handler.Starts);
        Assert.Equal("www.youtube.com", handler.Starts[0].Host);
    }

    [Fact]
    public async Task CheckAsync_RunsInParallel()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.OK, TimeSpan.FromMilliseconds(300));
        }

        var service = CreateService(handler);

        var sw = Stopwatch.StartNew();
        var report = await service.CheckAsync(CancellationToken.None);
        sw.Stop();

        Assert.True(report.BothOk);
        Assert.Equal(6, handler.Starts.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"expected under 1 s, took {sw.Elapsed}");
    }

    [Fact]
    public async Task CheckInternetAsync_AnyNeutralProbeSucceeds_ReturnsTrue()
    {
        var handler = new FakeHandler();
        handler.Add("www.msftconnecttest.com", HttpStatusCode.OK);
        handler.Add("captive.apple.com", HttpStatusCode.ServiceUnavailable);

        var service = CreateService(handler);

        Assert.True(await service.CheckInternetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckInternetAsync_BothProbesFail_ReturnsFalse()
    {
        var handler = new FakeHandler();
        handler.Add("www.msftconnecttest.com", HttpStatusCode.InternalServerError);
        handler.Add("captive.apple.com", HttpStatusCode.OK, throwException: new HttpRequestException("offline"));

        var service = CreateService(handler);

        Assert.False(await service.CheckInternetAsync(CancellationToken.None));
    }
}
