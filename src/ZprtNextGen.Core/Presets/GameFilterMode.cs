namespace ZprtNextGen.Core.Presets;

/// <summary>
/// Режим игрового фильтра winws: какие диапазоны портов подставляются вместо
/// плейсхолдеров {{GameFilterTCP}} и {{GameFilterUDP}} в аргументах пресета.
/// </summary>
public enum GameFilterMode
{
    /// <summary>Фильтр выключен: и TCP, и UDP получают "12" (заглушка).</summary>
    Disabled,
    /// <summary>Только TCP получает реальный диапазон, UDP — "12".</summary>
    Tcp,
    /// <summary>Только UDP получает реальный диапазон, TCP — "12".</summary>
    Udp,
    /// <summary>Оба протокола получают реальный диапазон.</summary>
    All
}
