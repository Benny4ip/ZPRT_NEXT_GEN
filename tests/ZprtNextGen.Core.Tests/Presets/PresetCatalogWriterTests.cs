using System.Text.Json;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Tests.Presets;

public class PresetCatalogWriterTests
{
    [Fact]
    public void Serialize_RoundTrip_PreservesCatalog()
    {
        var catalog = new PresetCatalog(
            1,
            "Flowseal/zapret-discord-youtube",
            "1.10.4",
            new[] { new PresetGroup("general", "Обычные") },
            new[]
            {
                new Preset("general", "general", "Основной", "Базовый набор.", "--wf-tcp=80"),
                new Preset("general-alt2", "general", "Альтернатива 2", "Вариант 2.", "--wf-tcp=443"),
            });

        var json = PresetCatalogWriter.Serialize(catalog);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("Flowseal/zapret-discord-youtube", root.GetProperty("engine").GetString());
        Assert.Equal("1.10.4", root.GetProperty("engineVersion").GetString());

        var groups = root.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Single(groups);
        Assert.Equal("general", groups[0].GetProperty("id").GetString());
        Assert.Equal("Обычные", groups[0].GetProperty("name").GetString());

        var presets = root.GetProperty("presets").EnumerateArray().ToArray();
        Assert.Equal(2, presets.Length);
        Assert.Equal("general", presets[0].GetProperty("id").GetString());
        Assert.Equal("--wf-tcp=80", presets[0].GetProperty("args").GetString());
        Assert.Equal("general-alt2", presets[1].GetProperty("id").GetString());
        Assert.Equal("--wf-tcp=443", presets[1].GetProperty("args").GetString());
    }

    [Fact]
    public void Save_WritesFile_WithTrailingNewline()
    {
        var catalog = new PresetCatalog(1, "e", "1.0", Array.Empty<PresetGroup>(), Array.Empty<Preset>());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "presets.json");

        try
        {
            PresetCatalogWriter.Save(catalog, path);

            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.EndsWith(Environment.NewLine, text);
            // Атомарная запись не оставляет временных файлов (в т.ч. при повторном сохранении).
            PresetCatalogWriter.Save(catalog, path);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
