using System.Collections.Generic;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.App.Tests.ViewModels;

public class PresetGroupViewModelTests
{
    [Fact]
    public void ExpandedState_FromCollapsedGroups()
    {
        var presets = new List<Preset> { new("p1", "general", "Пресет 1", "Описание", string.Empty) };

        var collapsed = new PresetGroupViewModel("general", "Общий", presets, ["general"]);
        var expanded = new PresetGroupViewModel("other", "Другое", presets, ["general"]);

        Assert.False(collapsed.IsExpanded);
        Assert.True(expanded.IsExpanded);
    }

    [Fact]
    public void ToggleGroup_UpdatesCollapsedList()
    {
        var invocations = new List<(string Id, bool Expanded)>();
        var vm = new PresetGroupViewModel(
            "general", "Общий", Array.Empty<Preset>(), [],
            (id, expanded) => invocations.Add((id, expanded)));

        Assert.True(vm.IsExpanded);

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PresetGroupViewModel.IsExpanded))
                raised = true;
        };

        vm.IsExpanded = false;

        Assert.False(vm.IsExpanded);
        Assert.True(raised);
        Assert.Contains(("general", false), invocations);

        vm.IsExpanded = true;

        Assert.True(vm.IsExpanded);
        Assert.Contains(("general", true), invocations);
    }

    [Fact]
    public void SelectedPreset_ExposesGroupAndName()
    {
        var preset = new Preset("p1", "general", "Пресет один", "Описание пресета", "--wf-tcp=80");

        Assert.Equal("general", preset.GroupId);
        Assert.Equal("Пресет один", preset.Name);

        var vm = new PresetGroupViewModel("general", "Общий", [preset], []);

        Assert.Equal("general", vm.Presets[0].GroupId);
        Assert.Equal("Пресет один", vm.Presets[0].Name);
    }
}
