using System.IO;
using System.Text.Json;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.Core.Tests.Settings;

public class ConfigStoreTests
{
    private static (string Directory, string ConfigPath, string LegacyPath) NewPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZprtNextGen.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, Path.Combine(dir, "config.json"), Path.Combine(dir, "settings.json"));
    }

    private static ConfigStore NewStore(string configPath, string legacyPath) =>
        new(configPath, legacyPath);

    [Fact]
    public void Load_NoFiles_DefaultsToAuto()
    {
        var paths = NewPaths();
        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.Equal(ConfigStore.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(ConnectionMode.Auto, config.Connection.Mode);
        Assert.Null(config.Connection.AutoProfile);
        Assert.True(config.Behavior.MinimizeToTray);
        Assert.True(config.Connection.HealthCheck.Enabled);
        Assert.Empty(config.CustomPresets);
        Assert.False(File.Exists(paths.ConfigPath));
    }

    [Fact]
    public void Load_LegacySettings_MigratesAllFields()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.LegacyPath, """
        {
          "enginePath": "C:\\Engine",
          "presetId": "general-alt2",
          "gameFilter": "All",
          "autostart": true,
          "minimizeToTray": false,
          "theme": "light",
          "reduceMotion": true,
          "collapsedGroups": ["general", "faketls"]
        }
        """);

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.Equal(@"C:\Engine", config.Engine.Path);
        Assert.Equal(ConnectionMode.Manual, config.Connection.Mode);
        Assert.Equal("general-alt2", config.Connection.ManualPresetId);
        Assert.Equal(GameFilterMode.All, config.GameFilter);
        Assert.True(config.Behavior.Autostart);
        Assert.False(config.Behavior.MinimizeToTray);
        Assert.Equal("light", config.Behavior.Theme);
        Assert.True(config.Behavior.ReduceMotion);
        Assert.Equal(new[] { "general", "faketls" }, config.Ui.CollapsedGroups);
    }

    [Fact]
    public void Load_LegacySettings_WritesConfigAndKeepsBackupAndOriginal()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.LegacyPath, """{"presetId":"general-alt2"}""");

        var store = NewStore(paths.ConfigPath, paths.LegacyPath);
        store.Load();

        Assert.True(File.Exists(paths.ConfigPath));
        Assert.True(File.Exists(paths.LegacyPath));
        Assert.True(File.Exists(paths.LegacyPath + ".bak"));
        Assert.True(File.Exists(paths.ConfigPath));
    }

    [Fact]
    public void Load_ExistingConfig_WinsOverLegacy()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.LegacyPath, """{"presetId":"from-legacy"}""");
        new ConfigStore(paths.ConfigPath, paths.LegacyPath).Save(new AppConfig
        {
            Connection = new ConnectionSettings { Mode = ConnectionMode.Auto, ManualPresetId = "from-config" }
        });

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.Equal(ConnectionMode.Auto, config.Connection.Mode);
        Assert.Equal("from-config", config.Connection.ManualPresetId);
    }

    [Fact]
    public void Load_CorruptConfig_MovesToBadAndUsesDefaults()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.ConfigPath, "{oops");

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.Equal(ConnectionMode.Auto, config.Connection.Mode);
        Assert.True(File.Exists(paths.ConfigPath + ".bad"));
        Assert.False(File.Exists(paths.ConfigPath));
    }

    [Fact]
    public void Load_FutureSchemaVersion_BacksUpAndUsesDefaults()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.ConfigPath, """{"schemaVersion":99,"connection":{"mode":"manual"}}""");

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.Equal(ConnectionMode.Auto, config.Connection.Mode);
        Assert.True(File.Exists(paths.ConfigPath + ".bak"));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var paths = NewPaths();
        var store = NewStore(paths.ConfigPath, paths.LegacyPath);
        var original = new AppConfig
        {
            Engine = new EngineSettings { Path = @"C:\Zapret\engine" },
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = "general-alt7",
                AutoProfile = new AutoProfile(
                    "general-alt7", "Альтернатива 7", 143, 131, 143,
                    new DateTimeOffset(2026, 11, 13, 9, 15, 0, TimeSpan.Zero), "1.10.4", "sha256:abc"),
                HealthCheck = new HealthCheckSettings(true, 45, 3, 120)
            },
            CustomPresets = [new CustomPreset("custom:1", "Мой", "Описание", "--wf-tcp=80")],
            GameFilter = GameFilterMode.Tcp,
            Behavior = new BehaviorSettings
            {
                Autostart = true,
                MinimizeToTray = false,
                ReduceMotion = true,
                Theme = "light"
            },
            Updates = new UpdateSettings { AutoUpdateEngine = false },
            Ui = new UiSettings { CollapsedGroups = ["general"] }
        };

        store.Save(original);
        var loaded = store.Load(["general-alt7", "general"]);

        Assert.Equal(@"C:\Zapret\engine", loaded.Engine.Path);
        Assert.Equal(ConnectionMode.Manual, loaded.Connection.Mode);
        Assert.Equal("general-alt7", loaded.Connection.ManualPresetId);
        Assert.Equal(143, loaded.Connection.AutoProfile!.LatencyMs);
        Assert.Equal(131, loaded.Connection.AutoProfile.YoutubeMs);
        Assert.Equal(45, loaded.Connection.HealthCheck.IntervalSeconds);
        Assert.Equal("custom:1", Assert.Single(loaded.CustomPresets).Id);
        Assert.Equal(GameFilterMode.Tcp, loaded.GameFilter);
        Assert.True(loaded.Behavior.Autostart);
        Assert.False(loaded.Behavior.MinimizeToTray);
        Assert.True(loaded.Behavior.ReduceMotion);
        Assert.False(loaded.Updates.AutoUpdateEngine);
        Assert.Equal(new[] { "general" }, loaded.Ui.CollapsedGroups);
    }

    [Fact]
    public void Load_ClampsHealthCheckValues()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.ConfigPath, """
        {
          "schemaVersion": 2,
          "connection": {
            "mode": "auto",
            "healthCheck": { "enabled": true, "intervalSeconds": 0, "failuresBeforeRetest": 0, "retestCooldownSeconds": -5 }
          }
        }
        """);

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.InRange(config.Connection.HealthCheck.IntervalSeconds, 10, 3600);
        Assert.InRange(config.Connection.HealthCheck.FailuresBeforeRetest, 1, 10);
        Assert.InRange(config.Connection.HealthCheck.RetestCooldownSeconds, 0, 86400);
    }

    [Fact]
    public void Load_UnknownEnumValues_FallBackToSafeDefaults()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.ConfigPath, """
        {
          "schemaVersion": 2,
          "connection": { "mode": "nonsense" },
          "gameFilter": "nonsense"
        }
        """);

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        Assert.Equal(ConnectionMode.Auto, config.Connection.Mode);
        Assert.Equal(GameFilterMode.Disabled, config.GameFilter);
    }

    [Fact]
    public void Load_DropsInvalidCustomPresets()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.ConfigPath, """
        {
          "schemaVersion": 2,
          "customPresets": [
            { "id": "custom:a", "name": "Рабочий", "args": "--wf-tcp=80" },
            { "id": "custom:a", "name": "Дубль", "args": "--wf-tcp=443" },
            { "id": "", "name": "Без id", "args": "--wf-tcp=443" },
            { "id": "custom:b", "name": "Без аргументов", "args": "   " }
          ]
        }
        """);

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load();

        var preset = Assert.Single(config.CustomPresets);
        Assert.Equal("custom:a", preset.Id);
        Assert.Equal("Рабочий", preset.Name);
    }

    [Fact]
    public void Load_UnknownAutoProfilePreset_DropsProfile()
    {
        var paths = NewPaths();
        File.WriteAllText(paths.ConfigPath, """
        {
          "schemaVersion": 2,
          "connection": {
            "mode": "auto",
            "autoProfile": {
              "presetId": "removed-preset",
              "name": "Старый",
              "latencyMs": 100,
              "testedAtUtc": "2026-11-13T09:15:00Z",
              "engineVersion": "1.10.4",
              "presetArgsHash": "sha256:x"
            }
          }
        }
        """);

        var config = NewStore(paths.ConfigPath, paths.LegacyPath).Load(["general"]);

        Assert.Null(config.Connection.AutoProfile);
    }

    [Fact]
    public void Save_ConcurrentCalls_LeaveValidFileAndNoTempFiles()
    {
        var paths = NewPaths();
        var store = NewStore(paths.ConfigPath, paths.LegacyPath);

        Parallel.For(0, 20, i => store.Save(new AppConfig
        {
            Connection = new ConnectionSettings { ManualPresetId = "preset-" + i }
        }));

        var loaded = store.Load();
        Assert.StartsWith("preset-", loaded.Connection.ManualPresetId, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(paths.Directory, "*.tmp"));
    }
}
