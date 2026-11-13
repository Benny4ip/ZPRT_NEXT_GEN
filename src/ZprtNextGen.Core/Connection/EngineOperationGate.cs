namespace ZprtNextGen.Core.Connection;

/// <summary>Шлюз, сериализующий операции, меняющие состояние движка.</summary>
public interface IEngineOperationGate
{
    /// <summary>Идёт ли операция прямо сейчас.</summary>
    bool IsBusy { get; }

    /// <summary>Получить эксклюзивный доступ; освобождается через <see cref="IAsyncDisposable"/>.</summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken ct);
}

/// <summary>Шлюз на <see cref="SemaphoreSlim"/>.</summary>
public sealed class EngineOperationGate : IEngineOperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        Interlocked.Exchange(ref _busy, 1);
        return new Lease(this);
    }

    private void Release()
    {
        Interlocked.Exchange(ref _busy, 0);
        _semaphore.Release();
    }

    private sealed class Lease(EngineOperationGate gate) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release();

            return ValueTask.CompletedTask;
        }
    }
}
