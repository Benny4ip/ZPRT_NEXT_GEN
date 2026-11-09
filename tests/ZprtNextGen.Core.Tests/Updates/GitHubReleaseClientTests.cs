using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class GitHubReleaseClientTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request));
    }

    [Fact]
    public async Task GetLatestAsync_ParsesReleaseAndFindsZip()
    {
        var json = """
            {
              "tag_name": "1.10.4",
              "name": "1.10.4",
              "body": "release notes",
              "assets": [
                { "name": "a.zip", "browser_download_url": "https://x/a.zip", "size": 123 },
                { "name": "b.tar.gz", "browser_download_url": "https://x/b", "size": 456 }
              ]
            }
            """;
        var client = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var sut = new GitHubReleaseClient(client);

        var info = await sut.GetLatestAsync("owner/repo", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("1.10.4", info!.Tag);
        Assert.Equal("1.10.4", info.Name);
        Assert.Equal("release notes", info.Body);
        var zip = info.FindZip();
        Assert.NotNull(zip);
        Assert.Equal("a.zip", zip.Name);
        Assert.Equal("https://x/a.zip", zip.DownloadUrl);
        Assert.Equal(123, zip.Size);
    }

    [Fact]
    public async Task GetLatestAsync_NotFound_ReturnsNull()
    {
        var client = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var sut = new GitHubReleaseClient(client);

        var info = await sut.GetLatestAsync("owner/repo", CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task GetLatestAsync_NoZipAsset_FindZipReturnsNull()
    {
        var json = """
            {
              "tag_name": "1.10.4",
              "name": "1.10.4",
              "assets": [
                { "name": "a.tar.gz", "browser_download_url": "https://x/a", "size": 1 }
              ]
            }
            """;
        var client = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));
        var sut = new GitHubReleaseClient(client);

        var info = await sut.GetLatestAsync("owner/repo", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Null(info!.FindZip());
    }
}
