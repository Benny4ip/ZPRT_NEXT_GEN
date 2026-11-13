namespace ZprtNextGen.Core.Diagnostics;

using System.Diagnostics;
using System.Net;

/// <summary>Проверка доступности целей через <see cref="HttpClient"/>.</summary>
public sealed class ConnectivityService(HttpClient client) : IConnectivityService, IInternetProbe
{
    /// <summary>Цели по умолчанию: по две первичные на каждый сервис и по одной вторичной.</summary>
    public static IReadOnlyList<ConnectivityTarget> DefaultTargets { get; } = new ConnectivityTarget[]
    {
        new("youtube", "YouTube", "https://www.youtube.com/generate_204", true, ServiceKind.YouTube),
        new("youtube-cdn", "YouTube CDN", "https://redirector.googlevideo.com/generate_204", true, ServiceKind.YouTube),
        new("youtube-img", "YouTube картинки", "https://i.ytimg.com/generate_204", false, ServiceKind.YouTube),
        new("discord-api", "Discord API", "https://discord.com/api/v10/gateway", true, ServiceKind.Discord),
        new("discord-gateway", "Discord Gateway", "https://gateway.discord.gg/?v=10&encoding=json", true, ServiceKind.Discord),
        new("discord-cdn", "Discord CDN", "https://cdn.discordapp.com/generate_204", false, ServiceKind.Discord),
    };

    /// <summary>Нейтральные узлы для проверки наличия интернета (не YouTube/Discord).</summary>
    public static IReadOnlyList<ConnectivityTarget> InternetProbeTargets { get; } = new ConnectivityTarget[]
    {
        new("internet-msft", "Microsoft Connect Test", "https://www.msftconnecttest.com/connecttest.txt", true),
        new("internet-apple", "Apple Captive Portal", "https://captive.apple.com/hotspot-detect.html", true),
    };

    private readonly HttpClient _client = client;

    /// <summary>Сервис с таймаутом HTTP-клиента 5 секунд.</summary>
    public static ConnectivityService CreateDefault()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return new ConnectivityService(client);
    }

    /// <inheritdoc/>
    public async Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null)
    {
        var targets = string.IsNullOrEmpty(targetId)
            ? DefaultTargets
            : DefaultTargets.Where(t => t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase)).ToList();

        var tasks = targets
            .Select(target => CheckTargetAsync(target, ct))
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new ConnectivityReport(results);
    }

    /// <inheritdoc/>
    public async Task<bool> CheckInternetAsync(CancellationToken ct)
    {
        var tasks = InternetProbeTargets
            .Select(target => CheckTargetAsync(target, ct))
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Any(result => result.Success);
    }

    private async Task<ConnectivityResult> CheckTargetAsync(ConnectivityTarget target, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, target.Url),
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            stopwatch.Stop();
            return new ConnectivityResult(
                target,
                response.StatusCode < HttpStatusCode.InternalServerError,
                stopwatch.ElapsedMilliseconds,
                null);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return new ConnectivityResult(target, false, stopwatch.ElapsedMilliseconds, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Отмена пользователем или таймаут HttpClient — результат проверки, а не падение всего прогона.
            stopwatch.Stop();
            return new ConnectivityResult(
                target, false, stopwatch.ElapsedMilliseconds,
                "Превышено время ожидания или проверка отменена.", TimedOut: true);
        }
        catch (Exception ex)
        {
            // Любая неожиданная ошибка (DNS, сокеты, битый URI) не должна ронять проверку остальных целей.
            stopwatch.Stop();
            return new ConnectivityResult(target, false, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
