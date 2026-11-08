using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Presentation;

namespace ZprtNextGen.Core.Tests.Presentation;

public class AnimationStateResolverTests
{
    [Fact]
    public void Stopped_WithoutReport_ReturnsStopped()
    {
        var state = AnimationStateResolver.Resolve(EngineStatus.Stopped, null);

        Assert.Equal(MeditationState.Stopped, state);
    }

    [Fact]
    public void Starting_WithoutReport_ReturnsConnecting()
    {
        var state = AnimationStateResolver.Resolve(EngineStatus.Starting, null);

        Assert.Equal(MeditationState.Connecting, state);
    }

    [Fact]
    public void Running_WithoutReport_ReturnsConnecting()
    {
        var state = AnimationStateResolver.Resolve(EngineStatus.Running, null);

        Assert.Equal(MeditationState.Connecting, state);
    }

    [Fact]
    public void Running_WithPrimarySuccess_ReturnsConnected()
    {
        var report = new ConnectivityReport(new[]
        {
            new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), true, 12, null)
        });

        var state = AnimationStateResolver.Resolve(EngineStatus.Running, report);

        Assert.Equal(MeditationState.Connected, state);
    }

    [Fact]
    public void Running_WithOnlySecondarySuccess_ReturnsError()
    {
        var report = new ConnectivityReport(new[]
        {
            new ConnectivityResult(new ConnectivityTarget("s", "Secondary", "https://example.net", false), true, 12, null)
        });

        var state = AnimationStateResolver.Resolve(EngineStatus.Running, report);

        Assert.Equal(MeditationState.Error, state);
    }

    [Fact]
    public void Error_WithReport_ReturnsError()
    {
        var report = new ConnectivityReport(new[]
        {
            new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), true, 12, null)
        });

        var state = AnimationStateResolver.Resolve(EngineStatus.Error, report);

        Assert.Equal(MeditationState.Error, state);
    }

    [Fact]
    public void Stopping_WithReport_ReturnsConnecting()
    {
        var report = new ConnectivityReport(new[]
        {
            new ConnectivityResult(new ConnectivityTarget("p", "Primary", "https://example.com", true), true, 12, null)
        });

        var state = AnimationStateResolver.Resolve(EngineStatus.Stopping, report);

        Assert.Equal(MeditationState.Connecting, state);
    }
}
