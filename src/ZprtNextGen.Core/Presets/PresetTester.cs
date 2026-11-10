using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Process;

namespace ZprtNextGen.Core.Presets;

/// <summary>Результат проверки одного пресета.</summary>
/// <param name="ElapsedMs">Измеренная HTTP-задержка успешного обхода, либо <c>null</c>,
/// если задержку измерить не удалось (таймаут/ошибка запуска).</param>
/// <param name="TimedOut"><c>true</c>, если проверка закончилась таймаутом, а не измеренной задержкой.</param>
public record PresetTestResult(
    string PresetId, string Name, bool Success, long? ElapsedMs, string? Detail, bool TimedOut = false)
{
    /// <summary>Краткий текст результата для отображения в UI.</summary>
    public string StatusText => Success ? "Успех" : $"Ошибка: {Detail ?? "неизвестная"}";

    /// <summary>Задержка для UI: «N мс», «таймаут» или «—» (нет измерения).</summary>
    public string LatencyText => Success && ElapsedMs is { } ms
        ? $"{ms} мс"
        : TimedOut ? "таймаут" : "—";
}

/// <summary>Прогресс прогона всех пресетов.</summary>
public record PresetTestProgress(int Index, int Total, Preset Preset, PresetTestResult? Result);

/// <summary>Проверка всех пресетов последовательно: старт → пауза → проверка связи → остановка.</summary>
public interface IPresetTester
{
    Task<IReadOnlyList<PresetTestResult>> TestAllAsync(IReadOnlyList<Preset> presets, string engineRoot,
        GameFilterMode filter, IProgress<PresetTestProgress>? progress, CancellationToken ct);
}

/// <summary>Реализация <see cref="IPresetTester"/>.</summary>
public sealed class PresetTester : IPresetTester
{
    private readonly IEngineProcessManager _engine;
    private readonly IConnectivityService _connectivity;
    private readonly TimeSpan _startupDelay;

    public PresetTester(IEngineProcessManager engine, IConnectivityService connectivity, TimeSpan? startupDelay = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        _startupDelay = startupDelay ?? TimeSpan.FromSeconds(2);
    }

    public async Task<IReadOnlyList<PresetTestResult>> TestAllAsync(IReadOnlyList<Preset> presets, string engineRoot,
        GameFilterMode filter, IProgress<PresetTestProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineRoot);

        var bin = Path.Combine(engineRoot, "bin");
        var results = new List<PresetTestResult>(presets.Count);

        for (var i = 0; i < presets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var preset = presets[i];
            var result = new PresetTestResult(preset.Id, preset.Name, false, null, null);

            try
            {
                if (_engine.IsForeignInstanceRunning())
                {
                    result = new PresetTestResult(
                        preset.Id, preset.Name, false, null,
                        "Обход уже запущен извне. Остановите его перед тестированием пресетов.");
                    results.Add(result);
                    progress?.Report(new PresetTestProgress(i + 1, presets.Count, preset, result));
                    continue;
                }

                var arguments = PresetArgumentBuilder.Build(preset, engineRoot, filter);
                await _engine.StartAsync(Path.Combine(bin, "winws.exe"), arguments, bin, ct);

                await Task.Delay(_startupDelay, ct);

                if (_engine.Status != EngineStatus.Running)
                {
                    result = new PresetTestResult(
                        preset.Id, preset.Name, false, null,
                        "Движок (winws) завершился сразу после запуска. Проверьте права администратора, WinDivert/драйвер, файлы списков и аргументы пресета.");
                }
                else
                {
                    var report = await _connectivity.CheckAsync(ct);
                    ct.ThrowIfCancellationRequested();

                    if (report.IsConnected)
                    {
                        // Задержка — реальное время ответа лучшей первичной цели, а не длительность паузы/таймаута.
                        var latency = report.Results
                            .Where(r => r.Success && r.Target.IsPrimary)
                            .Select(r => r.ElapsedMs)
                            .DefaultIfEmpty(0)
                            .Min();
                        result = new PresetTestResult(preset.Id, preset.Name, true, latency, null);
                    }
                    else
                    {
                        result = new PresetTestResult(
                            preset.Id, preset.Name, false, null, "Связь не подтверждена",
                            report.Results.Any(r => r.TimedOut));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new PresetTestResult(preset.Id, preset.Name, false, null, ex.Message);
            }
            finally
            {
                try
                {
                    // Останавливаем при любом незавершённом состоянии (Running/Starting/Stopping/Error),
                    // иначе процесс winws может остаться висеть после теста.
                    if (_engine.Status != EngineStatus.Stopped)
                        await _engine.StopAsync(CancellationToken.None);
                }
                catch
                {
                    // Лучшая попытка остановить движок между пресетами.
                }
            }

            results.Add(result);
            progress?.Report(new PresetTestProgress(i + 1, presets.Count, preset, result));
        }

        return results;
    }
}
