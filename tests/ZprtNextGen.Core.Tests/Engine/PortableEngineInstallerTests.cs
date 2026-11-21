using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Engine;

namespace ZprtNextGen.Core.Tests.Engine;

public class PortableEngineInstallerTests
{
    private static string NewRoot() => Path.Combine(
        Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"));

    private static byte[] CreateZip(params string[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Replace('\\', '/'));
                using var writer = new StreamWriter(zipEntry.Open());
                writer.Write("stub");
            }
        }

        return memory.ToArray();
    }

    private static byte[] ValidZip() => CreateZip(
        @"bin\winws.exe",
        @"bin\WinDivert.dll",
        @"bin\WinDivert64.sys",
        @"lists\list-general.txt",
        @"presets\presets.json");

    [Fact]
    public async Task InstallAsync_NoEmbeddedEngine_ReturnsNull()
    {
        var installer = new PortableEngineInstaller(null, "1.0.0", NewRoot());

        Assert.False(installer.IsAvailable);
        Assert.Null(await installer.InstallAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_FirstRun_ExtractsAndWritesReady()
    {
        var cacheRoot = NewRoot();
        var installer = new PortableEngineInstaller(ValidZip(), "1.0.0-beta.1", cacheRoot);

        var result = await installer.InstallAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Installed);
        Assert.True(File.Exists(Path.Combine(result.EngineRoot, "bin", "winws.exe")));
        Assert.True(File.Exists(Path.Combine(result.EngineRoot, "lists", "list-general.txt")));
        Assert.True(File.Exists(Path.Combine(result.EngineRoot, "presets", "presets.json")));
        Assert.True(File.Exists(Path.Combine(result.EngineRoot, ".ready")));
        Assert.Equal(Path.Combine(result.EngineRoot, "presets", "presets.json"), result.PresetsPath);
        Assert.StartsWith(Path.GetFullPath(cacheRoot), Path.GetFullPath(result.EngineRoot), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.0.0-beta.1", result.EngineRoot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_SecondRun_ReusesReadyFolder()
    {
        var cacheRoot = NewRoot();
        var installer = new PortableEngineInstaller(ValidZip(), "1.0.0", cacheRoot);
        var first = await installer.InstallAsync(CancellationToken.None);

        var marker = Path.Combine(first!.EngineRoot, "user-marker.txt");
        File.WriteAllText(marker, "kept");

        var second = await installer.InstallAsync(CancellationToken.None);

        Assert.NotNull(second);
        Assert.False(second!.Installed);
        Assert.Equal(first.EngineRoot, second.EngineRoot);
        Assert.True(File.Exists(marker), "повторная установка не должна перезаписывать готовую папку");
    }

    [Fact]
    public async Task InstallAsync_RemovesStaleVersions()
    {
        var cacheRoot = NewRoot();
        var stale = Path.Combine(cacheRoot, "0.9.0-old");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, ".ready"), "old");

        var installer = new PortableEngineInstaller(ValidZip(), "1.0.0", cacheRoot);
        var result = await installer.InstallAsync(CancellationToken.None);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(result!.EngineRoot));
    }

    [Fact]
    public async Task InstallAsync_MissingRequiredFile_ThrowsAndDoesNotMarkReady()
    {
        var cacheRoot = NewRoot();
        var zip = CreateZip(@"bin\winws.exe", @"lists\list-general.txt");
        var installer = new PortableEngineInstaller(zip, "1.0.0", cacheRoot);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.InstallAsync(CancellationToken.None));

        if (Directory.Exists(cacheRoot))
        {
            Assert.Empty(Directory.GetFiles(cacheRoot, ".ready", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public async Task InstallAsync_CorruptZip_Throws()
    {
        var installer = new PortableEngineInstaller([1, 2, 3, 4], "1.0.0", NewRoot());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.InstallAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_CacheFolderDependsOnVersionAndHash()
    {
        var cacheRoot = NewRoot();
        var first = await new PortableEngineInstaller(ValidZip(), "1.0.0", cacheRoot)
            .InstallAsync(CancellationToken.None);

        var otherZip = CreateZip(
            @"bin\winws.exe", @"bin\WinDivert.dll", @"bin\WinDivert64.sys",
            @"lists\list-general.txt", @"presets\presets.json", "extra.txt");
        var second = await new PortableEngineInstaller(otherZip, "1.0.0", cacheRoot)
            .InstallAsync(CancellationToken.None);

        Assert.NotEqual(first!.EngineRoot, second!.EngineRoot);
    }

    [Fact]
    public async Task InstallAsync_Cancelled_DoesNotWriteReady()
    {
        var cacheRoot = NewRoot();
        var installer = new PortableEngineInstaller(ValidZip(), "1.0.0", cacheRoot);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.InstallAsync(cts.Token));

        if (Directory.Exists(cacheRoot))
        {
            Assert.Empty(Directory.GetFiles(cacheRoot, ".ready", SearchOption.AllDirectories));
        }
    }
}
