using System.IO.Compression;
using System.Text.Json;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Updates;

/// <summary>Результат обновления движка.</summary>
public record EngineUpdateResult(bool Updated, string? OldVersion, string? NewVersion, int PresetCount, string? Message);

/// <summary>Обновление движка winws из релиза GitHub с бэкапом и откатом.</summary>
public interface IEngineUpdater
{
    /// <summary>Скачать .zip релиза, проверить, остановить движок, заменить папку и переимпортировать пресеты.</summary>
    Task<EngineUpdateResult> UpdateAsync(ReleaseInfo release, string engineRoot, string presetsPath,
        Func<Task> stopEngine, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>Реализация <see cref="IEngineUpdater"/>.</summary>
public sealed class EngineUpdater : IEngineUpdater
{
    private readonly IFileDownloader _downloader;

    public EngineUpdater(IFileDownloader downloader)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    }

    public async Task<EngineUpdateResult> UpdateAsync(ReleaseInfo release, string engineRoot, string presetsPath,
        Func<Task> stopEngine, IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetsPath);
        ArgumentNullException.ThrowIfNull(stopEngine);

        // GetFullPath сохраняет завершающий разделитель; без нормализации бэкап создался бы внутри папки.
        var normalizedEngineRoot = engineRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureNotApplicationDirectory(normalizedEngineRoot);

        var zipAsset = release.FindZip()
            ?? throw new InvalidDataException("В релизе движка не найден .zip-ассет для скачивания.");

        // Если прошлое обновление оборвалось после переноса движка в бэкап — возвращаем его.
        TryRestoreOrphanedBackup(normalizedEngineRoot);

        var engineRootExisted = Directory.Exists(normalizedEngineRoot);
        var tempZip = Path.GetTempFileName();
        string? backupRoot = null;

        try
        {
            progress?.Report("Скачивание движка...");
            await _downloader.DownloadAsync(new Uri(zipAsset.DownloadUrl), tempZip, null, ct);

            var tempExtract = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(tempZip, tempExtract);

                var winws = Directory.GetFiles(tempExtract, "winws.exe", SearchOption.AllDirectories)
                    .FirstOrDefault()
                    ?? throw new InvalidDataException("В архиве движка не найден bin\\winws.exe.");
                var newEngineRoot = Path.GetDirectoryName(Path.GetDirectoryName(winws));
                if (string.IsNullOrEmpty(newEngineRoot))
                    throw new InvalidDataException("Невозможно определить корень движка в архиве.");

                var missing = EngineLocator.RequiredFiles
                    .Where(file => !File.Exists(Path.Combine(newEngineRoot, file))).ToList();
                if (missing.Count > 0)
                    throw new InvalidDataException(
                        "Архив движка неполный. Не найдены: " + string.Join(", ", missing));

                progress?.Report("Остановка движка...");
                await stopEngine();
                // Даём WinDivert/драйверу освободить файлы после завершения процессов.
                await Task.Delay(800, ct);

                var oldVersion = ReadOldVersion(presetsPath);

                if (engineRootExisted)
                {
                    backupRoot = normalizedEngineRoot + ".backup." + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                    PrepareDirectoryForMove(normalizedEngineRoot);
                    MoveDirectoryWithRetry(normalizedEngineRoot, backupRoot);
                }

                try
                {
                    Directory.CreateDirectory(normalizedEngineRoot);
                    CopyDirectory(newEngineRoot, normalizedEngineRoot);

                    progress?.Report("Импорт пресетов...");
                    var catalog = PresetImporter.Import(normalizedEngineRoot, release.Tag);
                    PresetCatalogWriter.Save(catalog, presetsPath);

                    if (backupRoot is not null)
                        TryDeleteDirectory(backupRoot);

                    progress?.Report("Готово.");
                    return new EngineUpdateResult(true, oldVersion, release.Tag, catalog.Presets.Count, null);
                }
                catch (Exception ex)
                {
                    // Откат: вернуть старый движок на место. Не копируем бэкап поверх частичного,
                    // чтобы не получить гибрид из старых и новых файлов.
                    var restored = RestoreBackup(normalizedEngineRoot, ref backupRoot);
                    if (!restored && !engineRootExisted)
                    {
                        // Движка не было — убираем частичную папку, чтобы не оставлять битый движок.
                        TryDeleteDirectory(normalizedEngineRoot);
                    }

                    throw new IOException(
                        $"Не удалось обновить движок: {ex.Message}" +
                        (restored ? "" : $". Бэкап сохранён: {backupRoot ?? "нет"}"), ex);
                }
            }
            finally
            {
                if (Directory.Exists(tempExtract))
                    TryDeleteDirectory(tempExtract);
            }
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    private static void EnsureNotApplicationDirectory(string engineRoot)
    {
        var normalizedBase = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(engineRoot, normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Нельзя обновлять движок в папке приложения: это приведёт к перемещению/удалению самой программы.");
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var processDir = Path.GetFullPath(Path.GetDirectoryName(processPath) ?? string.Empty);
            if (string.Equals(engineRoot, processDir, StringComparison.OrdinalIgnoreCase) ||
                processDir.StartsWith(engineRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Нельзя обновлять движок в папке, где находится запущенное приложение.");
            }
        }
    }

    private static void TryRestoreOrphanedBackup(string engineRoot)
    {
        if (Directory.Exists(engineRoot))
            return;

        try
        {
            var parent = Path.GetDirectoryName(engineRoot);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return;

            var prefix = Path.GetFileName(engineRoot) + ".backup.";
            var latest = Directory.GetDirectories(parent, prefix + "*")
                .OrderByDescending(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
            if (latest is not null)
                Directory.Move(latest, engineRoot);
        }
        catch
        {
            // Лучшая попытка: не мешаем обновлению из-за старого бэкапа.
        }
    }

    private static string? ReadOldVersion(string presetsPath)
    {
        try
        {
            if (!File.Exists(presetsPath))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(presetsPath));
            if (doc.RootElement.TryGetProperty("engineVersion", out var version))
                return version.GetString();
        }
        catch
        {
            // Повреждённый старый каталог — версию определить не удалось, продолжим без неё.
        }
        return null;
    }

    private static void PrepareDirectoryForMove(string path)
    {
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
        }
        catch { /* лучшая попытка */ }
    }

    private static void MoveDirectoryWithRetry(string source, string target)
    {
        Exception? last = null;
        for (var i = 0; i < 4; i++)
        {
            try
            {
                Directory.Move(source, target);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                Thread.Sleep(500 * (i + 1));
                PrepareDirectoryForMove(source);
            }
        }
        throw last ?? new IOException($"Не удалось переместить '{source}'.");
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;
        try
        {
            PrepareDirectoryForMove(path);
            Directory.Delete(path, recursive: true);
        }
        catch { /* лучшая попытка */ }
    }

    private static bool RestoreBackup(string engineRoot, ref string? backupRoot)
    {
        if (string.IsNullOrEmpty(backupRoot) || !Directory.Exists(backupRoot))
            return false;

        try
        {
            // Убираем повреждённую/частичную папку движка, чтобы освободить целевой путь.
            if (Directory.Exists(engineRoot))
            {
                var failed = engineRoot + ".failed." + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                try
                {
                    Directory.Move(engineRoot, failed);
                    TryDeleteDirectory(failed);
                }
                catch
                {
                    TryDeleteDirectory(engineRoot);
                }
            }

            if (Directory.Exists(engineRoot))
                return false; // путь занят — не копируем поверх, оставляем бэкап для ручного восстановления

            Directory.Move(backupRoot, engineRoot);
            backupRoot = null;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(target, rel));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            try { if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal); } catch { }
            File.Copy(file, dest, overwrite: true);
        }
    }
}
