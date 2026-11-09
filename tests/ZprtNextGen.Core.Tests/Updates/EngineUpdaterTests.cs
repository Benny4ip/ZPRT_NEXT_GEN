using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class EngineUpdaterTests
{
    private sealed class FakeDownloader : IFileDownloader
    {
        public readonly byte[] ZipBytes;
        public string? LastUrl;
        public string? LastDest;
        public readonly List<double> Progress = new();

        public FakeDownloader(byte[] zipBytes) => ZipBytes = zipBytes;

        public Task DownloadAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken ct)
        {
            LastUrl = url.ToString();
            LastDest = destinationPath;
            File.WriteAllBytes(destinationPath, ZipBytes);
            progress?.Report(100);
            return Task.CompletedTask;
        }
    }

    private static string MakeBat(string id, string args) =>
        $"start \"{id}\" /min \"winws.exe\" {args}{Environment.NewLine}";

    private static byte[] MakeZip(Action<ZipArchive> add)
    {
        var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            add(archive);
        return File.ReadAllBytes(zip);
    }

    private static DirectoryInfo OldEngine(string dir)
    {
        var root = Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(root.FullName, "bin"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "lists"));
        File.WriteAllText(Path.Combine(root.FullName, "bin", "winws.exe"), "old-winws");
        File.WriteAllText(Path.Combine(root.FullName, "bin", "WinDivert.dll"), "dll");
        File.WriteAllText(Path.Combine(root.FullName, "bin", "WinDivert64.sys"), "sys");
        File.WriteAllText(Path.Combine(root.FullName, "lists", "list-general.txt"), "list");
        File.WriteAllText(Path.Combine(root.FullName, "general.bat"), MakeBat("general", "--wf-tcp=80"));
        return root;
    }

    [Fact]
    public async Task UpdateAsync_ReplacesEngine_ImportsPresets_BacksUpOld()
    {
        var work = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var engineRoot = OldEngine(Path.Combine(work, "engine"));
        var presetsPath = Path.Combine(work, "presets", "presets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(presetsPath)!);
        File.WriteAllText(presetsPath, """
            { "version": 1, "engine": "Flowseal/zapret-discord-youtube", "engineVersion": "1.0.0", "groups": [], "presets": [] }
            """);

        var newZip = MakeZip(archive =>
        {
            foreach (var (name, content) in new[]
                     {
                         ("bin/winws.exe", "new-winws"),
                         ("bin/WinDivert.dll", "dll"),
                         ("bin/WinDivert64.sys", "sys"),
                         ("lists/list-general.txt", "list"),
                         ("general-alt5.bat", MakeBat("general-alt5", "--wf-tcp=443")),
                     })
            {
                var entry = archive.CreateEntry(name);
                using var s = entry.Open();
                using var w = new StreamWriter(s);
                w.Write(content);
            }
        });

        var downloader = new FakeDownloader(newZip);
        bool stopped = false;
        var release = new ReleaseInfo("1.10.4", "1.10.4", null, new[]
        {
            new ReleaseAsset("zapret-discord-youtube-1.10.4.zip", "https://x/z.zip", newZip.Length)
        });

        var sut = new EngineUpdater(downloader);
        var result = await sut.UpdateAsync(release, engineRoot.FullName, presetsPath,
            () => { stopped = true; return Task.CompletedTask; },
            null, CancellationToken.None);

        Assert.True(result.Updated);
        Assert.Equal("1.0.0", result.OldVersion);
        Assert.Equal("1.10.4", result.NewVersion);
        Assert.True(stopped);
        Assert.True(File.Exists(Path.Combine(engineRoot.FullName, "bin", "winws.exe")));
        var newExe = File.ReadAllText(Path.Combine(engineRoot.FullName, "bin", "winws.exe"));
        Assert.Equal("new-winws", newExe);
        Assert.False(Directory.GetDirectories(engineRoot.Parent!.FullName, "engine.backup.*").Any(),
            "после успешного обновления бэкап удалён");

        var presets = JsonDocument.Parse(File.ReadAllText(presetsPath)).RootElement;
        var presetIds = presets.GetProperty("presets").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Contains("general-alt5", presetIds);
        Assert.Equal("1.10.4", presets.GetProperty("engineVersion").GetString());
    }

    [Fact]
    public async Task UpdateAsync_BadZip_LeavesEngineIntactAndThrows()
    {
        var work = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var engineRoot = OldEngine(Path.Combine(work, "engine"));
        var presetsPath = Path.Combine(work, "presets", "presets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(presetsPath)!);
        File.WriteAllText(presetsPath, "{ }");

        var badZip = MakeZip(archive =>
        {
            var entry = archive.CreateEntry("readme.txt");
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write("no winws here");
        });

        var downloader = new FakeDownloader(badZip);
        var release = new ReleaseInfo("9.9.9", "9.9.9", null, new[]
        {
            new ReleaseAsset("a.zip", "https://x/a.zip", badZip.Length)
        });

        var sut = new EngineUpdater(downloader);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            sut.UpdateAsync(release, engineRoot.FullName, presetsPath,
                () => Task.CompletedTask, null, CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(engineRoot.FullName, "bin", "winws.exe")));
        Assert.Equal("old-winws", File.ReadAllText(Path.Combine(engineRoot.FullName, "bin", "winws.exe")));
        Assert.False(Directory.GetDirectories(engineRoot.Parent!.FullName, "engine.backup.*").Any());
    }
}
