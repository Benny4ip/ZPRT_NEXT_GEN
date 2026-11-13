namespace ZprtNextGen.Core.Connection;

/// <summary>
/// <see cref="IProgress{T}"/>, который вызывает обработчик синхронно в потоке отчёта.
/// Нужен там, где важен порядок событий (прогресс теста, автоподбор).
/// </summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public void Report(T value) => _handler(value);
}
