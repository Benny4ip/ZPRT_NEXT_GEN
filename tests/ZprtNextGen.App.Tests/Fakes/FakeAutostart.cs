using ZprtNextGen.Core.Startup;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый сервис автозапуска с настраиваемыми исключениями.</summary>
public sealed class FakeAutostart : IAutostartService
{
    public bool Enabled { get; private set; }

    public Exception? EnableException { get; set; }

    public Exception? DisableException { get; set; }

    public int EnableCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public bool IsEnabled() => Enabled;

    public void Enable()
    {
        if (EnableException is not null)
            throw EnableException;
        Enabled = true;
        EnableCalls++;
    }

    public void Disable()
    {
        if (DisableException is not null)
            throw DisableException;
        Enabled = false;
        DisableCalls++;
    }
}
