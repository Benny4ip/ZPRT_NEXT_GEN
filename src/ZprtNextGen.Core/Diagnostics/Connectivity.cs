namespace ZprtNextGen.Core.Diagnostics;

/// <summary>Цель проверки доступности (connectivity).</summary>
public record ConnectivityTarget(string Id, string DisplayName, string Url, bool IsPrimary);

/// <summary>Результат проверки одной цели.</summary>
/// <param name="TimedOut"><c>true</c>, если ответа не дождались (таймаут/отмена) и <paramref name="ElapsedMs"/>
/// — это длительность ожидания, а не измеренная задержка.</param>
public record ConnectivityResult(
    ConnectivityTarget Target, bool Success, long ElapsedMs, string? Error, bool TimedOut = false);

/// <summary>Отчёт проверки доступности.</summary>
public record ConnectivityReport(IReadOnlyList<ConnectivityResult> Results)
{
    /// <summary><c>true</c>, если хотя бы одна первичная цель ответила успешно.</summary>
    public bool IsConnected => Results.Any(r => r.Target.IsPrimary && r.Success);
}

/// <summary>Проверка доступности целей через HTTP.</summary>
public interface IConnectivityService
{
    /// <summary>Проверить все цели, или одну по <paramref name="targetId"/>.</summary>
    /// <param name="ct">Токен отмены.</param>
    /// <param name="targetId">Идентификатор цели, или <c>null</c>, чтобы проверить все.</param>
    Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null);
}
