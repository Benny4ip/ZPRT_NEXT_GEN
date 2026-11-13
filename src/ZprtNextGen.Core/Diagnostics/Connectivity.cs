namespace ZprtNextGen.Core.Diagnostics;

/// <summary>Сервис, доступность которого проверяется.</summary>
public enum ServiceKind
{
    YouTube,
    Discord,
}

/// <summary>Цель проверки доступности (connectivity).</summary>
public record ConnectivityTarget(
    string Id,
    string DisplayName,
    string Url,
    bool IsPrimary,
    ServiceKind Service = ServiceKind.YouTube);

/// <summary>Результат проверки одной цели.</summary>
/// <param name="TimedOut"><c>true</c>, если ответа не дождались (таймаут/отмена) и <paramref name="ElapsedMs"/>
/// — это длительность ожидания, а не измеренная задержка.</param>
public record ConnectivityResult(
    ConnectivityTarget Target, bool Success, long ElapsedMs, string? Error, bool TimedOut = false);

/// <summary>Отчёт проверки доступности.</summary>
public record ConnectivityReport(IReadOnlyList<ConnectivityResult> Results)
{
    /// <summary><c>true</c>, если хотя бы одна первичная цель ответила успешно (старый контракт).</summary>
    public bool IsConnected => Results.Any(r => r.Target.IsPrimary && r.Success);

    /// <summary>Доступен хотя бы один первичный узел YouTube.</summary>
    public bool YouTubeOk => Results.Any(
        r => r.Target.Service == ServiceKind.YouTube && r.Target.IsPrimary && r.Success);

    /// <summary>Доступен хотя бы один первичный узел Discord.</summary>
    public bool DiscordOk => Results.Any(
        r => r.Target.Service == ServiceKind.Discord && r.Target.IsPrimary && r.Success);

    /// <summary>Работают оба сервиса — только такой обход считается рабочим.</summary>
    public bool BothOk => YouTubeOk && DiscordOk;

    /// <summary>Минимальная задержка успешной первичной цели YouTube.</summary>
    public long? YouTubeLatencyMs => MinPrimaryLatency(ServiceKind.YouTube);

    /// <summary>Минимальная задержка успешной первичной цели Discord.</summary>
    public long? DiscordLatencyMs => MinPrimaryLatency(ServiceKind.Discord);

    /// <summary>Худшая из двух задержек: по ней выбирается лучший обход.</summary>
    public long? ScoreMs => YouTubeLatencyMs is { } youtube && DiscordLatencyMs is { } discord
        ? Math.Max(youtube, discord)
        : null;

    private long? MinPrimaryLatency(ServiceKind service)
    {
        var latencies = Results
            .Where(r => r.Target.Service == service && r.Target.IsPrimary && r.Success)
            .Select(r => r.ElapsedMs)
            .ToList();

        return latencies.Count == 0 ? null : latencies.Min();
    }
}

/// <summary>Проверка доступности целей через HTTP.</summary>
public interface IConnectivityService
{
    /// <summary>Проверить все цели, или одну по <paramref name="targetId"/>.</summary>
    /// <param name="ct">Токен отмены.</param>
    /// <param name="targetId">Идентификатор цели, или <c>null</c>, чтобы проверить все.</param>
    Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null);
}

/// <summary>Быстрая проверка «интернет вообще есть» по нейтральным узлам.</summary>
public interface IInternetProbe
{
    /// <summary><c>true</c>, если хотя бы один нейтральный узел ответил.</summary>
    Task<bool> CheckInternetAsync(CancellationToken ct);
}
