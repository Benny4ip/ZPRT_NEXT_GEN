using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Diagnostics;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый сервис проверки связности с настраиваемым отчётом.</summary>
public sealed class FakeConnectivity : IConnectivityService
{
    public ConnectivityReport Report { get; set; } =
        new(new[]
        {
            new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), true, 10, null)
        });

    public int CheckCalls { get; private set; }
    public string? LastTargetId { get; private set; }

    /// <summary>Если задан, проверка связи «висит» до завершения этого источника (для тестов гонок).</summary>
    public TaskCompletionSource<ConnectivityReport>? Gate { get; set; }

    public Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null)
    {
        CheckCalls++;
        LastTargetId = targetId;
        if (Gate is not null)
            return Gate.Task;

        var results = string.IsNullOrEmpty(targetId)
            ? Report.Results
            : Report.Results.Where(r => r.Target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase)).ToList();

        return Task.FromResult(new ConnectivityReport(results));
    }
}
