using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZprtNextGen.Core.Presets;

public static class PresetCatalogLoader
{
    private const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PresetCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<CatalogDto>(stream, Options)
            ?? throw new InvalidDataException("Каталог пресетов пуст.");

        if (dto.Version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"Неподдерживаемая версия каталога пресетов: {dto.Version}. Ожидается {SupportedVersion}.");
        }

        var groups = ReadGroups(dto.Groups);
        var presets = ReadPresets(dto.Presets, groups);

        return new PresetCatalog(
            dto.Version,
            dto.Engine ?? string.Empty,
            dto.EngineVersion ?? string.Empty,
            groups,
            presets);
    }

    private static List<PresetGroup> ReadGroups(List<GroupDto>? dtos)
    {
        if (dtos is null || dtos.Count == 0)
        {
            throw new InvalidDataException("Каталог пресетов не содержит групп.");
        }

        var groups = new List<PresetGroup>(dtos.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dto in dtos)
        {
            var group = new PresetGroup(dto.Id ?? string.Empty, dto.Name ?? string.Empty);
            if (!ids.Add(group.Id))
            {
                throw new InvalidDataException($"Дублирующийся идентификатор группы: '{group.Id}'.");
            }

            groups.Add(group);
        }

        return groups;
    }

    private static List<Preset> ReadPresets(List<PresetDto>? dtos, List<PresetGroup> groups)
    {
        if (dtos is null || dtos.Count == 0)
        {
            throw new InvalidDataException("Каталог пресетов не содержит пресетов.");
        }

        var groupIds = groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var presets = new List<Preset>(dtos.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dto in dtos)
        {
            var preset = new Preset(
                dto.Id ?? string.Empty,
                dto.Group ?? string.Empty,
                dto.Name ?? string.Empty,
                dto.Description ?? string.Empty,
                dto.Args ?? string.Empty);

            if (!groupIds.Contains(preset.GroupId))
            {
                throw new InvalidDataException(
                    $"Пресет '{preset.Id}' ссылается на неизвестную группу '{preset.GroupId}'.");
            }

            if (!ids.Add(preset.Id))
            {
                throw new InvalidDataException($"Дублирующийся идентификатор пресета: '{preset.Id}'.");
            }

            presets.Add(preset);
        }

        // Порядок в файле может быть лексикографическим («1, 10, 11, 2…»):
        // для UI и тестирования приводим пресеты к естественному порядку.
        presets.Sort((a, b) => PresetNaming.CompareIds(a.Id, b.Id));
        return presets;
    }

    private sealed class CatalogDto
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("engine")]
        public string? Engine { get; init; }

        [JsonPropertyName("engineVersion")]
        public string? EngineVersion { get; init; }

        [JsonPropertyName("groups")]
        public List<GroupDto>? Groups { get; init; }

        [JsonPropertyName("presets")]
        public List<PresetDto>? Presets { get; init; }
    }

    private sealed class GroupDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class PresetDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("group")]
        public string? Group { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("args")]
        public string? Args { get; init; }
    }
}
