using System.IO;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.Core.Tests.Settings;

public class SettingsStoreTests
{
    private static string TempSettingsPath(string fileName = "settings.json")
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        // Файл намеренно не создаём.
        var store = new SettingsStore(TempSettingsPath());

        var settings = store.Load();

        Assert.Equal("general", settings.PresetId);
        Assert.Equal(GameFilterMode.Disabled, settings.GameFilter);
        Assert.True(settings.MinimizeToTray);
        Assert.Empty(settings.CollapsedGroups);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = TempSettingsPath();
        var store = new SettingsStore(path);

        var original = new AppSettings
        {
            EnginePath = @"C:\Игры\Zapret\winws.exe",
            PresetId = "gaming",
            GameFilter = GameFilterMode.All,
            Autostart = true,
            MinimizeToTray = false,
            Theme = "light",
            ReduceMotion = true,
            CollapsedGroups = new List<string> { "browser", "игры" }
        };

        store.Save(original);

        var loaded = store.Load();

        Assert.Equal(original.EnginePath, loaded.EnginePath);
        Assert.Equal(original.PresetId, loaded.PresetId);
        Assert.Equal(original.GameFilter, loaded.GameFilter);
        Assert.Equal(original.Autostart, loaded.Autostart);
        Assert.Equal(original.MinimizeToTray, loaded.MinimizeToTray);
        Assert.Equal(original.Theme, loaded.Theme);
        Assert.Equal(original.ReduceMotion, loaded.ReduceMotion);
        Assert.Equal(original.CollapsedGroups, loaded.CollapsedGroups);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndRenames()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, "{oops");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("general", settings.PresetId);
        Assert.Equal(GameFilterMode.Disabled, settings.GameFilter);
        Assert.True(settings.MinimizeToTray);
        Assert.Empty(settings.CollapsedGroups);
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Load_WrongEnumValue_ReturnsDefaults()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, """{"gameFilter":"nonsense"}""");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("general", settings.PresetId);
        Assert.Equal(GameFilterMode.Disabled, settings.GameFilter);
        Assert.True(settings.MinimizeToTray);
        Assert.Empty(settings.CollapsedGroups);
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"), "nested", "deep");
        var path = Path.Combine(dir, "settings.json");
        var store = new SettingsStore(path);

        store.Save(new AppSettings { Theme = "light" });

        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(path));
        Assert.Equal("light", store.Load().Theme);
    }

    [Fact]
    public void Load_NullThemeAndCollapsedGroups_NormalizesToDefaults()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, """{"theme":null,"collapsedGroups":null,"presetId":null,"enginePath":"   "}""");

        var settings = new SettingsStore(path).Load();

        Assert.Equal("dark", settings.Theme);
        Assert.Empty(settings.CollapsedGroups);
        Assert.Equal("general", settings.PresetId);
        Assert.Null(settings.EnginePath);
    }

    [Fact]
    public void Save_ConcurrentCalls_LeaveValidFileAndNoTempFiles()
    {
        var path = TempSettingsPath();
        var store = new SettingsStore(path);

        Parallel.For(0, 20, i => store.Save(new AppSettings { PresetId = "preset-" + i }));

        var loaded = store.Load();
        Assert.StartsWith("preset-", loaded.PresetId, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }
}
