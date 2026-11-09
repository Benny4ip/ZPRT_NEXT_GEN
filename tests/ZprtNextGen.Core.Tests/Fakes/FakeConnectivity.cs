using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Diagnostics;

namespace ZprtNextGen.Core.Tests.Fakes;

/// <summary>Фейковый сервис проверки связности для тестов Core.</summary>
public sealed class FakeConnectivity : IConnectivityService
{
    public ConnectivityReport Report { get; set; } = new(
        new[] { new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), true, 10, null) });
    public int CheckCalls;

    public Task<ConnectivityReport> CheckAsync(CancellationToken ct, string? targetId = null)
    {
        CheckCalls++;
        return Task.FromResult(Report);
    }
}
