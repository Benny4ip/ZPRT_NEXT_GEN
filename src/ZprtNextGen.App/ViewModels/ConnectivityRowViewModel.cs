#nullable enable

namespace ZprtNextGen.App.ViewModels;

/// <summary>Строка результата проверки связности для отображения в UI.</summary>
public class ConnectivityRowViewModel
{
    public string Name { get; set; } = string.Empty;

    public bool Success { get; set; }

    public long ElapsedMs { get; set; }

    public string? Error { get; set; }

    public ConnectivityRowViewModel()
    {
    }

    public ConnectivityRowViewModel(string name, bool success, long elapsedMs, string? error)
    {
        Name = name;
        Success = success;
        ElapsedMs = elapsedMs;
        Error = error;
    }
}
