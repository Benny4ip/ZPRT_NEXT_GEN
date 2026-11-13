using System;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Connection;

namespace ZprtNextGen.Core.Tests.Connection;

public class HealthMonitorTests
{
    [Fact]
    public async Task Start_TicksRepeatedly()
    {
        var ticks = 0;
        await using var monitor = new HealthMonitor(
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                Interlocked.Increment(ref ticks);
                return Task.CompletedTask;
            });

        monitor.Start();

        await WaitUntilAsync(() => Volatile.Read(ref ticks) >= 2, TimeSpan.FromSeconds(5));
        Assert.True(Volatile.Read(ref ticks) >= 2);
    }

    [Fact]
    public async Task TickException_DoesNotStopLoop()
    {
        var ticks = 0;
        await using var monitor = new HealthMonitor(
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                var value = Interlocked.Increment(ref ticks);
                if (value == 1)
                    throw new InvalidOperationException("boom");

                return Task.CompletedTask;
            });

        monitor.Start();

        await WaitUntilAsync(() => Volatile.Read(ref ticks) >= 2, TimeSpan.FromSeconds(5));
        Assert.True(Volatile.Read(ref ticks) >= 2);
    }

    [Fact]
    public async Task Dispose_StopsTicks()
    {
        var ticks = 0;
        var monitor = new HealthMonitor(
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                Interlocked.Increment(ref ticks);
                return Task.CompletedTask;
            });

        monitor.Start();
        await WaitUntilAsync(() => Volatile.Read(ref ticks) >= 1, TimeSpan.FromSeconds(5));

        await monitor.DisposeAsync();

        var afterDispose = Volatile.Read(ref ticks);
        await Task.Delay(100);
        Assert.Equal(afterDispose, Volatile.Read(ref ticks));
    }

    [Fact]
    public async Task StartTwice_DoesNotCreateSecondLoop()
    {
        var ticks = 0;
        await using var monitor = new HealthMonitor(
            TimeSpan.FromMilliseconds(50),
            _ =>
            {
                Interlocked.Increment(ref ticks);
                return Task.CompletedTask;
            });

        monitor.Start();
        monitor.Start();

        await WaitUntilAsync(() => Volatile.Read(ref ticks) >= 3, TimeSpan.FromSeconds(5));
        Assert.InRange(Volatile.Read(ref ticks), 3, 6);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > timeout)
                throw new TimeoutException("Условие не выполнено за отведённое время.");

            await Task.Delay(10);
        }
    }
}
