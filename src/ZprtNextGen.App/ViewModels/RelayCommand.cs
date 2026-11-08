#nullable enable

using System;
using System.Windows.Input;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Команда WPF с синхронным выполнением и проверкой возможности выполнения.</summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Создать команду.</summary>
    /// <param name="execute">Действие выполнения.</param>
    /// <param name="canExecute">Проверка возможности выполнения (может быть <c>null</c>).</param>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>Событие изменения возможности выполнения.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Признак возможности выполнения.</summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>Выполнить команду.</summary>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Уведомить подписчики об изменении <see cref="ICommand.CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
