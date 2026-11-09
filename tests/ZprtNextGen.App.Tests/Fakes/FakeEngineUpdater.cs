using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый обновление движка для тестов ViewModel.</summary>
public sealed class FakeEngineUpdater : IEngineUpdater
{
    public EngineUpdateResult Result = new(true, "1.0.0", "1.10.4", 3, null);
    public ReleaseInfo? LastRelease;
    public string? LastEngineRoot;
    public string? LastPresetsPath;
    public int UpdateCalls;

    public Task<EngineUpdateResult> UpdateAsync(ReleaseInfo release, string engineRoot, string presetsPath,
        Func<Task> stopEngine, IProgress<string>? progress, CancellationToken ct)
    {
        UpdateCalls++;
        LastRelease = release;
        LastEngineRoot = engineRoot;
        LastPresetsPath = presetsPath;
        return Task.FromResult(Result);
    }
}
