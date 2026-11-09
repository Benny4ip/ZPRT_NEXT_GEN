#nullable enable

namespace ZprtNextGen.App.ViewModels;

/// <summary>Строка результата проверки связности для отображения в UI.</summary>
public class ConnectivityRowViewModel
{
    /// <summary>Идентификатор цели проверки (например, "discord").</summary>
    public string TargetId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Success { get; set; }

    public long ElapsedMs { get; set; }

    public string? Error { get; set; }

    public ConnectivityRowViewModel()
    {
    }

    public ConnectivityRowViewModel(string targetId, string name, bool success, long elapsedMs, string? error)
    {
        TargetId = targetId;
        Name = name;
        Success = success;
        ElapsedMs = elapsedMs;
        Error = error;
    }
}
