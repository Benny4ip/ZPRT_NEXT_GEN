namespace ZprtNextGen.Core.Process;

/// <summary>Статус управляемого процесса движка (winws).</summary>
public enum EngineStatus
{
    /// <summary>Процесс остановлен.</summary>
    Stopped,

    /// <summary>Запуск процесса начат.</summary>
    Starting,

    /// <summary>Процесс запущен и работает.</summary>
    Running,

    /// <summary>Остановка процесса начата.</summary>
    Stopping,

    /// <summary>Процесс завершился с ошибкой.</summary>
    Error
}
