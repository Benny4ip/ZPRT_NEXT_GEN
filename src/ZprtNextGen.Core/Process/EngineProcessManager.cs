namespace ZprtNextGen.Core.Process;

using System.ComponentModel;
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

    /// <summary>Остановить принадлежащий менеджеру процесс. Идемпотентно: если процесса нет — не бросает.</summary>
    /// <param name="ct">Токен отмены.</param>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Признак того, что процесс с именем движка запущен кем-то другим,
    /// т. е. есть экземпляр с PID, отличным от <see cref="OwnedProcessId"/>.
    /// </summary>
    bool IsForeignInstanceRunning();

    /// <summary>
    /// Завершить все процессы движка, кроме принадлежащего этому менеджеру (по PID).
    /// Требует прав администратора.
    /// </summary>
    void KillForeignInstances();
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
            if (_process is not null)
            {
                if (!_process.HasExited)
                    throw new InvalidOperationException("Engine process is already running.");
                // Старый процесс уже завершился — освобождаем объект перед заменой.
                try { _process.Dispose(); }
                catch { /* лучшая попытка */ }
                _process = null;
            }
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
            try { process.Dispose(); } catch { /* лучшая попытка */ }
            throw;
        }

        lock (_sync)
        {
            _process = process;
            _ownedProcessId = process.Id;
            _status = EngineStatus.Running;
        }

        // Начинаем чтение вывода до события Running, чтобы подписчики не потеряли строки.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        RaiseStatusChanged(EngineStatus.Running, null);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Process? process = null;
        Process? stale = null;
        lock (_sync)
        {
            if (_process is null || !_ownedProcessId.HasValue)
            {
                // Нечего останавливать — идемпотентно (важно для обновления, когда движок уже остановлен).
                // Если процесс уже завершился сам, освобождаем его handle, иначе он утечёт до выхода.
                stale = _process;
                _status = EngineStatus.Stopped;
                _ownedProcessId = null;
                _process = null;
            }
            else
            {
                process = _process;
            }
        }

        if (process is null)
        {
            if (stale is not null)
            {
                stale.OutputDataReceived -= OnOutputDataReceived;
                stale.ErrorDataReceived -= OnErrorDataReceived;
                stale.Exited -= OnExited;
                try { stale.Dispose(); } catch { /* лучшая попытка */ }
            }

            return;
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
            catch (Win32Exception)
            {
                // Нет прав на завершение — всё равно пробуем дождаться выхода.
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

                RaiseStatusChanged(EngineStatus.Error, "stop cancelled");
                throw;
            }
            catch (TimeoutException)
            {
                // Не удалось остановить за отведённое время — сбрасываем состояние,
                // чтобы менеджер не застрял в Stopping и не блокировал перезапуск.
                lock (_sync)
                {
                    _status = EngineStatus.Error;
                    _ownedProcessId = null;
                    if (ReferenceEquals(_process, process))
                        _process = null;
                }

                RaiseStatusChanged(EngineStatus.Error, "stop timeout");
                throw new TimeoutException("Не удалось остановить движок за 5 секунд.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // Процесс завершился между проверкой и ожиданием.
            }
        }

        lock (_sync)
        {
            _status = EngineStatus.Stopped;
            _ownedProcessId = null;
            if (ReferenceEquals(_process, process))
                _process = null;
        }

        RaiseStatusChanged(EngineStatus.Stopped, null);

        try { process.Dispose(); } catch { /* лучшая попытка */ }
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

    public void KillForeignInstances()
    {
        Process[] foreign;
        lock (_sync)
        {
            foreign = Process.GetProcessesByName(_engineProcessName)
                .Where(p => !_ownedProcessId.HasValue || p.Id != _ownedProcessId.Value)
                .ToArray();
        }

        foreach (var proc in foreign)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Лучшая попытка — игнорируем ошибки (процесс мог уже завершиться).
            }
            finally
            {
                proc.Dispose();
            }
        }
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
        if (e.Data is null)
            return;

        try { OutputReceived.Invoke(this, e.Data); }
        catch { /* исключение в подписке не должно ронять поток вывода процесса */ }
    }

    private void OnErrorDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        try { OutputReceived.Invoke(this, e.Data); }
        catch { /* исключение в подписке не должно ронять поток вывода процесса */ }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        // Событие может прийти от старого процесса, уже заменённого новым.
        // Поэтому проверяем именно sender, а не текущее поле _process.
        bool isOurs = false;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _process) || !_ownedProcessId.HasValue || _status == EngineStatus.Stopped)
            {
                isOurs = false;
            }
            else
            {
                isOurs = true;
                _status = EngineStatus.Stopped;
                _ownedProcessId = null;
            }
        }

        if (isOurs)
            RaiseStatusChanged(EngineStatus.Stopped, null);
    }

    private void RaiseStatusChanged(EngineStatus status, string? detail)
    {
        try { StatusChanged.Invoke(this, new EngineStatusChangedEventArgs(status, detail)); }
        catch { /* исключение в подписке не должно ронять менеджер процесса */ }
    }
}
