using System.Windows.Media;
using ZprtNextGen.App.Controls;

namespace ZprtNextGen.App.Tests.Controls;

public class MeditationViewTests
{
    [Fact]
    public void CreateProgressArcGeometry_EqualsOne_ReturnsNonEmptyFullCircle()
    {
        var geometry = MeditationView.CreateProgressArcGeometry(1.0);

        Assert.NotNull(geometry);
        Assert.NotSame(Geometry.Empty, geometry);
        Assert.True(geometry.Bounds.Width > 0);
        Assert.True(geometry.Bounds.Height > 0);
    }

    [Fact]
    public void CreateProgressArcGeometry_EqualsHalf_ReturnsNonEmptyArc()
    {
        var geometry = MeditationView.CreateProgressArcGeometry(0.5);

        Assert.NotNull(geometry);
        Assert.NotSame(Geometry.Empty, geometry);
    }

    [Fact]
    public void CreateProgressArcGeometry_EqualsZero_ReturnsNull()
    {
        Assert.Null(MeditationView.CreateProgressArcGeometry(0.0));
    }

    [Fact]
    public void CreateProgressArcGeometry_ClampsOutOfRange()
    {
        Assert.Null(MeditationView.CreateProgressArcGeometry(-0.5));
        Assert.NotNull(MeditationView.CreateProgressArcGeometry(1.5));
    }
}
