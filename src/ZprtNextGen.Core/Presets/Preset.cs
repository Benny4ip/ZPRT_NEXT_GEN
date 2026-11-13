namespace ZprtNextGen.Core.Presets;

public sealed record PresetGroup(string Id, string Name);

/// <summary>Источник пресета: встроенный каталог или пользовательский.</summary>
public enum PresetSource
{
    BuiltIn,
    Custom,
}

public sealed record Preset(
    string Id,
    string GroupId,
    string Name,
    string Description,
    string Arguments,
    PresetSource Source = PresetSource.BuiltIn);

public sealed record PresetCatalog(
    int Version,
    string Engine,
    string EngineVersion,
    IReadOnlyList<PresetGroup> Groups,
    IReadOnlyList<Preset> Presets);
