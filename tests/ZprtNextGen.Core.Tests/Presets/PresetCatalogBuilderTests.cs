using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.Core.Tests.Presets;

public class PresetCatalogBuilderTests
{
    private static PresetCatalog Catalog() => new(
        1,
        "zapret",
        "1.10.4",
        [new PresetGroup("general", "Обычные")],
        [
            new Preset("general", "general", "Основной", "Базовый", "--wf-tcp=80"),
            new Preset("general-alt2", "general", "Альтернатива 2", "Второй", "--wf-tcp=443"),
        ]);

    [Fact]
    public void Build_AddsCustomGroupAndPresets()
    {
        var custom = new[]
        {
            new CustomPreset("custom:1", "Мой обход", "Для дома", "--wf-tcp=80"),
            new CustomPreset("custom:2", "Второй обход", "", "--wf-tcp=443"),
        };

        var result = PresetCatalogBuilder.Build(Catalog(), custom);

        var group = Assert.Single(result.Groups, g => g.Id == "custom");
        Assert.Equal("Мои пресеты", group.Name);
        Assert.Equal(4, result.Presets.Count);
        Assert.Equal(2, result.Presets.Count(p => p.Source == PresetSource.Custom));
        Assert.All(result.Presets.Where(p => p.Source == PresetSource.Custom),
            p => Assert.Equal("custom", p.GroupId));
    }

    [Fact]
    public void Build_NoCustomPresets_ReturnsOriginalCatalog()
    {
        var catalog = Catalog();

        var result = PresetCatalogBuilder.Build(catalog, []);

        Assert.Same(catalog, result);
    }

    [Fact]
    public void Build_InvalidAndDuplicateCustomPresets_AreSkipped()
    {
        var custom = new[]
        {
            new CustomPreset("custom:1", "Рабочий", "", "--wf-tcp=80"),
            new CustomPreset("custom:1", "Дубль", "", "--wf-tcp=443"),
            new CustomPreset("custom:2", " ", "", "--wf-tcp=443"),
            new CustomPreset("custom:3", "Без аргументов", "", "  "),
            new CustomPreset("general-alt2", "Чужой id", "", "--wf-tcp=443"),
        };

        var result = PresetCatalogBuilder.Build(Catalog(), custom);

        var added = Assert.Single(result.Presets, p => p.Source == PresetSource.Custom);
        Assert.Equal("custom:1", added.Id);
    }

    [Fact]
    public void Build_KeepsNaturalOrder()
    {
        var custom = new[]
        {
            new CustomPreset("custom:10", "Десятый", "", "--wf-tcp=80"),
            new CustomPreset("custom:2", "Второй", "", "--wf-tcp=80"),
        };

        var result = PresetCatalogBuilder.Build(Catalog(), custom);

        var ids = result.Presets.Select(p => p.Id).ToList();
        Assert.True(ids.IndexOf("custom:2") < ids.IndexOf("custom:10"));
    }
}
