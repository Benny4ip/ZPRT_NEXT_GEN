#nullable enable

using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Команда WPF с асинхронным выполнением и отслеживанием занятости.</summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Создать команду.</summary>
    /// <param name="execute">Асинхронное действие выполнения.</param>
    /// <param name="canExecute">Проверка возможности выполнения (может быть <c>null</c>).</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
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
            await _execute();
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Уведомить подписчики об изменении <see cref="ICommand.CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
