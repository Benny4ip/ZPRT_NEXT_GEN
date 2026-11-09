namespace ZprtNextGen.Core.Presentation;

/// <summary>Целевое заполнение кольца-прогресса вокруг линии пульса.</summary>
public static class MeditationProgress
{
    /// <summary>
    /// Заполнение кольца: остановлено — 0, подключение — 0.85,
    /// успех — 1, ошибка — 0.5.
    /// </summary>
    public static double Resolve(MeditationState state) => state switch
    {
        MeditationState.Connected => 1.0,
        MeditationState.Error => 0.5,
        MeditationState.Connecting => 0.85,
        _ => 0.0,
    };
}
