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

    public Task<ConnectivityReport> CheckAsync(CancellationToken ct)
    {
        CheckCalls++;
        return Task.FromResult(Report);
    }
}
