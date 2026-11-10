namespace ZprtNextGen.Core.Startup;

using System;
using System.Diagnostics;

/// <summary>Сборщик аргументов командной строки для задачи автозапуска ZPRT_NEXT_GEN.</summary>
public static class AutostartCommand
{
    /// <summary>Имя задачи в планировщике Windows.</summary>
    public const string TaskName = "ZPRT_NEXT_GEN";

    /// <summary>
    /// Аргументы для <c>schtasks.exe /Create</c>: запуск от имени текущего пользователя при входе с наивысшими правами.
    /// </summary>
    /// <param name="exePath">Путь к исполняемому файлу приложения.</param>
    /// <returns>Аргументы schtasks.exe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exePath"/> is <c>null</c>.</exception>
    public static string BuildCreateArguments(string exePath)
    {
        ArgumentNullException.ThrowIfNull(exePath);
        return $"/Create /TN \"{TaskName}\" /TR \"{exePath}\" /SC ONLOGON /RL HIGHEST /F";
    }

    /// <summary>Аргументы для <c>schtasks.exe /Delete</c> задачи автозапуска.</summary>
    /// <returns>Аргументы schtasks.exe.</returns>
    public static string BuildDeleteArguments()
    {
        return $"/Delete /TN \"{TaskName}\" /F";
    }

    /// <summary>Аргументы для <c>schtasks.exe /Query</c> с подробным выводом.</summary>
    internal static string BuildQueryArguments()
    {
        return $"/Query /TN \"{TaskName}\" /FO LIST /V";
    }
}

/// <summary>Управление автозапуском приложения.</summary>
public interface IAutostartService
{
    /// <summary>Проверить, что задача автозапуска зарегистрирована именно для этого исполняемого файла.</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct = default);

    /// <summary>Зарегистрировать задачу автозапуска.</summary>
    Task EnableAsync(CancellationToken ct = default);

    /// <summary>Удалить задачу автозапуска. Если задачи нет — ничего не делает.</summary>
    Task DisableAsync(CancellationToken ct = default);
}

/// <summary>Автозапуск через запланированную задачу Windows (schtasks.exe).</summary>
/// <param name="exePath">Путь к исполняемому файлу приложения.</param>
public class ScheduledTaskAutostart(string exePath) : IAutostartService
{
    private const string Executable = "schtasks.exe";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    private readonly string _exePath = exePath;

    /// <summary>Асинхронно запустить schtasks.exe и вернуть код завершения вместе с выводом.</summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить schtasks.exe.");

        // Читаем потоки сразу, иначе переполнение буфера может заблокировать процесс.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* лучшая попытка */ }
            throw new TimeoutException($"schtasks.exe не ответил за {CommandTimeout.TotalSeconds:0} секунд.");
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return (process.ExitCode, string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error);
    }

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_exePath))
            return false;

        var (exitCode, output) = await RunAsync(AutostartCommand.BuildQueryArguments(), ct).ConfigureAwait(false);
        if (exitCode != 0)
            return false;

        // Задача с тем же именем могла быть создана другой копией программы —
        // считаем автозапуск включённым только если путь совпадает.
        var normalized = output.Replace("\\\\", "\\");
        return normalized.Contains(_exePath.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task EnableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_exePath))
            throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу приложения.");

        var (exitCode, output) = await RunAsync(
            AutostartCommand.BuildCreateArguments(_exePath), ct).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Не удалось включить автозапуск задачи \"{AutostartCommand.TaskName}\" (код завершения {exitCode}). {output}".Trim());
        }
    }

    public async Task DisableAsync(CancellationToken ct = default)
    {
        // Удаляем только свою задачу: чужую задачу с тем же именем не трогаем.
        if (!await IsEnabledAsync(ct).ConfigureAwait(false))
            return;

        var (exitCode, output) = await RunAsync(
            AutostartCommand.BuildDeleteArguments(), ct).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Не удалось отключить автозапуск задачи \"{AutostartCommand.TaskName}\" (код завершения {exitCode}). {output}".Trim());
        }
    }
}
