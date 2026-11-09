using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class HttpFileDownloaderTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public FakeHttpMessageHandler(byte[] body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body)
                {
                    Headers = { ContentLength = _body.Length }
                }
            });
    }

    [Fact]
    public async Task DownloadAsync_WritesFileAndReportsProgress()
    {
        var body = Encoding.UTF8.GetBytes("hello-download");
        var client = new HttpClient(new FakeHttpMessageHandler(body));
        var sut = new HttpFileDownloader(client);

        var dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "file.bin");
        var reports = new List<double>();
        await sut.DownloadAsync(new Uri("https://example.com/file.bin"), dest,
            new Progress<double>(p => reports.Add(p)), CancellationToken.None);

        Assert.True(File.Exists(dest));
        Assert.Equal("hello-download", File.ReadAllText(dest));
        Assert.InRange(reports.Last(), 99.0, 100.0);
        File.Delete(dest);
    }
}
