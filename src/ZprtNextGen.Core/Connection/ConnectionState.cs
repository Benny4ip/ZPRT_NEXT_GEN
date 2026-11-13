namespace ZprtNextGen.Core.Connection;

/// <summary>Состояние подключения, отображаемое светофором.</summary>
public enum ConnectionState
{
    Disconnected,
    Selecting,
    Connecting,
    Connected,
    Stopping,
    Error,
}

/// <summary>Причина проблемного состояния.</summary>
public enum ConnectionIssue
{
    None,
    EngineMissing,
    ForeignInstance,
    NoInternet,
    AllPresetsFailed,
    SavedProfileFailed,
}

/// <summary>Снимок состояния для UI.</summary>
public sealed record ConnectionSnapshot(
    ConnectionState State,
    ConnectionIssue Issue,
    string? PresetId,
    string? PresetName,
    long? LatencyMs,
    long? YoutubeMs,
    long? DiscordMs,
    int TestedCount,
    int TotalCount,
    double Progress,
    string? BestPresetName,
    long? BestLatencyMs,
    bool HasQualifiedCandidate,
    string? ErrorMessage,
    bool IsAutoMode)
{
    /// <summary>Идёт длительная операция: тест, подключение или остановка.</summary>
    public bool IsBusy => State is ConnectionState.Selecting or ConnectionState.Connecting or ConnectionState.Stopping;
}

/// <summary>Параметры автоподбора и фоновой проверки.</summary>
public sealed record AutoSelectPolicy
{
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan VerifyDelay { get; init; } = TimeSpan.FromMilliseconds(1500);
    public int MaxStartAttempts { get; init; } = 3;
    public int HealthFailures { get; init; } = 2;
    public TimeSpan HealthCooldown { get; init; } = TimeSpan.FromMinutes(5);
}
