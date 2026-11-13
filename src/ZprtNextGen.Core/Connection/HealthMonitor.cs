namespace ZprtNextGen.Core.Connection;

using ZprtNextGen.Core.Logging;

/// <summary>Периодическая фоновая проверка соединения.</summary>
public sealed class HealthMonitor : IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _tick;
    private readonly IAppLogger? _logger;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public HealthMonitor(TimeSpan interval, Func<CancellationToken, Task> tick, IAppLogger? logger = null)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        _interval = interval;
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _logger = logger;
    }

    /// <summary>Запустить цикл (повторный вызов игнорируется).</summary>
    public void Start()
    {
        if (_loop is not null)
            return;

        _cts = new CancellationTokenSource();
        _loop = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _tick(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.Error("Фоновая проверка соединения: " + ex.Message, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка монитора.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* остановка монитора не должна бросать */ }
        }

        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
}
