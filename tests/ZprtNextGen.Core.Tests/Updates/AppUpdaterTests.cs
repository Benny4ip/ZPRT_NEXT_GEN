using System.IO.Compression;
using System.Text;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class AppUpdaterTests
{
    private sealed class FakeDownloader : IFileDownloader
    {
        private readonly Dictionary<string, byte[]> _byUrl;

        public FakeDownloader(Dictionary<string, byte[]> byUrl) => _byUrl = byUrl;

        public Task DownloadAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken ct)
        {
            if (!_byUrl.TryGetValue(url.ToString(), out var bytes))
                throw new InvalidOperationException("Unexpected URL: " + url);
            File.WriteAllBytes(destinationPath, bytes);
            progress?.Report(100);
            return Task.CompletedTask;
        }
    }

    private static byte[] MakeZip(string entryName, string content)
    {
        var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(entryName);
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write(content);
        }
        return File.ReadAllBytes(zip);
    }

    [Fact]
    public void CreateScript_WaitsForPid_ThenReplacesAndRestarts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var script = AppUpdater.CreateScript(1234,
            @"C:\New\ZprtNextGen.exe", @"C:\App\ZprtNextGen.exe", tempDir);

        Assert.True(File.Exists(script));
        Assert.EndsWith(".cmd", script);
        var text = File.ReadAllText(script);
        Assert.Contains("PID eq 1234", text);
        Assert.Contains(@"C:\New\ZprtNextGen.exe", text);
        Assert.Contains(@"C:\App\ZprtNextGen.exe", text);
        Assert.Contains("start \"\"", text);
        Assert.Contains("rd /s /q", text);
        Assert.Contains("del %0", text);
    }

    [Fact]
    public async Task PrepareAsync_ExtractsZprtNextGenExe_ReturnsPlan()
    {
        var work = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(work, "app");
        Directory.CreateDirectory(appDir);

        var zip = MakeZip("ZprtNextGen.exe", "fake-exe-bytes");
        var zipUrl = "https://x/a.zip";
        var sumsUrl = "https://x/SHA256SUMS.txt";
        var sums = Sha256Verifier.FormatSums([("ZPRT_NEXT_GEN-0.2.0-win-x64.zip", Sha256Verifier.HashBytes(zip))]);
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [zipUrl] = zip,
            [sumsUrl] = Encoding.UTF8.GetBytes(sums),
        });
        var release = new ReleaseInfo("0.2.0", "0.2.0", null,
        [
            new ReleaseAsset("ZPRT_NEXT_GEN-0.2.0-win-x64.zip", zipUrl, zip.Length),
            new ReleaseAsset("SHA256SUMS.txt", sumsUrl, sums.Length),
        ]);

        var sut = new AppUpdater(downloader);
        var plan = await sut.PrepareAsync(release, appDir, null, CancellationToken.None);

        Assert.Equal(Path.Combine(appDir, "ZprtNextGen.exe"), plan.TargetExecutablePath);
        Assert.True(File.Exists(plan.NewExecutablePath));
        Assert.Equal("fake-exe-bytes", File.ReadAllText(plan.NewExecutablePath));
        Assert.True(File.Exists(plan.ScriptPath));
        Assert.StartsWith(Path.GetTempPath(), plan.ScriptPath);
    }

    [Fact]
    public async Task PrepareAsync_MissingChecksum_Throws()
    {
        var appDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDir);
        var zip = MakeZip("ZprtNextGen.exe", "x");
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://x/a.zip"] = zip,
        });
        var release = new ReleaseInfo("0.2.0", "0.2.0", null,
        [
            new ReleaseAsset("a.zip", "https://x/a.zip", zip.Length),
        ]);

        var sut = new AppUpdater(downloader);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => sut.PrepareAsync(release, appDir, null, CancellationToken.None));
    }

    [Fact]
    public async Task PrepareAsync_DigestMismatch_Throws()
    {
        var appDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDir);
        var zip = MakeZip("ZprtNextGen.exe", "x");
        var downloader = new FakeDownloader(new Dictionary<string, byte[]>
        {
            ["https://x/a.zip"] = zip,
        });
        var release = new ReleaseInfo("0.2.0", "0.2.0", null,
        [
            new ReleaseAsset("a.zip", "https://x/a.zip", zip.Length, "sha256:00"),
        ]);

        var sut = new AppUpdater(downloader);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => sut.PrepareAsync(release, appDir, null, CancellationToken.None));
        Assert.Contains("SHA256 не совпал", ex.Message);
    }
}
