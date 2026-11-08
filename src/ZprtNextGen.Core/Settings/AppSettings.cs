namespace ZprtNextGen.Core.Settings;

using ZprtNextGen.Core.Presets;

/// <summary>
/// Пользовательские настройки оболочки ZPRT_NEXT_GEN.
/// </summary>
public record AppSettings
{
    /// <summary>Путь к исполняемому файлу winws.exe (движок).</summary>
    public string? EnginePath { get; init; }

    /// <summary>Идентификатор активного пресета.</summary>
    public string PresetId { get; init; } = "general";

    /// <summary>Режим игрового фильтра winws.</summary>
    public GameFilterMode GameFilter { get; init; } = GameFilterMode.Disabled;

    /// <summary>Запускать оболочку вместе с системой.</summary>
    public bool Autostart { get; init; }

    /// <summary>Сворачивать в трей вместо закрытия.</summary>
    public bool MinimizeToTray { get; init; } = true;

    /// <summary>Тема оформления ("dark" / "light").</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Снижать анимацию интерфейса.</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>Идентификаторы свёрнутых групп пресетов.</summary>
    public IReadOnlyList<string> CollapsedGroups { get; init; } = [];
}
