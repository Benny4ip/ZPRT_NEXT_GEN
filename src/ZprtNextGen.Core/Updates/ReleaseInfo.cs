using System.Linq;

namespace ZprtNextGen.Core.Updates;

/// <summary>Один файл релиза на GitHub.</summary>
public record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>Релиз GitHub: тег, название, описание и список файлов.</summary>
public record ReleaseInfo(string Tag, string Name, string? Body, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>Первый ассет с расширением <c>.zip</c>, или <c>null</c>.</summary>
    public ReleaseAsset? FindZip() =>
        Assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Найти подходящий ассет программы: для portable ищется portable-артефакт, для standard — standard.
    /// </summary>
    public ReleaseAsset? FindAppAsset(bool isPortable)
    {
        if (isPortable)
        {
            return Assets.FirstOrDefault(a => a.Name.Contains("portable", StringComparison.OrdinalIgnoreCase)
                                           && (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                            || a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                ?? Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? FindZip();
        }

        return Assets.FirstOrDefault(a => a.Name.Contains("standard", StringComparison.OrdinalIgnoreCase)
                                       && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? FindZip();
    }
}
