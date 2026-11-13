using System.Threading;
using System.Threading.Tasks;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Р¤РµР№РєРѕРІРѕРµ РѕР±РЅРѕРІР»РµРЅРёРµ РїСЂРѕРіСЂР°РјРјС‹ РґР»СЏ С‚РµСЃС‚РѕРІ ViewModel.</summary>
public sealed class FakeAppUpdater : IAppUpdater
{
    public AppUpdatePlan Plan = new(@"C:\temp\ZPRT_Update.cmd", @"C:\temp\ZprtNextGen.exe", @"C:\app\ZprtNextGen.exe");
    public AppUpdatePlan? LastPrepared;
    public bool Launched;
    public AppUpdatePlan? LaunchedPlan;

    public Task<AppUpdatePlan> PrepareAsync(ReleaseInfo release, string appDirectory,
        IProgress<string>? progress, CancellationToken ct, bool isPortable = false)
    {
        LastPrepared = new AppUpdatePlan(Plan.ScriptPath, Plan.NewExecutablePath, Plan.TargetExecutablePath);
        return Task.FromResult(Plan);
    }

    public void Launch(AppUpdatePlan plan)
    {
        Launched = true;
        LaunchedPlan = plan;
    }
}

