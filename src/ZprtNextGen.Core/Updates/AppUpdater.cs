using System.Diagnostics;
using System.IO.Compression;

namespace ZprtNextGen.Core.Updates;

/// <summary>План обновления самой программы: скрипт-перезапуск + извлечённый новый файл.</summary>
public record AppUpdatePlan(string ScriptPath, string NewExecutablePath, string TargetExecutablePath);

/// <summary>Обновление программы: скачать zip или raw exe, подготовить скрипт перезапуска.</summary>
public interface IAppUpdater
{
    /// <summary>Скачать релиз, извлечь/подготовить <c>ZprtNextGen.exe</c> и составить скрипт, который после выхода
    /// текущего процесса заменит исполняемый файл и запустит новую версию.</summary>
    Task<AppUpdatePlan> PrepareAsync(ReleaseInfo release, string appDirectory,
        IProgress<string>? progress, CancellationToken ct, bool isPortable = false);

    /// <summary>Запустить скрипт обновления (обязательно после завершения текущего процесса).</summary>
    void Launch(AppUpdatePlan plan);
}

/// <summary>Реализация <see cref="IAppUpdater"/>.</summary>
public sealed class AppUpdater : IAppUpdater
{
    private readonly IFileDownloader _downloader;

    public AppUpdater(IFileDownloader downloader)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    }

    /// <summary>Сформировать .cmd-скрипт: дождаться завершения процесса <c>pid</c>, заменить
    /// <c>targetExe</c> на <c>newExe</c>, запустить программу и удалить временные файлы.</summary>
    public static string CreateScript(int pid, string newExe, string targetExe, string tempDirectory)
    {
        ArgumentNullException.ThrowIfNull(newExe);
        ArgumentNullException.ThrowIfNull(targetExe);
        ArgumentNullException.ThrowIfNull(tempDirectory);

        Directory.CreateDirectory(tempDirectory);
        var script = Path.Combine(tempDirectory, $"ZPRT_Update_{pid}.cmd");

        var lines = new[]
        {
            "@echo off",
            "setlocal",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" | findstr \"{pid}\" >nul",
            $"if not errorlevel 1 goto wait",
            $"move /Y \"{newExe}\" \"{targetExe}\"",
            "if errorlevel 1 goto :error",
            $"start \"\" \"{targetExe}\"",
            $"rd /s /q \"{tempDirectory}\"",
            "del %0",
            "exit /b 0",
            ":error",
            $"echo Не удалось заменить {targetExe}. Обновление не применено, временные файлы сохранены.",
            "exit /b 1",
        };
        File.WriteAllText(script, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return script;
    }

    public void Launch(AppUpdatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!File.Exists(plan.ScriptPath))
            throw new FileNotFoundException("Скрипт обновления не найден.", plan.ScriptPath);

        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(plan.ScriptPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Не удалось запустить обновление программы: {ex.Message}", ex);
        }
    }

    public async Task<AppUpdatePlan> PrepareAsync(ReleaseInfo release, string appDirectory,
        IProgress<string>? progress, CancellationToken ct, bool isPortable = false)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);

        var asset = release.FindAppAsset(isPortable)
            ?? throw new InvalidDataException("В релизе программы не найден подходящий файл для скачивания.");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "Zprt_AppUpdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string newExe;
            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("Скачивание portable-версии программы...");
                newExe = Path.Combine(tempDirectory, "ZprtNextGen.exe");
                await _downloader.DownloadAsync(new Uri(asset.DownloadUrl), newExe, null, ct).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("Скачивание программы...");
                var tempZip = Path.Combine(tempDirectory, "update.zip");
                await _downloader.DownloadAsync(new Uri(asset.DownloadUrl), tempZip, null, ct).ConfigureAwait(false);

                ZipFile.ExtractToDirectory(tempZip, tempDirectory);
                File.Delete(tempZip);

                newExe = Directory.GetFiles(tempDirectory, "ZprtNextGen.exe", SearchOption.AllDirectories)
                    .FirstOrDefault()
                    ?? throw new InvalidDataException("В архиве программы не найден ZprtNextGen.exe.");
            }

            var targetExe = Path.Combine(appDirectory, "ZprtNextGen.exe");

            var script = CreateScript(Environment.ProcessId, newExe, targetExe, tempDirectory);

            progress?.Report("Готово к перезапуску.");
            return new AppUpdatePlan(script, newExe, targetExe);
        }
        catch
        {
            try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true); } catch { }
            throw;
        }
    }
}

