using System;
using Xunit;
using ZprtNextGen.App.Services;

namespace ZprtNextGen.App.Tests.Services;

public class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuard_SameName_IsNotOwner()
    {
        var name = Guid.NewGuid().ToString("N");

        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public void AfterDispose_NewGuard_IsOwner()
    {
        var name = Guid.NewGuid().ToString("N");

        SingleInstanceGuard? first = null;
        first = new SingleInstanceGuard(name);
        Assert.True(first.IsOwner);

        first.Dispose();
        first = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        using var third = new SingleInstanceGuard(name);
        Assert.True(third.IsOwner);
    }
}
