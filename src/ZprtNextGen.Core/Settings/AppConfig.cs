namespace ZprtNextGen.Core.Settings;

using ZprtNextGen.Core.Presets;

/// <summary>Режим выбора обхода: автоматический подбор или ручной пресет.</summary>
public enum ConnectionMode
{
    /// <summary>Автоматически подбирать лучший обход.</summary>
    Auto,
    /// <summary>Использовать выбранный пользователем пресет.</summary>
    Manual,
}

/// <summary>Настройки движка.</summary>
public sealed record EngineSettings
{
    /// <summary>Путь к движку. <c>null</c> — искать автоматически.</summary>
    public string? Path { get; init; }
}

/// <summary>Пороги фоновой проверки соединения.</summary>
public sealed record HealthCheckSettings(
    bool Enabled = true,
    int IntervalSeconds = 60,
    int FailuresBeforeRetest = 2,
    int RetestCooldownSeconds = 300);

/// <summary>Последний рабочий профиль, найденный автоподбором.</summary>
public sealed record AutoProfile(
    string PresetId,
    string Name,
    long LatencyMs,
    long? YoutubeMs,
    long? DiscordMs,
    DateTimeOffset TestedAtUtc,
    string EngineVersion,
    string PresetArgsHash);

/// <summary>Настройки подключения.</summary>
public sealed record ConnectionSettings
{
    public ConnectionMode Mode { get; init; } = ConnectionMode.Auto;
    public string ManualPresetId { get; init; } = "general";
    public AutoProfile? AutoProfile { get; init; }
    public HealthCheckSettings HealthCheck { get; init; } = new();
}

/// <summary>Пользовательский пресет, хранящийся в конфиге.</summary>
public sealed record CustomPreset(string Id, string Name, string Description, string Args);

/// <summary>Поведение приложения: автозапуск, трей, анимации.</summary>
public sealed record BehaviorSettings
{
    public bool Autostart { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public string Theme { get; init; } = "dark";
}

/// <summary>Настройки обновлений.</summary>
public sealed record UpdateSettings
{
    public bool AutoUpdateEngine { get; init; } = true;
}

/// <summary>Состояние интерфейса, которое переживает перезапуск.</summary>
public sealed record UiSettings
{
    public IReadOnlyList<string> CollapsedGroups { get; init; } = [];
}

/// <summary>Полная конфигурация приложения (config.json, schemaVersion 2).</summary>
public sealed record AppConfig
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public EngineSettings Engine { get; init; } = new();
    public ConnectionSettings Connection { get; init; } = new();
    public IReadOnlyList<CustomPreset> CustomPresets { get; init; } = [];
    public GameFilterMode GameFilter { get; init; } = GameFilterMode.Disabled;
    public BehaviorSettings Behavior { get; init; } = new();
    public UpdateSettings Updates { get; init; } = new();
    public UiSettings Ui { get; init; } = new();
}
