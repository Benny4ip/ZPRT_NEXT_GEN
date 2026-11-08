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
}

/// <summary>Управление автозапуском приложения.</summary>
public interface IAutostartService
{
    /// <summary>Признак того, что задача автозапуска зарегистрирована.</summary>
    bool IsEnabled();

    /// <summary>Зарегистрировать задачу автозапуска.</summary>
    void Enable();

    /// <summary>Удалить задачу автозапуска.</summary>
    void Disable();
}

/// <summary>Автозапуск через запланированную задачу Windows (schtasks.exe).</summary>
/// <param name="exePath">Путь к исполняемому файлу приложения.</param>
public class ScheduledTaskAutostart(string exePath) : IAutostartService
{
    private const string Executable = "schtasks.exe";
    private readonly string _exePath = exePath;

    /// <summary>Запустить schtasks.exe и вернуть код завершения.</summary>
    private static int Run(string arguments)
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
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    public bool IsEnabled()
    {
        var exitCode = Run($"/Query /TN \"{AutostartCommand.TaskName}\" /FO CSV");
        return exitCode == 0;
    }

    public void Enable()
    {
        var exitCode = Run(AutostartCommand.BuildCreateArguments(_exePath));
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Не удалось включить автозапуск задачи \"{AutostartCommand.TaskName}\" (код завершения {exitCode}).");
    }

    public void Disable()
    {
        var exitCode = Run(AutostartCommand.BuildDeleteArguments());
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Не удалось отключить автозапуск задачи \"{AutostartCommand.TaskName}\" (код завершения {exitCode}).");
    }
}
