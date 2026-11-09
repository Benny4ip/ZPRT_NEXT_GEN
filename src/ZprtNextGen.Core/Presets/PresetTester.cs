using System.Diagnostics;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Process;

namespace ZprtNextGen.Core.Presets;

/// <summary>Результат проверки одного пресета.</summary>
public record PresetTestResult(string PresetId, string Name, bool Success, long ElapsedMs, string? Detail)
{
    /// <summary>Краткий текст результата для отображения в UI.</summary>
    public string StatusText => Success ? "Успех" : $"Ошибка: {Detail ?? "неизвестная"}";
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
            var stopwatch = Stopwatch.StartNew();
            var result = new PresetTestResult(preset.Id, preset.Name, false, 0, null);

            try
            {
                var arguments = PresetArgumentBuilder.Build(preset, engineRoot, filter);
                await _engine.StartAsync(Path.Combine(bin, "winws.exe"), arguments, bin, ct);

                await Task.Delay(_startupDelay, ct);

                if (_engine.Status != EngineStatus.Running)
                {
                    result = new PresetTestResult(
                        preset.Id, preset.Name, false, stopwatch.ElapsedMilliseconds,
                        "Движок (winws) завершился сразу после запуска. Проверьте права администратора, WinDivert/драйвер, файлы списков и аргументы пресета.");
                }
                else
                {
                    var report = await _connectivity.CheckAsync(ct);
                    result = new PresetTestResult(
                        preset.Id, preset.Name, report.IsConnected, stopwatch.ElapsedMilliseconds,
                        report.IsConnected ? null : "Связь не подтверждена");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new PresetTestResult(preset.Id, preset.Name, false, stopwatch.ElapsedMilliseconds, ex.Message);
            }
            finally
            {
                try
                {
                    if (_engine.Status == EngineStatus.Running)
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
