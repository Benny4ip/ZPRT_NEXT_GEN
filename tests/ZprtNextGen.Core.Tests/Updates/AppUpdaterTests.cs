using System.IO.Compression;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class AppUpdaterTests
{
    private sealed class FakeDownloader : IFileDownloader
    {
        public readonly byte[] ZipBytes;
        public FakeDownloader(byte[] zipBytes) => ZipBytes = zipBytes;
        public Task DownloadAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken ct)
        {
            File.WriteAllBytes(destinationPath, ZipBytes);
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
        var downloader = new FakeDownloader(zip);
        var release = new ReleaseInfo("0.2.0", "0.2.0", null, new[]
        {
            new ReleaseAsset("ZPRT_NEXT_GEN-0.2.0-win-x64.zip", "https://x/a.zip", zip.Length)
        });

        var sut = new AppUpdater(downloader);
        var plan = await sut.PrepareAsync(release, appDir, null, CancellationToken.None);

        Assert.Equal(Path.Combine(appDir, "ZprtNextGen.exe"), plan.TargetExecutablePath);
        Assert.True(File.Exists(plan.NewExecutablePath));
        Assert.Equal("fake-exe-bytes", File.ReadAllText(plan.NewExecutablePath));
        Assert.True(File.Exists(plan.ScriptPath));
        Assert.True(plan.ScriptPath.StartsWith(Path.GetTempPath()));
    }
}
