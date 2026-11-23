namespace ZprtNextGen.Core.Updates;

/// <summary>Разрешение ожидаемого SHA256 для ассета релиза.</summary>
public static class ReleaseIntegrity
{
    /// <summary>
    /// Скачать <c>SHA256SUMS</c> при наличии, иначе взять <see cref="ReleaseAsset.Digest"/>.
    /// Без источника целостности — <see cref="InvalidDataException"/>.
    /// </summary>
    public static async Task<string> ResolveExpectedSha256Async(
        ReleaseInfo release,
        ReleaseAsset asset,
        IFileDownloader downloader,
        string tempDirectory,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempDirectory);

        var sumsAsset = release.FindChecksumAsset();
        if (sumsAsset is not null)
        {
            progress?.Report("Скачивание контрольных сумм...");
            var sumsPath = Path.Combine(tempDirectory, "SHA256SUMS.txt");
            await downloader.DownloadAsync(new Uri(sumsAsset.DownloadUrl), sumsPath, null, ct).ConfigureAwait(false);
            var sums = Sha256Verifier.ParseSums(await File.ReadAllTextAsync(sumsPath, ct).ConfigureAwait(false));
            var expected = Sha256Verifier.FindExpected(sums, asset.Name)
                ?? throw new InvalidDataException(
                    $"В {sumsAsset.Name} нет записи для {asset.Name}.");
            return expected;
        }

        if (!string.IsNullOrWhiteSpace(asset.Digest))
            return Sha256Verifier.NormalizeDigest(asset.Digest);

        throw new InvalidDataException(
            $"В релизе нет SHA256SUMS и digest для {asset.Name} — обновление отклонено.");
    }
}
