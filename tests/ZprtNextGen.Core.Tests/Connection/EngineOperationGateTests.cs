using System;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Connection;

namespace ZprtNextGen.Core.Tests.Connection;

public class EngineOperationGateTests
{
    [Fact]
    public async Task Acquire_IsExclusive()
    {
        var gate = new EngineOperationGate();
        Assert.False(gate.IsBusy);

        await using (var first = await gate.AcquireAsync(CancellationToken.None))
        {
            Assert.True(gate.IsBusy);

            using var waiting = new CancellationTokenSource();
            var second = gate.AcquireAsync(waiting.Token);
            await Task.Delay(50);
            Assert.False(second.IsCompleted);

            waiting.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
        }

        Assert.False(gate.IsBusy);
        await using var third = await gate.AcquireAsync(CancellationToken.None);
        Assert.True(gate.IsBusy);
    }

    [Fact]
    public async Task Acquire_WhileBusy_RespectsCancellation()
    {
        var gate = new EngineOperationGate();
        await using var first = await gate.AcquireAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.AcquireAsync(cts.Token));

        Assert.True(gate.IsBusy);
    }

    [Fact]
    public async Task DoubleDispose_ReleasesOnce()
    {
        var gate = new EngineOperationGate();
        var lease = await gate.AcquireAsync(CancellationToken.None);
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        await using var first = await gate.AcquireAsync(CancellationToken.None);
        var second = gate.AcquireAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
    }
}
