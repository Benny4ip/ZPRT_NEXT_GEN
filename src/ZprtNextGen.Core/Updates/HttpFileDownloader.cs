namespace ZprtNextGen.Core.Updates;

/// <summary>Скачивание файла с отчётом прогресса в процентах.</summary>
public interface IFileDownloader
{
    Task DownloadAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken ct);
}

/// <summary>Реализация <see cref="IFileDownloader"/> через <see cref="HttpClient"/>.</summary>
public sealed class HttpFileDownloader : IFileDownloader
{
    private readonly HttpClient _client;

    public HttpFileDownloader(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public static IFileDownloader CreateDefault() =>
        new HttpFileDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });

    public async Task DownloadAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await responseStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, ct);
            totalRead += read;

            if (totalBytes.HasValue && totalBytes.Value > 0)
                progress?.Report((double)totalRead / totalBytes.Value * 100.0);
        }
    }
}
