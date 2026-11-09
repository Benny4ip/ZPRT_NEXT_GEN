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

                var oldVersion = ReadOldVersion(presetsPath);

                if (engineRootExisted)
                {
                    backupRoot = engineRoot + ".backup." + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                    Directory.Move(engineRoot, backupRoot);
                }

                try
                {
                    Directory.CreateDirectory(engineRoot);
                    CopyDirectory(newEngineRoot, engineRoot);

                    progress?.Report("Импорт пресетов...");
                    var catalog = PresetImporter.Import(engineRoot, release.Tag);
                    PresetCatalogWriter.Save(catalog, presetsPath);

                    if (backupRoot is not null)
                        Directory.Delete(backupRoot, recursive: true);

                    progress?.Report("Готово.");
                    return new EngineUpdateResult(true, oldVersion, release.Tag, catalog.Presets.Count, null);
                }
                catch
                {
                    // Откат: вернуть старый движок на место.
                    if (backupRoot is not null && Directory.Exists(backupRoot))
                    {
                        if (Directory.Exists(engineRoot))
                            Directory.Delete(engineRoot, recursive: true);
                        Directory.Move(backupRoot, engineRoot);
                        backupRoot = null;
                    }
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
            File.Copy(file, Path.Combine(target, rel), overwrite: true);
        }
    }
}
