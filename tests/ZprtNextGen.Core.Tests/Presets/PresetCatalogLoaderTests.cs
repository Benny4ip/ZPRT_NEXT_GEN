using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Tests.Presets;

public class PresetCatalogLoaderTests
{
    private static string WriteTempCatalog(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_ValidCatalog_ReturnsGroupsAndPresets()
    {
        var path = WriteTempCatalog(
            """
            {
              "version": 1,
              "engine": "Flowseal/zapret-discord-youtube",
              "engineVersion": "1.10.2",
              "groups": [
                { "id": "general", "name": "Обычные" }
              ],
              "presets": [
                {
                  "id": "general",
                  "group": "general",
                  "name": "Основной",
                  "description": "Базовый",
                  "args": "--wf-tcp=80"
                }
              ]
            }
            """);

        try
        {
            var catalog = PresetCatalogLoader.Load(path);

            Assert.Equal(1, catalog.Version);
            Assert.Equal("Flowseal/zapret-discord-youtube", catalog.Engine);
            Assert.Equal("1.10.2", catalog.EngineVersion);

            var group = Assert.Single(catalog.Groups);
            Assert.Equal("general", group.Id);
            Assert.Equal("Обычные", group.Name);

            var preset = Assert.Single(catalog.Presets);
            Assert.Equal("general", preset.Id);
            Assert.Equal("general", preset.GroupId);
            Assert.Equal("Основной", preset.Name);
            Assert.Equal("Базовый", preset.Description);
            Assert.Equal("--wf-tcp=80", preset.Arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_LexicographicFileOrder_ReturnsNaturalOrder()
    {
        var path = WriteTempCatalog(
            """
            {
              "version": 1,
              "engine": "Flowseal/zapret-discord-youtube",
              "engineVersion": "1.10.4",
              "groups": [
                { "id": "general", "name": "Обычные" }
              ],
              "presets": [
                { "id": "general-alt10", "group": "general", "name": "10", "description": "", "args": "--a" },
                { "id": "general-alt2", "group": "general", "name": "2", "description": "", "args": "--b" },
                { "id": "general", "group": "general", "name": "0", "description": "", "args": "--c" },
                { "id": "general-alt", "group": "general", "name": "1", "description": "", "args": "--d" }
              ]
            }
            """);

        try
        {
            var catalog = PresetCatalogLoader.Load(path);

            Assert.Equal(
                new[] { "general", "general-alt", "general-alt2", "general-alt10" },
                catalog.Presets.Select(p => p.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_PresetWithUnknownGroup_Throws()
    {
        var path = WriteTempCatalog(
            """
            {
              "version": 1,
              "engine": "Flowseal/zapret-discord-youtube",
              "engineVersion": "1.10.2",
              "groups": [
                { "id": "general", "name": "Обычные" }
              ],
              "presets": [
                {
                  "id": "general",
                  "group": "missing",
                  "name": "Основной",
                  "description": "Базовый",
                  "args": "--wf-tcp=80"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => PresetCatalogLoader.Load(path));
            Assert.Contains("missing", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_DuplicatePresetId_Throws()
    {
        var path = WriteTempCatalog(
            """
            {
              "version": 1,
              "engine": "Flowseal/zapret-discord-youtube",
              "engineVersion": "1.10.2",
              "groups": [
                { "id": "general", "name": "Обычные" }
              ],
              "presets": [
                {
                  "id": "general",
                  "group": "general",
                  "name": "Первый",
                  "description": "Базовый",
                  "args": "--wf-tcp=80"
                },
                {
                  "id": "general",
                  "group": "general",
                  "name": "Второй",
                  "description": "Базовый",
                  "args": "--wf-tcp=443"
                }
              ]
            }
            """);

        try
        {
            Assert.Throws<InvalidDataException>(() => PresetCatalogLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_UnsupportedVersion_Throws()
    {
        var path = WriteTempCatalog(
            """
            {
              "version": 2,
              "engine": "Flowseal/zapret-discord-youtube",
              "engineVersion": "1.10.2",
              "groups": [
                { "id": "general", "name": "Обычные" }
              ],
              "presets": [
                {
                  "id": "general",
                  "group": "general",
                  "name": "Основной",
                  "description": "Базовый",
                  "args": "--wf-tcp=80"
                }
              ]
            }
            """);

        try
        {
            Assert.Throws<InvalidDataException>(() => PresetCatalogLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
