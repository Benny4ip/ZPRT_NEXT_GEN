#nullable enable

using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Process;

namespace ZprtNextGen.Core.Presentation;

/// <summary>Состояние анимации медитации.</summary>
public enum MeditationState
{
    /// <summary>Движок остановлен, анимация выключена.</summary>
    Stopped,

    /// <summary>Движок запускается или останавливается.</summary>
    Connecting,

    /// <summary>Движок работает и связь установлена.</summary>
    Connected,

    /// <summary>Движок или связь в ошибке.</summary>
    Error
}

/// <summary>Разрешает состояние анимации медитации по статусу движка и отчёту связности.</summary>
public static class AnimationStateResolver
{
    /// <summary>Разрешить состояние анимации.</summary>
    /// <param name="status">Статус управляемого процесса движка (winws).</param>
    /// <param name="report">Отчёт проверки доступности, может быть <c>null</c>.</param>
    public static MeditationState Resolve(EngineStatus status, ConnectivityReport? report)
    {
        return status switch
        {
            EngineStatus.Stopped => MeditationState.Stopped,
            EngineStatus.Error => MeditationState.Error,
            EngineStatus.Starting or EngineStatus.Stopping => MeditationState.Connecting,
            EngineStatus.Running => report is null
                ? MeditationState.Connecting
                : report.IsConnected ? MeditationState.Connected : MeditationState.Error,
            _ => MeditationState.Stopped
        };
    }
}
