namespace ZprtNextGen.Core.Settings;

using System.Text.Json;
using System.Text.Json.Serialization;
using ZprtNextGen.Core.Presets;

/// <summary>Хранилище конфигурации приложения (config.json v2).</summary>
public interface IConfigStore
{
    /// <summary>Путь к config.json.</summary>
    string Path { get; }

    /// <summary>
    /// Загрузить конфиг. Если config.json отсутствует, но есть settings.json,
    /// выполняется миграция. <paramref name="knownPresetIds"/> позволяет отбросить
    /// авто-профиль, ссылающийся на исчезнувший пресет.
    /// </summary>
    AppConfig Load(IReadOnlyCollection<string>? knownPresetIds = null);

    /// <summary>Атомарно сохранить конфиг.</summary>
    void Save(AppConfig config);
}

/// <summary>Файловое хранилище config.json с миграцией старого settings.json.</summary>
public sealed class ConfigStore : IConfigStore
{
    /// <summary>Текущая версия схемы конфига.</summary>
    public const int CurrentSchemaVersion = AppConfig.CurrentSchemaVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new TolerantEnumConverter<ConnectionMode>(),
            new TolerantEnumConverter<GameFilterMode>(),
        },
    };

    private readonly object _saveLock = new();
    private readonly string? _legacyPath;

    public ConfigStore(string path, string? legacyPath = null)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _legacyPath = legacyPath;
    }

    /// <summary>Путь по умолчанию: %APPDATA%\ZPRT_NEXT_GEN\config.json.</summary>
    public static ConfigStore CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = System.IO.Path.Combine(appData, "ZPRT_NEXT_GEN");
        return new ConfigStore(
            System.IO.Path.Combine(directory, "config.json"),
            System.IO.Path.Combine(directory, "settings.json"));
    }

    public string Path { get; }

    public AppConfig Load(IReadOnlyCollection<string>? knownPresetIds = null)
    {
        if (File.Exists(Path))
            return LoadExisting(knownPresetIds);

        var migrated = TryMigrateLegacy();
        if (migrated is null)
            return Normalize(new AppConfig(), knownPresetIds);

        var normalized = Normalize(migrated, knownPresetIds);

        try
        {
            Save(normalized);
            BackupLegacy();
        }
        catch
        {
            // Даже если записать не удалось, приложение должно продолжить работу.
        }

        return normalized;
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_saveLock)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(config, JsonOptions);
            var tempFile = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, Path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* лучшая попытка */ }
                throw;
            }
        }
    }

    /// <summary>Привести конфиг к допустимым значениям.</summary>
    public static AppConfig Normalize(AppConfig config, IReadOnlyCollection<string>? knownPresetIds = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var engine = config.Engine ?? new EngineSettings();
        var connection = config.Connection ?? new ConnectionSettings();
        var health = connection.HealthCheck ?? new HealthCheckSettings();
        var behavior = config.Behavior ?? new BehaviorSettings();
        var updates = config.Updates ?? new UpdateSettings();
        var ui = config.Ui ?? new UiSettings();

        var autoProfile = NormalizeAutoProfile(connection.AutoProfile, knownPresetIds);
        var customPresets = NormalizeCustomPresets(config.CustomPresets);

        return config with
        {
            SchemaVersion = CurrentSchemaVersion,
            Engine = new EngineSettings
            {
                Path = string.IsNullOrWhiteSpace(engine.Path) ? null : engine.Path.Trim(),
            },
            Connection = new ConnectionSettings
            {
                Mode = Enum.IsDefined(connection.Mode) ? connection.Mode : ConnectionMode.Auto,
                ManualPresetId = string.IsNullOrWhiteSpace(connection.ManualPresetId)
                    ? "general"
                    : connection.ManualPresetId.Trim(),
                AutoProfile = autoProfile,
                HealthCheck = new HealthCheckSettings(
                    Enabled: health.Enabled,
                    IntervalSeconds: health.IntervalSeconds <= 0
                        ? 60
                        : Math.Clamp(health.IntervalSeconds, 10, 3600),
                    FailuresBeforeRetest: health.FailuresBeforeRetest <= 0
                        ? 2
                        : Math.Clamp(health.FailuresBeforeRetest, 1, 10),
                    RetestCooldownSeconds: health.RetestCooldownSeconds < 0
                        ? 300
                        : Math.Clamp(health.RetestCooldownSeconds, 0, 86400)),
            },
            CustomPresets = customPresets,
            GameFilter = Enum.IsDefined(config.GameFilter) ? config.GameFilter : GameFilterMode.Disabled,
            Behavior = new BehaviorSettings
            {
                Autostart = behavior.Autostart,
                MinimizeToTray = behavior.MinimizeToTray,
                ReduceMotion = behavior.ReduceMotion,
                Theme = string.IsNullOrWhiteSpace(behavior.Theme) ? "dark" : behavior.Theme.Trim(),
            },
            Updates = new UpdateSettings { AutoUpdateEngine = updates.AutoUpdateEngine },
            Ui = new UiSettings
            {
                CollapsedGroups = (ui.CollapsedGroups ?? [])
                    .Where(group => !string.IsNullOrWhiteSpace(group))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
            },
        };
    }

    private AppConfig LoadExisting(IReadOnlyCollection<string>? knownPresetIds)
    {
        try
        {
            var json = File.ReadAllText(Path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null)
            {
                MoveAside(".bad");
                return Normalize(new AppConfig(), knownPresetIds);
            }

            if (config.SchemaVersion > CurrentSchemaVersion)
            {
                MoveAside(".bak");
                return Normalize(new AppConfig(), knownPresetIds);
            }

            return Normalize(config, knownPresetIds);
        }
        catch (JsonException)
        {
            MoveAside(".bad");
            return Normalize(new AppConfig(), knownPresetIds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Normalize(new AppConfig(), knownPresetIds);
        }
    }

    private AppConfig? TryMigrateLegacy()
    {
        if (string.IsNullOrWhiteSpace(_legacyPath) || !File.Exists(_legacyPath))
            return null;

        try
        {
            var json = File.ReadAllText(_legacyPath);
            var legacy = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return legacy is null ? null : ConfigMigration.FromLegacy(legacy);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void BackupLegacy()
    {
        if (string.IsNullOrWhiteSpace(_legacyPath) || !File.Exists(_legacyPath))
            return;

        try
        {
            File.Copy(_legacyPath, _legacyPath + ".bak", overwrite: true);
        }
        catch
        {
            // Резервная копия — вспомогательная операция, не ломаем запуск.
        }
    }

    private void MoveAside(string extension)
    {
        try
        {
            if (File.Exists(Path))
                File.Move(Path, Path + extension, overwrite: true);
        }
        catch
        {
            // Неудача переименования не должна ломать запуск.
        }
    }

    private static AutoProfile? NormalizeAutoProfile(
        AutoProfile? profile, IReadOnlyCollection<string>? knownPresetIds)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.PresetId))
            return null;

        if (knownPresetIds is not null &&
            !knownPresetIds.Contains(profile.PresetId, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return profile with
        {
            Name = profile.Name ?? string.Empty,
            LatencyMs = profile.LatencyMs < 0 ? 0 : profile.LatencyMs,
            EngineVersion = profile.EngineVersion ?? string.Empty,
            PresetArgsHash = profile.PresetArgsHash ?? string.Empty,
        };
    }

    private static List<CustomPreset> NormalizeCustomPresets(IReadOnlyList<CustomPreset>? presets)
    {
        var result = new List<CustomPreset>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in presets ?? [])
        {
            if (preset is null)
                continue;

            var id = preset.Id?.Trim() ?? string.Empty;
            var name = preset.Name?.Trim() ?? string.Empty;
            var args = preset.Args?.Trim() ?? string.Empty;

            if (!id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Length == 0 || args.Length == 0)
                continue;
            if (!ids.Add(id))
                continue;

            result.Add(new CustomPreset(id, name, preset.Description?.Trim() ?? string.Empty, args));
        }

        return result;
    }
}
