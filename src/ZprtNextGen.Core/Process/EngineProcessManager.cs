namespace ZprtNextGen.Core.Process;

using System.Diagnostics;
using System.Text;

/// <summary>Аргументы события изменения статуса управляемого процесса движка.</summary>
public class EngineStatusChangedEventArgs : EventArgs
{
    /// <summary>Новый статус.</summary>
    public EngineStatus Status { get; }

    /// <summary>Описание изменения (может быть <c>null</c>).</summary>
    public string? Detail { get; }

    public EngineStatusChangedEventArgs(EngineStatus status, string? detail = null)
    {
        Status = status;
        Detail = detail;
    }
}

/// <summary>Управление жизненным циклом процесса движка (winws) с проверкой владения по PID.</summary>
public interface IEngineProcessManager : IDisposable
{
    /// <summary>Текущий статус процесса.</summary>
    EngineStatus Status { get; }

    /// <summary>PID процесса, которым владеет этот менеджер, или <c>null</c>.</summary>
    int? OwnedProcessId { get; }

    /// <summary>Событие изменения статуса.</summary>
    event EventHandler<EngineStatusChangedEventArgs> StatusChanged;

    /// <summary>Событие получения вывода процесса (stdout/stderr).</summary>
    event EventHandler<string> OutputReceived;

    /// <summary>Запустить процесс движка.</summary>
    /// <param name="executablePath">Путь к исполняемому файлу.</param>
    /// <param name="arguments">Аргументы командной строки.</param>
    /// <param name="workingDirectory">Рабочая директория.</param>
    /// <param name="ct">Токен отмены.</param>
    Task StartAsync(string executablePath, string arguments, string workingDirectory, CancellationToken ct);

    /// <summary>Остановить принадлежащий менеджеру процесс.</summary>
    /// <param name="ct">Токен отмены.</param>
    /// <exception cref="InvalidOperationException">Чужий процесс (PID не совпадает) или процесса нет.</exception>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Признак того, что процесс с именем движка запущен кем-то другим,
    /// т. е. есть экземпляр с PID, отличным от <see cref="OwnedProcessId"/>.
    /// </summary>
    bool IsForeignInstanceRunning();
}

public class EngineProcessManager : IEngineProcessManager
{
    private readonly string _engineProcessName;
    private readonly object _sync = new();

    private Process? _process;
    private EngineStatus _status = EngineStatus.Stopped;
    private int? _ownedProcessId;

    public EngineProcessManager(string engineProcessName = "winws")
    {
        _engineProcessName = string.IsNullOrEmpty(engineProcessName) ? "winws" : engineProcessName;
    }

    public EngineStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public int? OwnedProcessId
    {
        get
        {
            lock (_sync)
            {
                return _ownedProcessId;
            }
        }
    }

    public event EventHandler<EngineStatusChangedEventArgs> StatusChanged = delegate { };

    public event EventHandler<string> OutputReceived = delegate { };

    public Task StartAsync(string executablePath, string arguments, string workingDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_process is not null && !_process.HasExited)
                throw new InvalidOperationException("Engine process is already running.");
        }

        RaiseStatusChanged(EngineStatus.Starting, "starting");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Exited += OnExited;

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _status = EngineStatus.Error;
            }

            RaiseStatusChanged(EngineStatus.Error, ex.Message);

            process.OutputDataReceived -= OnOutputDataReceived;
            process.ErrorDataReceived -= OnErrorDataReceived;
            process.Exited -= OnExited;
            throw;
        }

        lock (_sync)
        {
            _process = process;
            _ownedProcessId = process.Id;
            _status = EngineStatus.Running;
        }

        RaiseStatusChanged(EngineStatus.Running, null);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Process process;
        lock (_sync)
        {
            if (_process is null || !_ownedProcessId.HasValue)
                throw new InvalidOperationException("No owned engine process to stop.");
            process = _process;
        }

        if (!process.HasExited)
        {
            RaiseStatusChanged(EngineStatus.Stopping, "stopping");

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Уже завершился между проверкой HasExited и Kill.
            }

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _status = EngineStatus.Error;
                }

                RaiseStatusChanged(EngineStatus.Error, "stop timeout");
                throw;
            }
        }

        lock (_sync)
        {
            _status = EngineStatus.Stopped;
            _ownedProcessId = null;
            _process = null;
        }

        RaiseStatusChanged(EngineStatus.Stopped, null);

        process.Dispose();
    }

    public bool IsForeignInstanceRunning()
    {
        int? ownedId;
        lock (_sync)
        {
            ownedId = _ownedProcessId;
        }

        foreach (var process in Process.GetProcessesByName(_engineProcessName))
        {
            try
            {
                if (!ownedId.HasValue || process.Id != ownedId.Value)
                    return true;
            }
            catch
            {
                // Процесс недоступен для текущего пользователя — игнорируем.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    public void Dispose()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
            _ownedProcessId = null;
            _status = EngineStatus.Stopped;
        }

        if (process is not null)
        {
            process.OutputDataReceived -= OnOutputDataReceived;
            process.ErrorDataReceived -= OnErrorDataReceived;
            process.Exited -= OnExited;

            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Лучшая попытка завершить принадлежащий менеджеру процесс.
                }
            }

            process.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnOutputDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
            OutputReceived.Invoke(this, e.Data);
    }

    private void OnErrorDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
            OutputReceived.Invoke(this, e.Data);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        bool isOurs;
        int pid;
        lock (_sync)
        {
            if (_process is null || !_ownedProcessId.HasValue || _status == EngineStatus.Stopped)
            {
                isOurs = false;
                pid = 0;
            }
            else
            {
                pid = _process.Id;
                isOurs = pid == _ownedProcessId.Value;
                if (isOurs)
                {
                    _status = EngineStatus.Stopped;
                    _ownedProcessId = null;
                }
            }
        }

        if (isOurs)
            RaiseStatusChanged(EngineStatus.Stopped, null);
    }

    private void RaiseStatusChanged(EngineStatus status, string? detail)
    {
        StatusChanged.Invoke(this, new EngineStatusChangedEventArgs(status, detail));
    }
}
