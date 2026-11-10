using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Process;

namespace ZprtNextGen.Core.Tests.Fakes;

/// <summary>Фейковый менеджер процесса движка для тестов Core.</summary>
public sealed class FakeEngineProcess : IEngineProcessManager
{
    public EngineStatus Status { get; set; } = EngineStatus.Stopped;
    public int? OwnedProcessId => null;
    public event System.EventHandler<EngineStatusChangedEventArgs> StatusChanged = delegate { };
    public event System.EventHandler<string> OutputReceived = delegate { };

    public readonly List<(string ExecutablePath, string Arguments, string WorkingDirectory)> Starts = new();
    public int StopCalls;
    public int ThrowStartsRemaining;
    public bool ForeignRunning { get; set; }
    public Exception? StartException = new InvalidOperationException("start failed (fake)");

    public Task StartAsync(string executablePath, string arguments, string workingDirectory, CancellationToken ct)
    {
        Starts.Add((executablePath, arguments, workingDirectory));
        if (ThrowStartsRemaining > 0)
        {
            ThrowStartsRemaining--;
            throw StartException!;
        }
        Status = EngineStatus.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        StopCalls++;
        Status = EngineStatus.Stopped;
        return Task.CompletedTask;
    }

    public bool IsForeignInstanceRunning() => ForeignRunning;
    public void KillForeignInstances() { }
    public void Dispose() { }
}
