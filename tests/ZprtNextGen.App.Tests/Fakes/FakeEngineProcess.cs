using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Process;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый менеджер процесса движка для тестов.</summary>
public sealed class FakeEngineProcess : IEngineProcessManager
{
    public EngineStatus Status { get; set; } = EngineStatus.Stopped;

    public int? OwnedProcessId => null;

    public event System.EventHandler<EngineStatusChangedEventArgs> StatusChanged = delegate { };

    public event System.EventHandler<string> OutputReceived = delegate { };

    public List<(string ExecutablePath, string Arguments, string WorkingDirectory)> Starts { get; } = new();

    public int StopCalls { get; private set; }

    public bool ShouldThrowOnStart { get; set; }

    public Exception? StartException { get; set; } =
        new System.InvalidOperationException("Не удалось запустить движок (имитация сбоя).");

    public bool IsForeignInstanceRunningResult { get; set; }

    public Task StartAsync(string executablePath, string arguments, string workingDirectory, CancellationToken ct)
    {
        Starts.Add((executablePath, arguments, workingDirectory));
        if (ShouldThrowOnStart)
            throw StartException!;
        Status = EngineStatus.Running;
        StatusChanged(this, new EngineStatusChangedEventArgs(EngineStatus.Running, null));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        StopCalls++;
        Status = EngineStatus.Stopped;
        StatusChanged(this, new EngineStatusChangedEventArgs(EngineStatus.Stopped, null));
        return Task.CompletedTask;
    }

    public bool IsForeignInstanceRunning() => IsForeignInstanceRunningResult;

    public void RaiseOutput(string line) => OutputReceived(this, line);

    public void Dispose()
    {
    }
}
