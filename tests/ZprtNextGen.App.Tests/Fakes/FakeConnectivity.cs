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

    /// <summary>Если задан, проверка связи «висит» до завершения этого источника (для тестов гонок).</summary>
    public TaskCompletionSource<ConnectivityReport>? Gate { get; set; }

    public Task<ConnectivityReport> CheckAsync(CancellationToken ct)
    {
        CheckCalls++;
        return Gate is not null ? Gate.Task : Task.FromResult(Report);
    }
}
