using ZprtNextGen.Core.Startup;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый сервис автозапуска с настраиваемыми исключениями.</summary>
public sealed class FakeAutostart : IAutostartService
{
    public bool Enabled { get; set; }

    public Exception? EnableException { get; set; }

    public Exception? DisableException { get; set; }

    public int EnableCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public Task<bool> IsEnabledAsync(CancellationToken ct = default) => Task.FromResult(Enabled);

    public Task EnableAsync(CancellationToken ct = default)
    {
        if (EnableException is not null)
            throw EnableException;
        Enabled = true;
        EnableCalls++;
        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken ct = default)
    {
        if (DisableException is not null)
            throw DisableException;
        Enabled = false;
        DisableCalls++;
        return Task.CompletedTask;
    }
}
