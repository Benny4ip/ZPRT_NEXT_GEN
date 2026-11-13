namespace ZprtNextGen.Core.Startup;

using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Xml.Linq;

/// <summary>Сборщик аргументов командной строки для задачи автозапуска ZPRT_NEXT_GEN.</summary>
public static class AutostartCommand
{
    /// <summary>Имя задачи в планировщике Windows.</summary>
    public const string TaskName = "ZPRT_NEXT_GEN";

    /// <summary>Аргументы запуска задачи: сразу в трей и с автоподключением.</summary>
    public const string LaunchArguments = "--tray --autoconnect";

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

    /// <summary>Аргументы для создания задачи из XML-файла.</summary>
    public static string BuildXmlCreateArguments(string xmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);
        return $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F";
    }

    /// <summary>Аргументы для чтения XML-определения задачи.</summary>
    public static string BuildQueryXmlArguments()
    {
        return $"/Query /TN \"{TaskName}\" /XML";
    }

    /// <summary>
    /// XML-задача: вход текущего пользователя, наивысшие права, запуск в трее
    /// с автоподключением, без ограничения времени выполнения.
    /// </summary>
    public static string BuildTaskXml(string exePath, string arguments = LaunchArguments)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        var command = SecurityElement.Escape(exePath);
        var launch = SecurityElement.Escape(arguments);

        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{command}</Command>
      <Arguments>{launch}</Arguments>
    </Exec>
  </Actions>
</Task>
""";
    }

    /// <summary>
    /// Проверить, что XML-задача указывает на этот исполняемый файл и запускает
    /// его с аргументами <c>--tray --autoconnect</c>.
    /// </summary>
    public static bool MatchesTaskXml(string xml, string exePath)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(exePath))
            return false;

        try
        {
            var document = XDocument.Parse(xml);
            var command = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Command")?.Value;
            var arguments = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Arguments")?.Value;

            if (command is null || arguments is null)
                return false;

            var normalizedCommand = command.Trim().Trim('"').Replace('/', '\\');
            var normalizedExe = exePath.Trim().Trim('"').Replace('/', '\\');

            return string.Equals(normalizedCommand, normalizedExe, StringComparison.OrdinalIgnoreCase)
                && arguments.Contains("--tray", StringComparison.OrdinalIgnoreCase)
                && arguments.Contains("--autoconnect", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return false;
        }
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
        // schtasks /XML отдаёт UTF-16, остальные команды — обычно UTF-8, поэтому
        // декодируем байты адаптивно.
        var outputTask = ReadAllAsync(process.StandardOutput.BaseStream);
        var errorTask = ReadAllAsync(process.StandardError.BaseStream);

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

        var output = Decode(await outputTask.ConfigureAwait(false));
        var error = Decode(await errorTask.ConfigureAwait(false));
        return (process.ExitCode, string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        var zeroBytes = 0;
        foreach (var value in bytes)
        {
            if (value == 0)
                zeroBytes++;
        }

        if (bytes.Length > 4 && zeroBytes > bytes.Length / 3)
            return Encoding.Unicode.GetString(bytes);

        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_exePath))
            return false;

        var (exitCode, output) = await RunAsync(AutostartCommand.BuildQueryXmlArguments(), ct).ConfigureAwait(false);

        // Задача с тем же именем могла быть создана другой копией программы —
        // считаем автозапуск включённым только если путь и аргументы совпадают.
        return exitCode == 0 && AutostartCommand.MatchesTaskXml(output, _exePath);
    }

    public async Task EnableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_exePath))
            throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу приложения.");

        var tempFile = Path.Combine(
            Path.GetTempPath(), $"ZPRT_NEXT_GEN-task-{Guid.NewGuid():N}.xml");

        try
        {
            var xml = AutostartCommand.BuildTaskXml(_exePath);
            File.WriteAllText(tempFile, xml, Encoding.Unicode);

            var (exitCode, output) = await RunAsync(
                AutostartCommand.BuildXmlCreateArguments(tempFile), ct).ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Не удалось включить автозапуск задачи \"{AutostartCommand.TaskName}\" (код завершения {exitCode}). {output}".Trim());
            }
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch { /* временный файл — лучшая попытка */ }
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
