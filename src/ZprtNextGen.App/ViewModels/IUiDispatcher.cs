#nullable enable

namespace ZprtNextGen.App.ViewModels;

/// <summary>Диспетчер действий в потоке UI.</summary>
public interface IUiDispatcher
{
    /// <summary>Выполнить <paramref name="action"/> в потоке UI.</summary>
    void Post(Action action);
}
