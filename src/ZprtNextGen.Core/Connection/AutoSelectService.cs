namespace ZprtNextGen.Core.Connection;

using ZprtNextGen.Core.Presets;

/// <summary>Прогресс автоподбора.</summary>
public sealed record AutoSelectProgress(
    int TestedCount,
    int TotalCount,
    PresetTestResult? LastResult,
    PresetTestResult? BestResult,
    bool HasQualifiedCandidate);

/// <summary>Итог автоподбора.</summary>
public sealed record AutoSelectOutcome(
    IReadOnlyList<PresetTestResult> Results,
    PresetTestResult? Best,
    bool Canceled);

/// <summary>Перебор всех пресетов и выбор лучшего рабочего.</summary>
public interface IAutoSelectService
{
    Task<AutoSelectOutcome> TestAsync(
        IReadOnlyList<Preset> presets,
        string engineRoot,
        GameFilterMode filter,
        IProgress<AutoSelectProgress>? progress,
        CancellationToken ct);
}

/// <summary>
/// Прогоняет пресеты через <see cref="IPresetTester"/> и выбирает рабочий
/// (YouTube и Discord) с минимальной худшей задержкой. При отмене возвращает
/// уже проверенные результаты, чтобы UI мог применить лучший досрочно.
/// </summary>
public sealed class AutoSelectService(IPresetTester tester) : IAutoSelectService
{
    private readonly IPresetTester _tester = tester ?? throw new ArgumentNullException(nameof(tester));

    public async Task<AutoSelectOutcome> TestAsync(
        IReadOnlyList<Preset> presets,
        string engineRoot,
        GameFilterMode filter,
        IProgress<AutoSelectProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineRoot);

        var completed = new List<PresetTestResult>();
        PresetTestResult? best = null;

        var reporter = new InlineProgress<PresetTestProgress>(report =>
        {
            if (report.Result is not null)
            {
                completed.Add(report.Result);
                best = PickBest(completed);
            }

            progress?.Report(new AutoSelectProgress(
                report.Index,
                report.Total,
                report.Result,
                best,
                best is not null));
        });

        try
        {
            var results = await _tester
                .TestAllAsync(presets, engineRoot, filter, reporter, ct)
                .ConfigureAwait(false);

            return new AutoSelectOutcome(results, PickBest(results), Canceled: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new AutoSelectOutcome(completed.ToList(), PickBest(completed), Canceled: true);
        }
    }

    /// <summary>Лучший рабочий результат: меньший score, затем меньшая сумма, затем id.</summary>
    public static PresetTestResult? PickBest(IEnumerable<PresetTestResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .Where(result => result.Qualified)
            .OrderBy(result => result.ScoreMs ?? long.MaxValue)
            .ThenBy(result => Sum(result))
            .ThenBy(result => result.PresetId, PresetNaming.IdComparer)
            .FirstOrDefault();
    }

    private static long Sum(PresetTestResult result) =>
        (result.YoutubeMs ?? long.MaxValue / 4) + (result.DiscordMs ?? long.MaxValue / 4);
}
