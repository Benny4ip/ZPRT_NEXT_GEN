using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый клиент GitHub Releases для тестов ViewModel.</summary>
public sealed class FakeGitHubReleaseClient : IGitHubReleaseClient
{
    public readonly Dictionary<string, ReleaseInfo?> Releases = new(StringComparer.Ordinal);
    public readonly List<string> RequestedRepos = new();

    public Task<ReleaseInfo?> GetLatestAsync(string repository, CancellationToken ct)
    {
        RequestedRepos.Add(repository);
        Releases.TryGetValue(repository, out var release);
        return Task.FromResult(release);
    }
}
