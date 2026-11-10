using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ZprtNextGen.Core.Process;

namespace ZprtNextGen.Core.Tests.Process;

public class EngineProcessManagerTests
{
    private static string Exe => Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Start_CapturesOutputAndRuns()
    {
        var workDir = TempDir();
        using var manager = new EngineProcessManager();

        var outputTcs = new TaskCompletionSource<string>();
        var stoppedTcs = new TaskCompletionSource<bool>();

        manager.OutputReceived += (_, data) =>
        {
            if (data.Contains("ready"))
                outputTcs.TrySetResult(data);
        };
        manager.StatusChanged += (_, e) =>
        {
            if (e.Status == EngineStatus.Stopped)
                stoppedTcs.TrySetResult(true);
        };

        await manager.StartAsync(Exe, @"/c echo ready & ping -n 3 127.0.0.1 >nul", workDir, CancellationToken.None);

        var output = await outputTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("ready", output);

        Assert.Equal(EngineStatus.Running, manager.Status);
        Assert.NotNull(manager.OwnedProcessId);

        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(EngineStatus.Stopped, manager.Status);
        Assert.Null(manager.OwnedProcessId);
    }

    [Fact]
    public async Task Stop_KillsOwnedProcess()
    {
        var workDir = TempDir();
        using var manager = new EngineProcessManager();

        await manager.StartAsync(Exe, @"/c ping -n 30 127.0.0.1 >nul", workDir, CancellationToken.None);
        var pid = manager.OwnedProcessId!.Value;

        await manager.StopAsync(CancellationToken.None);

        Assert.Throws<ArgumentException>(() => global::System.Diagnostics.Process.GetProcessById(pid));
        Assert.Equal(EngineStatus.Stopped, manager.Status);
        Assert.Null(manager.OwnedProcessId);
    }

    [Fact]
    public async Task IsForeignInstanceRunning_DetectsCopiedExe()
    {
        var dir = TempDir();
        var copiedExe = Path.Combine(dir, "zprt-test-engine.exe");
        File.Copy(Exe, copiedExe);

        global::System.Diagnostics.Process? foreign = null;
        try
        {
            foreign = global::System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = copiedExe,
                Arguments = @"/c ping -n 30 127.0.0.1 >nul",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            using var manager = new EngineProcessManager("zprt-test-engine");
            Assert.True(manager.IsForeignInstanceRunning());
        }
        finally
        {
            if (foreign is not null)
            {
                foreign.Kill();
                foreign.WaitForExit();
                foreign.Dispose();
            }
        }

        using var managerAfter = new EngineProcessManager("zprt-test-engine");
        Assert.False(managerAfter.IsForeignInstanceRunning());
    }

    [Fact]
    public async Task KillForeignInstances_KillsCopiedExe()
    {
        var dir = TempDir();
        var copiedExe = Path.Combine(dir, "zprt-test-kill.exe");
        File.Copy(Exe, copiedExe);

        using var foreign = global::System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = copiedExe,
            Arguments = @"/c ping -n 30 127.0.0.1 >nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        using var manager = new EngineProcessManager("zprt-test-kill");
        Assert.True(manager.IsForeignInstanceRunning());

        manager.KillForeignInstances();

        await foreign.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(foreign.HasExited);
        Assert.False(manager.IsForeignInstanceRunning());
    }

    [Fact]
    public async Task Start_MissingExecutable_Throws()
    {
        var missingPath = Path.Combine(TempDir(), "winws.exe");
        using var manager = new EngineProcessManager();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => manager.StartAsync(missingPath, string.Empty, Path.GetTempPath(), CancellationToken.None));
        Assert.True(ex is FileNotFoundException or System.ComponentModel.Win32Exception, $"Unexpected exception type: {ex.GetType()}");
        Assert.Equal(EngineStatus.Error, manager.Status);
    }

    [Fact]
    public async Task StopAsync_WhenNoOwnedProcess_IsIdempotent()
    {
        using var manager = new EngineProcessManager();
        await manager.StopAsync(CancellationToken.None);
        await manager.StopAsync(CancellationToken.None);
        Assert.Equal(EngineStatus.Stopped, manager.Status);
        Assert.Null(manager.OwnedProcessId);
    }

    [Fact]
    public async Task OnExited_FromPreviousProcess_DoesNotMarkCurrentStopped()
    {
        var workDir = TempDir();
        using var manager = new EngineProcessManager();

        await manager.StartAsync(Exe, @"/c ping -n 30 127.0.0.1 >nul", workDir, CancellationToken.None);

        // Событие Exited от чужого (старого) процесса не должно сбрасывать текущий.
        // Раньше OnExited сравнивал PID с _process.Id и помечал новый процесс остановленным.
        using var previous = global::System.Diagnostics.Process.GetCurrentProcess();
        var onExited = typeof(EngineProcessManager).GetMethod(
            "OnExited", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!;
        onExited.Invoke(manager, new object?[] { previous, EventArgs.Empty });

        Assert.Equal(EngineStatus.Running, manager.Status);
        Assert.NotNull(manager.OwnedProcessId);

        await manager.StopAsync(CancellationToken.None);
        Assert.Equal(EngineStatus.Stopped, manager.Status);
    }
}
