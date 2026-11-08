namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Диспетчер UI, выполняющий действия синхронно inline.</summary>
public sealed class ImmediateDispatcher : ZprtNextGen.App.ViewModels.IUiDispatcher
{
    public void Post(System.Action action) => action();
}
