namespace ZprtNextGen.Core.Settings;

/// <summary>Преобразование старого плоского settings.json в config.json v2.</summary>
public static class ConfigMigration
{
    /// <summary>
    /// Существующая установка после миграции остаётся в ручном режиме:
    /// нельзя молча заменить уже выбранный пользователем обход.
    /// </summary>
    public static AppConfig FromLegacy(AppSettings legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        return new AppConfig
        {
            Engine = new EngineSettings
            {
                Path = string.IsNullOrWhiteSpace(legacy.EnginePath) ? null : legacy.EnginePath,
            },
            Connection = new ConnectionSettings
            {
                Mode = ConnectionMode.Manual,
                ManualPresetId = string.IsNullOrWhiteSpace(legacy.PresetId) ? "general" : legacy.PresetId,
            },
            GameFilter = legacy.GameFilter,
            Behavior = new BehaviorSettings
            {
                Autostart = legacy.Autostart,
                MinimizeToTray = legacy.MinimizeToTray,
                ReduceMotion = legacy.ReduceMotion,
                Theme = string.IsNullOrWhiteSpace(legacy.Theme) ? "dark" : legacy.Theme,
            },
            Ui = new UiSettings
            {
                CollapsedGroups = legacy.CollapsedGroups ?? [],
            },
        };
    }
}
