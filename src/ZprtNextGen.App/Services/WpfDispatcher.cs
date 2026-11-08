#nullable enable

using System;
using System.Windows;
using ZprtNextGen.App.ViewModels;

namespace ZprtNextGen.App.Services;

/// <summary>
/// Реализация <see cref="ZprtNextGen.App.ViewModels.IUiDispatcher"/> через
/// <see cref="Application.Current"/>.Dispatcher.BeginInvoke.
/// </summary>
public sealed class WpfDispatcher : IUiDispatcher
{
    /// <inheritdoc/>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var application = Application.Current;
        if (application is not null && application.Dispatcher is not null)
            application.Dispatcher.BeginInvoke(action);
        else
            action();
    }
}
