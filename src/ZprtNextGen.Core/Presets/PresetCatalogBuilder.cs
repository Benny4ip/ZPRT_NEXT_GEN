namespace ZprtNextGen.Core.Presets;

using ZprtNextGen.Core.Settings;

/// <summary>Объединение встроенного каталога с пользовательскими пресетами.</summary>
public static class PresetCatalogBuilder
{
    /// <summary>Идентификатор группы пользовательских пресетов.</summary>
    public const string CustomGroupId = "custom";

    /// <summary>Название группы пользовательских пресетов.</summary>
    public const string CustomGroupName = "Мои пресеты";

    /// <summary>
    /// Добавить пользовательские пресеты группой «Мои пресеты». Невалидные,
    /// дублирующиеся и конфликтующие с каталогом записи пропускаются.
    /// </summary>
    public static PresetCatalog Build(PresetCatalog catalog, IReadOnlyList<CustomPreset> customPresets)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (customPresets is null || customPresets.Count == 0)
            return catalog;

        var existingIds = new HashSet<string>(
            catalog.Presets.Select(preset => preset.Id), StringComparer.OrdinalIgnoreCase);
        var presets = catalog.Presets.ToList();

        foreach (var custom in customPresets)
        {
            if (custom is null)
                continue;

            var id = custom.Id?.Trim() ?? string.Empty;
            var name = custom.Name?.Trim() ?? string.Empty;
            var args = custom.Args?.Trim() ?? string.Empty;

            if (!id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Length == 0 || args.Length == 0)
                continue;
            if (!existingIds.Add(id))
                continue;

            presets.Add(new Preset(
                id,
                CustomGroupId,
                name,
                custom.Description?.Trim() ?? string.Empty,
                args,
                PresetSource.Custom));
        }

        if (presets.Count == catalog.Presets.Count)
            return catalog;

        var groups = catalog.Groups.ToList();
        groups.Add(new PresetGroup(CustomGroupId, CustomGroupName));
        presets.Sort((x, y) => PresetNaming.CompareIds(x.Id, y.Id));

        return catalog with
        {
            Groups = groups,
            Presets = presets,
        };
    }
}
