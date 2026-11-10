#nullable enable

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Строка результата проверки связности для отображения в UI.</summary>
public class ConnectivityRowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _targetId = string.Empty;
    private string _name = string.Empty;
    private bool _success;
    private long _elapsedMs;
    private bool _timedOut;
    private string? _error;

    /// <summary>Идентификатор цели проверки (например, "discord").</summary>
    public string TargetId
    {
        get => _targetId;
        set => SetField(ref _targetId, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool Success
    {
        get => _success;
        set
        {
            if (SetField(ref _success, value))
                OnPropertyChanged(nameof(LatencyText));
        }
    }

    /// <summary>Измеренная задержка ответа (для успешных проверок).</summary>
    public long ElapsedMs
    {
        get => _elapsedMs;
        set
        {
            if (SetField(ref _elapsedMs, value))
                OnPropertyChanged(nameof(LatencyText));
        }
    }

    /// <summary>Проверка закончилась таймаутом: <see cref="ElapsedMs"/> — это ожидание, а не задержка.</summary>
    public bool TimedOut
    {
        get => _timedOut;
        set
        {
            if (SetField(ref _timedOut, value))
                OnPropertyChanged(nameof(LatencyText));
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetField(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>Задержка для UI: «N мс», «таймаут» или «—» (нет измерения).</summary>
    public string LatencyText => Success ? $"{_elapsedMs} мс" : _timedOut ? "таймаут" : "—";

    public ConnectivityRowViewModel()
    {
    }

    public ConnectivityRowViewModel(string targetId, string name, bool success, long elapsedMs, string? error)
        : this(targetId, name, success, elapsedMs, error, timedOut: false)
    {
    }

    public ConnectivityRowViewModel(
        string targetId, string name, bool success, long elapsedMs, string? error, bool timedOut)
    {
        TargetId = targetId;
        Name = name;
        Success = success;
        ElapsedMs = elapsedMs;
        Error = error;
        TimedOut = timedOut;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
