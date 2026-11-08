using System.IO;
using ZprtNextGen.Core.Engine;

namespace ZprtNextGen.Core.Tests.Engine;

public class EngineLocatorTests
{
    private static string TempAppDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"), "app");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateEngineFiles(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "lists"));
        File.WriteAllText(Path.Combine(root, "bin", "winws.exe"), "stub");
        File.WriteAllText(Path.Combine(root, "bin", "WinDivert.dll"), "stub");
        File.WriteAllText(Path.Combine(root, "bin", "WinDivert64.sys"), "stub");
        File.WriteAllText(Path.Combine(root, "lists", "list-general.txt"), "stub");
    }

    [Fact]
    public void Locate_BundledEngine_Valid()
    {
        var app = TempAppDir();
        var engineRoot = Path.Combine(app, "engine");
        CreateEngineFiles(engineRoot);

        var result = EngineLocator.Locate(null, app);

        Assert.True(result.IsValid);
        Assert.EndsWith(Path.DirectorySeparatorChar + "engine", result.EngineRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void Locate_ConfiguredPathWins()
    {
        var app = TempAppDir();
        var configured = Path.Combine(app, "configured-engine");
        var bundled = Path.Combine(app, "engine");
        CreateEngineFiles(configured);
        CreateEngineFiles(bundled);

        var result = EngineLocator.Locate(configured, app);

        Assert.True(result.IsValid);
        Assert.Equal(configured, result.EngineRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void Locate_BrokenConfigured_FallsBackToBundled()
    {
        var app = TempAppDir();
        var configured = Path.Combine(app, "configured-engine");
        var bundled = Path.Combine(app, "engine");
        Directory.CreateDirectory(configured); // без обязательных файлов
        CreateEngineFiles(bundled);

        var result = EngineLocator.Locate(configured, app);

        Assert.True(result.IsValid);
        Assert.Equal(bundled, result.EngineRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void Locate_NothingValid_ReturnsMissingFiles()
    {
        var app = TempAppDir(); // пустая app, движок отсутствует

        var result = EngineLocator.Locate(null, app);

        Assert.False(result.IsValid);
        Assert.Null(result.EngineRoot);
        Assert.Contains(@"bin\winws.exe", result.MissingFiles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locate_PathWithSpacesAndCyrillic_Valid()
    {
        var root = @"C:\Temp\Запрет движок";
        Directory.CreateDirectory(root);
        CreateEngineFiles(root);

        var result = EngineLocator.Locate(root, @"C:\Temp");

        Assert.True(result.IsValid);
        Assert.Equal(root, result.EngineRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void Locate_AcceptsUpstreamRoot()
    {
        var app = TempAppDir();
        CreateEngineFiles(app); // bin/ и lists/ напрямую в корне, подпапки engine нет

        var result = EngineLocator.Locate(null, app);

        Assert.True(result.IsValid);
        Assert.Equal(app, result.EngineRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(result.MissingFiles);
    }
}
