using System.Linq;

namespace ZprtNextGen.Core.Updates;

/// <summary>Один файл релиза на GitHub.</summary>
/// <param name="Digest">Опциональный digest API GitHub, например <c>sha256:abc...</c>.</param>
public record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Digest = null);

/// <summary>Релиз GitHub: тег, название, описание и список файлов.</summary>
public record ReleaseInfo(string Tag, string Name, string? Body, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>Первый ассет с расширением <c>.zip</c>, или <c>null</c>.</summary>
    public ReleaseAsset? FindZip() =>
        Assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>Ассет контрольных сумм (<c>SHA256SUMS.txt</c> / <c>*.sha256</c>), или <c>null</c>.</summary>
    public ReleaseAsset? FindChecksumAsset() =>
        Assets.FirstOrDefault(a =>
            a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)
            || a.Name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase)
            || a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));

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
