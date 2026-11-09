using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace ZprtNextGen.Core.Presets;

/// <summary>Сериализация каталога пресетов в ту же форму, что и upstream-импорт.</summary>
public static class PresetCatalogWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Сериализовать каталог в JSON.</summary>
    public static string Serialize(PresetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var dto = new CatalogDto
        {
            Version = catalog.Version,
            Engine = catalog.Engine,
            EngineVersion = catalog.EngineVersion,
            Groups = catalog.Groups.Select(g => new GroupDto { Id = g.Id, Name = g.Name }).ToList(),
            Presets = catalog.Presets.Select(p => new PresetDto
            {
                Id = p.Id, Group = p.GroupId, Name = p.Name,
                Description = p.Description, Args = p.Arguments
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Сохранить каталог в файл (UTF-8 без BOM, с переводом строки в конце).</summary>
    public static void Save(PresetCatalog catalog, string path)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = Serialize(catalog);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.AppendAllText(path, Environment.NewLine);
    }

    private sealed class CatalogDto
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("engine")] public string Engine { get; init; } = string.Empty;
        [JsonPropertyName("engineVersion")] public string EngineVersion { get; init; } = string.Empty;
        [JsonPropertyName("groups")] public List<GroupDto> Groups { get; init; } = new();
        [JsonPropertyName("presets")] public List<PresetDto> Presets { get; init; } = new();
    }

    private sealed class GroupDto
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    }

    private sealed class PresetDto
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("group")] public string Group { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
        [JsonPropertyName("args")] public string Args { get; init; } = string.Empty;
    }
}
