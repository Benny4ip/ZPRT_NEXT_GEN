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

        var zipAsset = release.FindZip()
            ?? throw new InvalidDataException("В релизе движка не найден .zip-ассет для скачивания.");

        var engineRootExisted = Directory.Exists(engineRoot);
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
                    backupRoot = engineRoot + ".backup." + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                    PrepareDirectoryForMove(engineRoot);
                    MoveDirectoryWithRetry(engineRoot, backupRoot);
                }

                try
                {
                    Directory.CreateDirectory(engineRoot);
                    CopyDirectory(newEngineRoot, engineRoot);

                    progress?.Report("Импорт пресетов...");
                    var catalog = PresetImporter.Import(engineRoot, release.Tag);
                    PresetCatalogWriter.Save(catalog, presetsPath);

                    if (backupRoot is not null)
                        TryDeleteDirectory(backupRoot);

                    progress?.Report("Готово.");
                    return new EngineUpdateResult(true, oldVersion, release.Tag, catalog.Presets.Count, null);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // Откат: вернуть старый движок на место.
                    RestoreBackup(engineRoot, ref backupRoot);
                    throw new IOException(
                        "Не удалось обновить файлы движка (возможно, WinDivert/драйвер ещё используется). " +
                        "Закройте другие экземпляры запрета/winws и попробуйте снова. " + ex.Message, ex);
                }
                catch
                {
                    // Откат: вернуть старый движок на место.
                    RestoreBackup(engineRoot, ref backupRoot);
                    throw;
                }
            }
            finally
            {
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, recursive: true);
            }
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
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

    private static void RestoreBackup(string engineRoot, ref string? backupRoot)
    {
        if (string.IsNullOrEmpty(backupRoot) || !Directory.Exists(backupRoot))
            return;

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
            {
                // Не удалось освободить путь — копируем содержимое бэкапа поверх.
                CopyDirectory(backupRoot, engineRoot);
                TryDeleteDirectory(backupRoot);
                backupRoot = null;
                return;
            }

            Directory.Move(backupRoot, engineRoot);
            backupRoot = null;
        }
        catch
        {
            // Лучшая попытка: бэкап оставляем на диске (backupRoot не обнуляем здесь намеренно? нет — оставим для диагностики).
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
