using System.Text.Json;

namespace ZprtNextGen.Core.Updates;

/// <summary>Клиент GitHub Releases API.</summary>
public interface IGitHubReleaseClient
{
    /// <summary>Последний релиз репозитория, или <c>null</c>, если GitHub недоступен.</summary>
    Task<ReleaseInfo?> GetLatestAsync(string repository, CancellationToken ct);
}

/// <summary>Реализация <see cref="IGitHubReleaseClient"/> через <c>api.github.com</c>.</summary>
public sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    private readonly HttpClient _client;

    public GitHubReleaseClient(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Клиент с таймаутом 30 секунд и пользовательским агентом (требуется GitHub API).</summary>
    public static IGitHubReleaseClient CreateDefault()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZPRT-NEXT-GEN");
        return new GitHubReleaseClient(client);
    }

    public async Task<ReleaseInfo?> GetLatestAsync(string repository, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var url = $"https://api.github.com/repos/{repository}/releases/latest";
        using var response = await _client.GetAsync(url, ct);

        // GitHub может быть недоступен или вернуть 404/rate-limit — не падаем, возвращаем null.
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        string name = root.GetProperty("name").GetString() ?? string.Empty;
        string? body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in assetsEl.EnumerateArray())
            {
                assets.Add(new ReleaseAsset(
                    item.GetProperty("name").GetString() ?? string.Empty,
                    item.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    item.GetProperty("size").GetInt64()));
            }
        }

        return new ReleaseInfo(tag, name, body, assets);
    }
}
