using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class VersionComparerTests
{
    [Theory]
    [InlineData("1.10.4", "1.10.2", 1)]
    [InlineData("v1.10.4", "1.10.2", 1)]
    [InlineData("1.10.2", "1.10.4", -1)]
    [InlineData("1.10.4", "1.10.4", 0)]
    [InlineData("1.10.4", "v1.10.4", 0)]
    [InlineData(null, "1.0.0", -1)]
    [InlineData("1.0.0", null, 1)]
    [InlineData(null, null, 0)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.2", "1.2.0", 0)]
    public void Compare_RanksVersions(string? a, string? b, int expectedSign)
    {
        var result = VersionComparer.Compare(a, b);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Theory]
    [InlineData("1.10.4", "1.10.2", true)]
    [InlineData("v1.10.4", "1.10.2", true)]
    [InlineData("1.10.2", "1.10.4", false)]
    [InlineData("1.10.4", "1.10.4", false)]
    [InlineData(null, "1.0", false)]
    [InlineData("1.0", null, true)]
    public void IsNewer_MatchesCompare(string? candidate, string? current, bool expected)
    {
        Assert.Equal(expected, VersionComparer.IsNewer(candidate, current));
    }
}
