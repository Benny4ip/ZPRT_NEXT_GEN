using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZprtNextGen.App.ViewModels;

namespace ZprtNextGen.App.Tests.ViewModels;

public sealed class CommandThreadSafetyTests
{
    [Fact]
    public async Task AsyncRelayCommand_RaiseCanExecuteChanged_CanBeCalledFromBackgroundThread()
    {
        var command = new AsyncRelayCommand(_ => Task.CompletedTask);
        var eventRaised = false;
        command.CanExecuteChanged += (_, _) => eventRaised = true;

        await Task.Run(() => command.RaiseCanExecuteChanged());

        Assert.True(eventRaised);
    }

    [Fact]
    public async Task RelayCommand_RaiseCanExecuteChanged_CanBeCalledFromBackgroundThread()
    {
        var command = new RelayCommand(_ => { });
        var eventRaised = false;
        command.CanExecuteChanged += (_, _) => eventRaised = true;

        await Task.Run(() => command.RaiseCanExecuteChanged());

        Assert.True(eventRaised);
    }
}
