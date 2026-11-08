using System.Diagnostics;
using System.Net;
using System.Net.Http;
using ZprtNextGen.Core.Diagnostics;

namespace ZprtNextGen.Core.Tests.Diagnostics;

public class ConnectivityServiceTests
{
    private static readonly string[] s_hosts =
    [
        "discord.com",
        "gateway.discord.gg",
        "www.youtube.com",
        "redirector.googlevideo.com"
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
    public async Task CheckAsync_AllSuccess_IsConnectedTrue()
    {
        var handler = new FakeHandler();
        foreach (var host in s_hosts)
        {
            handler.Add(host, HttpStatusCode.OK);
        }

        var service = CreateService(handler);
        var report = await service.CheckAsync(CancellationToken.None);

        Assert.True(report.IsConnected);
        Assert.Equal(4, report.Results.Count);
        Assert.Equal(new[] { "discord", "discord-gateway", "youtube", "youtube-cdn" },
            report.Results.Select(r => r.Target.Id));
        Assert.All(report.Results, r =>
        {
            Assert.True(r.Success);
            Assert.Null(r.Error);
            Assert.InRange(r.ElapsedMs, 0, long.MaxValue);
        });
    }

    [Fact]
    public async Task CheckAsync_OnlySecondarySuccess_IsConnectedFalse()
    {
        var handler = new FakeHandler();
        handler.Add("discord.com", HttpStatusCode.ServiceUnavailable);
        handler.Add("gateway.discord.gg", HttpStatusCode.ServiceUnavailable);
        handler.Add("www.youtube.com", HttpStatusCode.ServiceUnavailable);
        handler.Add("redirector.googlevideo.com", HttpStatusCode.OK);

        var service = CreateService(handler);
        var report = await service.CheckAsync(CancellationToken.None);

        Assert.False(report.IsConnected);
        Assert.Equal(4, report.Results.Count);

        var secondary = Assert.Single(report.Results, r => !r.Target.IsPrimary);
        Assert.True(secondary.Success);
        Assert.Null(secondary.Error);

        Assert.All(report.Results.Where(r => r.Target.IsPrimary), r =>
        {
            Assert.False(r.Success);
            Assert.Null(r.Error);
        });
    }

    [Fact]
    public async Task CheckAsync_PrimaryFailure_RecordedWithError()
    {
        var handler = new FakeHandler();
        handler.Add("discord.com", HttpStatusCode.OK, throwException: new HttpRequestException("connect failed"));
        handler.Add("gateway.discord.gg", HttpStatusCode.OK);
        handler.Add("www.youtube.com", HttpStatusCode.OK);
        handler.Add("redirector.googlevideo.com", HttpStatusCode.OK);

        var service = CreateService(handler);
        var report = await service.CheckAsync(CancellationToken.None);

        var failed = Assert.Single(report.Results, r => r.Target.Id == "discord");
        Assert.False(failed.Success);
        Assert.NotNull(failed.Error);
        Assert.Contains("connect failed", failed.Error, StringComparison.Ordinal);

        Assert.All(report.Results.Where(r => r.Target.Id != "discord"), r =>
        {
            Assert.True(r.Success);
            Assert.Null(r.Error);
        });
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
        Assert.Equal(4, report.Results.Count);
        Assert.All(report.Results, r =>
        {
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
        });
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 0, 5000);
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

        Assert.True(report.IsConnected);
        Assert.Equal(4, report.Results.Count);
        Assert.All(report.Results, r => Assert.True(r.Success));
        Assert.True(handler.Starts.Count == 4);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"expected under 1 s, took {sw.Elapsed}");
    }
}
