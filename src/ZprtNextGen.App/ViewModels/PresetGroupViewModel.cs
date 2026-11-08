#nullable enable

using System.Collections.Generic;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Группа пресетов для отображения в UI.</summary>
public class PresetGroupViewModel
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsExpanded { get; set; }

    public IReadOnlyList<Preset> Presets { get; set; } = Array.Empty<Preset>();
}
