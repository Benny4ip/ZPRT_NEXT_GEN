namespace ZprtNextGen.Core.Presets;

public sealed record PresetGroup(string Id, string Name);

public sealed record Preset(
    string Id,
    string GroupId,
    string Name,
    string Description,
    string Arguments);

public sealed record PresetCatalog(
    int Version,
    string Engine,
    string EngineVersion,
    IReadOnlyList<PresetGroup> Groups,
    IReadOnlyList<Preset> Presets);
