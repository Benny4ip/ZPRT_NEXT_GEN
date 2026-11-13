namespace ZprtNextGen.Core.Discord;

using System.Diagnostics;
using System.IO;
using ZprtNextGen.Core.Logging;

/// <summary>Где искать данные Discord.</summary>
public sealed record DiscordFixPaths(string AppData, string LocalAppData)
{
    /// <summary>Пути текущего пользователя.</summary>
    public static DiscordFixPaths CreateDefault() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}

/// <summary>Итог очистки Discord.</summary>
public enum DiscordFixStatus
{
    Success,
    NotInstalled,
    Failed,
}

/// <summary>Результат Fix Discord.</summary>
public sealed record DiscordFixResult(
    DiscordFixStatus Status,
    int RemovedEntries,
    int StoppedProcesses,
    string? ErrorMessage,
    bool FullReset)
{
    public bool Success => Status == DiscordFixStatus.Success;
}

/// <summary>Завершение и запуск клиентов Discord (выделено для тестируемости).</summary>
public interface IDiscordProcessController
{
    /// <summary>Завершить процессы Discord под указанными каталогами установки.</summary>
    Task<int> StopAsync(IReadOnlyList<string> installRoots, CancellationToken ct);

    /// <summary>Запустить исполняемый файл; <c>false</c>, если запуск не удался.</summary>
    Task<bool> StartAsync(string executable, string arguments, CancellationToken ct);
}

/// <summary>Очистка кэша Discord без выхода из аккаунта (и полный сброс по запросу).</summary>
public interface IDiscordFixService
{
    Task<DiscordFixResult> FixAsync(bool fullReset, CancellationToken ct);
}

/// <summary>
/// Завершает клиенты Discord, удаляет безопасные папки кэша и перезапускает клиент.
/// Полный сброс дополнительно удаляет cookies и локальные данные аккаунта.
/// </summary>
public sealed class DiscordFixService : IDiscordFixService
{
    /// <summary>Известные клиенты Discord: папка, имя процесса.</summary>
    public static IReadOnlyList<(string Folder, string ProcessName)> Clients { get; } = new[]
    {
        ("Discord", "Discord"),
        ("DiscordPTB", "DiscordPTB"),
        ("DiscordCanary", "DiscordCanary"),
    };

    /// <summary>Безопасные для удаления папки кэша (аккаунт не затрагивается).</summary>
    public static IReadOnlyList<string> SafeEntries { get; } = new[]
    {
        "Cache",
        "Code Cache",
        "GPUCache",
        "DawnCache",
        "ShaderCache",
        "Service Worker",
        "Network Persistent State",
    };

    /// <summary>Дополнительные данные, удаляемые только при полном сбросе.</summary>
    public static IReadOnlyList<string> FullResetEntries { get; } = new[]
    {
        "Cookies",
        "Local Storage",
        "Session Storage",
        "IndexedDB",
        "Network",
    };

    private const int DeleteAttempts = 5;

    private readonly DiscordFixPaths _paths;
    private readonly IDiscordProcessController _processes;
    private readonly IAppLogger? _logger;

    public DiscordFixService(
        DiscordFixPaths paths, IDiscordProcessController processes, IAppLogger? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _logger = logger;
    }

    /// <summary>Сервис с системными путями и настоящим контроллером процессов.</summary>
    public static DiscordFixService CreateDefault(IAppLogger? logger = null) =>
        new(DiscordFixPaths.CreateDefault(), new DiscordProcessController(), logger);

