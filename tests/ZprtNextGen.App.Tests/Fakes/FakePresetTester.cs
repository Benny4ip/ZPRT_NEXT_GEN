using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый тестер пресетов для тестов ViewModel.</summary>
public sealed class FakePresetTester : IPresetTester
{
    public readonly List<PresetTestResult> Results = new();
    public IReadOnlyList<Preset>? LastPresets;
    public string? LastEngineRoot;
    public GameFilterMode LastFilter;
    public CancellationToken LastCt;
    public bool Hang;

    public async Task<IReadOnlyList<PresetTestResult>> TestAllAsync(IReadOnlyList<Preset> presets, string engineRoot,
        GameFilterMode filter, IProgress<PresetTestProgress>? progress, CancellationToken ct)
    {
        LastPresets = presets;
        LastEngineRoot = engineRoot;
        LastFilter = filter;
        LastCt = ct;

        if (Hang)
            await Task.Delay(-1, ct);

        for (var i = 0; i < presets.Count; i++)
            progress?.Report(new PresetTestProgress(i + 1, presets.Count, presets[i],
                i < Results.Count ? Results[i] : null));
        return Results;
    }
}
