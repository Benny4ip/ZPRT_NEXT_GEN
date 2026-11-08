namespace ZprtNextGen.Core.Diagnostics;

/// <summary>Цель проверки доступности (connectivity).</summary>
public record ConnectivityTarget(string Id, string DisplayName, string Url, bool IsPrimary);

/// <summary>Результат проверки одной цели.</summary>
public record ConnectivityResult(ConnectivityTarget Target, bool Success, long ElapsedMs, string? Error);

/// <summary>Отчёт проверки доступности.</summary>
public record ConnectivityReport(IReadOnlyList<ConnectivityResult> Results)
{
    /// <summary><c>true</c>, если хотя бы одна первичная цель ответила успешно.</summary>
    public bool IsConnected => Results.Any(r => r.Target.IsPrimary && r.Success);
}

/// <summary>Проверка доступности целей через HTTP.</summary>
public interface IConnectivityService
{
    /// <summary>Проверить все цели.</summary>
    /// <param name="ct">Токен отмены.</param>
    Task<ConnectivityReport> CheckAsync(CancellationToken ct);
}
