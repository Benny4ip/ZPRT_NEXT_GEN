#nullable enable

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Команда WPF с асинхронным выполнением и отслеживанием занятости.</summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Создать команду.</summary>
    /// <param name="execute">Асинхронное действие выполнения (получает параметр команды).</param>
    /// <param name="canExecute">Проверка возможности выполнения (может быть <c>null</c>).</param>
    public AsyncRelayCommand(Func<object?, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>Событие изменения возможности выполнения.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Признак того, что выполнение уже идёт.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>Признак возможности выполнения.</summary>
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    /// <summary>Выполнить команду.</summary>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        try
        {
            _isRunning = true;
            RaiseCanExecuteChanged();
            await _execute(parameter);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Уведомить подписчики об изменении <see cref="ICommand.CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged()
    {
        var handler = CanExecuteChanged;
        if (handler is null)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(handler, this, EventArgs.Empty);
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }
}
