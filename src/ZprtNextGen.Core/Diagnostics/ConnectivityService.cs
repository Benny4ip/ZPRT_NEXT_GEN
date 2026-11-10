namespace ZprtNextGen.Core.Diagnostics;

using System.Diagnostics;
using System.Net;

/// <summary>Проверка доступности целей через <see cref="HttpClient"/>.</summary>
public sealed class ConnectivityService(HttpClient client) : IConnectivityService
{
    /// <summary>Цели по умолчанию: первичные — Discord, Discord Gateway, YouTube;
    /// второстепенная — YouTube CDN.</summary>
    public static IReadOnlyList<ConnectivityTarget> DefaultTargets { get; } = new ConnectivityTarget[]
    {
        new("discord", "Discord", "https://discord.com", true),
        new("discord-gateway", "Discord Gateway", "https://gateway.discord.gg", true),
        new("youtube", "YouTube", "https://www.youtube.com", true),
        new("youtube-cdn", "YouTube CDN", "https://redirector.googlevideo.com", false)
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
                target, false, stopwatch.ElapsedMilliseconds, "Превышено время ожидания или проверка отменена.");
        }
        catch (Exception ex)
        {
            // Любая неожиданная ошибка (DNS, сокеты, битый URI) не должна ронять проверку остальных целей.
            stopwatch.Stop();
            return new ConnectivityResult(target, false, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