    /// <inheritdoc/>
    public async Task<DiscordFixResult> FixAsync(bool fullReset, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var installs = Discover();
        if (installs.Count == 0)
        {
            _logger?.Info("Fix Discord: установка Discord не найдена.");
            return new DiscordFixResult(DiscordFixStatus.NotInstalled, 0, 0, null, fullReset);
        }

        var stopped = await _processes
            .StopAsync(installs.Select(install => install.Root).ToList(), ct)
            .ConfigureAwait(false);

        var removed = RemoveEntries(installs, fullReset, ct);

        var started = 0;
        foreach (var install in installs)
        {
            ct.ThrowIfCancellationRequested();

            var ok = false;
            if (install.UpdateExe is not null)
            {
                ok = await _processes
                    .StartAsync(install.UpdateExe, $"--processStart {install.ProcessName}.exe", ct)
                    .ConfigureAwait(false);
            }
            else if (install.AppExe is not null)
            {
                ok = await _processes.StartAsync(install.AppExe, string.Empty, ct).ConfigureAwait(false);
            }

            if (ok)
                started++;
            else
                _logger?.Error($"Fix Discord: не удалось перезапустить {install.ProcessName}.");
        }

        if (started == 0)
        {
            return new DiscordFixResult(
                DiscordFixStatus.Failed, removed, stopped,
                "Кэш очищен, но перезапустить Discord не удалось. Запусти его вручную.", fullReset);
        }

        _logger?.Info($"Fix Discord: удалено записей — {removed}, остановлено процессов — {stopped}.");

        return new DiscordFixResult(DiscordFixStatus.Success, removed, stopped, null, fullReset);
    }

    private List<DiscordInstall> Discover()
    {
        var installs = new List<DiscordInstall>();

        foreach (var (folder, processName) in Clients)
        {
            var root = Path.Combine(_paths.LocalAppData, folder);
            if (!Directory.Exists(root))
                continue;

            var updateExe = Path.Combine(root, "Update.exe");
            var hasUpdate = File.Exists(updateExe);

            var appExe = FindAppExecutable(root, processName);

            if (!hasUpdate && appExe is null)
                continue;

            installs.Add(new DiscordInstall(
                folder, processName, root, hasUpdate ? updateExe : null, appExe));
        }

        return installs;
    }

    private static string? FindAppExecutable(string root, string processName)
    {
        try
        {
            return Directory
                .EnumerateDirectories(root, "app-*")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, processName + ".exe"))
                .FirstOrDefault(File.Exists);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private int RemoveEntries(IReadOnlyList<DiscordInstall> installs, bool fullReset, CancellationToken ct)
    {
        var entries = new List<string>(SafeEntries);
        if (fullReset)
            entries.AddRange(FullResetEntries);

        var removed = 0;

        foreach (var install in installs)
        {
            var roaming = Path.Combine(_paths.AppData, install.Folder);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var path = Path.Combine(roaming, entry);
                if (DeletePath(path))
                    removed++;
            }
        }

        return removed;
    }

    private bool DeletePath(string path)
    {
        for (var attempt = 0; attempt < DeleteAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    return true;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }

                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts - 1)
                {
                    _logger?.Error($"Fix Discord: не удалось удалить «{path}»: {ex.Message}", ex);
                    return false;
                }

                Thread.Sleep(100);
            }
        }

        return false;
    }

    private sealed record DiscordInstall(
        string Folder,
        string ProcessName,
        string Root,
        string? UpdateExe,
        string? AppExe);
}

/// <summary>Системный контроллер процессов Discord: фильтрация по пути установки.</summary>
public sealed class DiscordProcessController : IDiscordProcessController
{
    private static readonly string[] ProcessNames = ["Discord", "DiscordPTB", "DiscordCanary", "Update"];
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public async Task<int> StopAsync(IReadOnlyList<string> installRoots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installRoots);

        var processes = new List<Process>();

        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null || !IsUnderAnyRoot(path, installRoots))
                    {
                        process.Dispose();
                        continue;
                    }

                    processes.Add(process);
                }
                catch (Exception)
                {
                    process.Dispose();
                }
            }
        }

        var stopped = 0;
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    stopped++;
                }
            }
            catch (Exception)
            {
                // Процесс мог завершиться сам или не позволить завершение.
            }
        }

        var deadline = DateTime.UtcNow + StopTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (processes.All(HasExited))
                break;

            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        foreach (var process in processes)
            process.Dispose();

        return stopped;
    }

    /// <inheritdoc/>
    public Task<bool> StartAsync(string executable, string arguments, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ct.ThrowIfCancellationRequested();

        try
        {
            var startInfo = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            };

            Process.Start(startInfo)?.Dispose();
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception) { return true; }
    }

    private static bool IsUnderAnyRoot(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
