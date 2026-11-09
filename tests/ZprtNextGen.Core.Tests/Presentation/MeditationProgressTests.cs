using ZprtNextGen.Core.Presentation;

namespace ZprtNextGen.Core.Tests.Presentation;

public class MeditationProgressTests
{
    [Theory]
    [InlineData(MeditationState.Stopped, 0.0)]
    [InlineData(MeditationState.Connecting, 0.85)]
    [InlineData(MeditationState.Connected, 1.0)]
    [InlineData(MeditationState.Error, 0.5)]
    public void Resolve_MapsStateToProgress(MeditationState state, double expected)
    {
        Assert.Equal(expected, MeditationProgress.Resolve(state));
    }
}
