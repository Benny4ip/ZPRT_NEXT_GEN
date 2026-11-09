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
}
