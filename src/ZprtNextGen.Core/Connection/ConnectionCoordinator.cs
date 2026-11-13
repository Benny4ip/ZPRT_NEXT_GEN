namespace ZprtNextGen.Core.Connection;

using System.Security.Cryptography;
using System.Text;
using ZprtNextGen.Core.Diagnostics;
using ZprtNextGen.Core.Engine;
using ZprtNextGen.Core.Logging;
using ZprtNextGen.Core.Presets;
using ZprtNextGen.Core.Process;
using ZprtNextGen.Core.Settings;

/// <summary>Зависимости <see cref="ConnectionCoordinator"/>.</summary>
public sealed record ConnectionCoordinatorDependencies(
    PresetCatalog Catalog,
    IConfigStore ConfigStore,
    IEngineProcessManager Engine,
    IConnectivityService Connectivity,
    IInternetProbe InternetProbe,
    IPresetTester PresetTester,
    IEngineOperationGate Gate,
    IAppLogger Logger,
    Func<EngineValidation> ResolveEngine,
    Func<CancellationToken, Task<bool>>? ConfirmStopForeign = null,
    AutoSelectPolicy? Policy = null);

/// <summary>
/// Единственный владелец переходов состояния подключения: автоподбор,
/// подключение сохранённого профиля, восстановление и фоновая проверка.
/// Не знает о WPF; UI подписывается на <see cref="StateChanged"/>.
/// </summary>
public sealed class ConnectionCoordinator
{
    private PresetCatalog _catalog;
    private readonly IConfigStore _configStore;
    private readonly IEngineProcessManager _engine;
    private readonly IConnectivityService _connectivity;
    private readonly IInternetProbe _internetProbe;
    private readonly IPresetTester _presetTester;
    private readonly IEngineOperationGate _gate;
    private readonly IAppLogger _logger;
    private readonly Func<EngineValidation> _resolveEngine;
    private readonly Func<CancellationToken, Task<bool>>? _confirmStopForeign;
    private readonly AutoSelectPolicy _policy;
    private readonly object _sync = new();
    private readonly AutoSelectService _autoSelect;

    private AppConfig _config = new();
    private ConnectionSnapshot _snapshot;
    private string? _engineRoot;
    private CancellationTokenSource? _selectCts;
    private bool _applyBestOnCancel;
    private int _healthFailures;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;

    public ConnectionCoordinator(ConnectionCoordinatorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _catalog = dependencies.Catalog ?? throw new ArgumentNullException(nameof(dependencies.Catalog));
        _configStore = dependencies.ConfigStore ?? throw new ArgumentNullException(nameof(dependencies.ConfigStore));
        _engine = dependencies.Engine ?? throw new ArgumentNullException(nameof(dependencies.Engine));
        _connectivity = dependencies.Connectivity ?? throw new ArgumentNullException(nameof(dependencies.Connectivity));
        _internetProbe = dependencies.InternetProbe ?? throw new ArgumentNullException(nameof(dependencies.InternetProbe));
        _presetTester = dependencies.PresetTester ?? throw new ArgumentNullException(nameof(dependencies.PresetTester));
        _gate = dependencies.Gate ?? throw new ArgumentNullException(nameof(dependencies.Gate));
        _logger = dependencies.Logger ?? throw new ArgumentNullException(nameof(dependencies.Logger));
        _resolveEngine = dependencies.ResolveEngine ?? throw new ArgumentNullException(nameof(dependencies.ResolveEngine));
        _confirmStopForeign = dependencies.ConfirmStopForeign;
        _policy = dependencies.Policy ?? new AutoSelectPolicy();
        _autoSelect = new AutoSelectService(_presetTester);
        _snapshot = CreateBaseSnapshot();
    }

    /// <summary>Актуальный снимок состояния.</summary>
    public ConnectionSnapshot Snapshot
    {
        get
        {
            lock (_sync) { return _snapshot; }
        }
    }

    /// <summary>Текущая конфигурация (после нормализации).</summary>
    public AppConfig Config => _config;

    /// <summary>Снимок состояния изменился.</summary>
    public event EventHandler<ConnectionSnapshot>? StateChanged;

    /// <summary>Пользовательское уведомление (balloon в трее).</summary>
    public event EventHandler<string>? Notification;

    /// <summary>Загрузить конфиг и окружение. Подключение не запускает.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var knownIds = _catalog.Presets.Select(preset => preset.Id).ToList();
        _config = _configStore.Load(knownIds);

        var validation = _resolveEngine();
        _engineRoot = validation.IsValid ? validation.EngineRoot : null;

        Publish(CreateBaseSnapshot());
        _logger.Info("Состояние подключения: " + Snapshot.State);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Применить новые настройки без перезапуска.</summary>
    public void ApplyConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var knownIds = _catalog.Presets.Select(preset => preset.Id).ToList();
        _config = ConfigStore.Normalize(config, knownIds);
        Publish(Snapshot with { IsAutoMode = _config.Connection.Mode == ConnectionMode.Auto });
    }

    /// <summary>
    /// Заменить каталог пресетов (например, после добавления пользовательских пресетов).
    /// Подключение не перезапускается: новый каталог используется при следующей операции.
    /// </summary>
    public void UpdateCatalog(PresetCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>Клик по светофору.</summary>
    public Task ToggleAsync(CancellationToken ct) => Snapshot.State switch
    {
        ConnectionState.Connected => DisconnectAsync(ct),
        ConnectionState.Disconnected => ConnectAsync(ct),
        ConnectionState.Error => ConnectAsync(ct),
        ConnectionState.Connecting => CancelSelectionAsync(ct),
        _ => Task.CompletedTask,
    };

    /// <summary>Подключиться: сохранённый профиль, ручной пресет или полный автоподбор.</summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            await using var lease = await _gate.AcquireAsync(ct).ConfigureAwait(false);
            await ConnectCoreAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopQuietAsync().ConfigureAwait(false);
            Publish(CreateBaseSnapshot());
        }
        catch (Exception ex)
        {
            _logger.Error("Подключение: " + ex.Message, ex);
            await StopQuietAsync().ConfigureAwait(false);
            PublishError(null, "Не удалось подключиться: " + ex.Message);
        }
    }

    /// <summary>Выключить обход.</summary>
    public async Task DisconnectAsync(CancellationToken ct)
    {
        try
        {
            await using var lease = await _gate.AcquireAsync(ct).ConfigureAwait(false);
            Publish(Snapshot with { State = ConnectionState.Stopping, ErrorMessage = null });

            await _engine.StopAsync(ct).ConfigureAwait(false);

            _healthFailures = 0;
            Publish(CreateBaseSnapshot());
        }
        catch (OperationCanceledException)
        {
            Publish(CreateBaseSnapshot());
        }
        catch (Exception ex)
        {
            _logger.Error("Отключение: " + ex.Message, ex);
            PublishError(null, "Не удалось остановить обход: " + ex.Message);
        }
    }

    /// <summary>Принудительный перебор заново, без сохранённого профиля.</summary>
    public async Task RetestAsync(CancellationToken ct)
    {
        try
        {
            await using var lease = await _gate.AcquireAsync(ct).ConfigureAwait(false);
            await StopQuietAsync().ConfigureAwait(false);
            await SelectAndConnectAsync(ct, savedProfileFailed: false)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopQuietAsync().ConfigureAwait(false);
            Publish(CreateBaseSnapshot());
        }
        catch (Exception ex)
        {
            _logger.Error("Повторный подбор: " + ex.Message, ex);
            PublishError(null, "Не удалось подобрать обход: " + ex.Message);
        }
    }

    /// <summary>«Остановить и применить лучший»: прекратить перебор и подключить найденное.</summary>
    public Task StopAndApplyBestAsync(CancellationToken ct)
    {
        _applyBestOnCancel = true;
        _selectCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>Отменить подбор и вернуться в «выключено».</summary>
    public Task CancelSelectionAsync(CancellationToken ct)
    {
        _applyBestOnCancel = false;
        _selectCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>Фоновая проверка: две неудачи подряд запускают восстановление.</summary>
    public async Task CheckHealthAsync(CancellationToken ct)
    {
        if (Snapshot.State != ConnectionState.Connected || _gate.IsBusy)
            return;

        if (DateTimeOffset.UtcNow < _cooldownUntil)
            return;

        try
        {
            await using var lease = await _gate.AcquireAsync(ct).ConfigureAwait(false);

            if (Snapshot.State != ConnectionState.Connected)
                return;

            var report = await _connectivity.CheckAsync(ct).ConfigureAwait(false);
            if (report.BothOk)
            {
                _healthFailures = 0;
                return;
            }

            _healthFailures++;
            if (_healthFailures < Math.Max(1, _policy.HealthFailures))
                return;

            _healthFailures = 0;

            if (_config.Connection.Mode == ConnectionMode.Manual)
            {
                Notify("Discord или YouTube перестали отвечать. Проверь обход.");
                return;
            }

            await RecoverAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Остановка монитора — не ошибка.
        }
        catch (Exception ex)
        {
            _logger.Error("Фоновая проверка: " + ex.Message, ex);
        }
    }

    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Snapshot.State == ConnectionState.Connected)
            return;

        if (_config.Connection.Mode == ConnectionMode.Manual)
        {
            await ConnectManualAsync(ct).ConfigureAwait(false);
            return;
        }

        var profile = _config.Connection.AutoProfile;
        if (profile is not null)
        {
            var preset = FindPreset(profile.PresetId);
            if (preset is null)
            {
                _logger.Info("Сохранённый пресет больше не существует — запускаем полный подбор.");
            }
            else
            {
                Publish(Snapshot with
                {
                    State = ConnectionState.Connecting,
                    Issue = ConnectionIssue.None,
                    PresetId = preset.Id,
                    PresetName = preset.Name,
                    ErrorMessage = null,
                });

                var report = await TryConnectPresetAsync(preset, attempts: 2, ct).ConfigureAwait(false);
                if (report is not null)
                {
                    OnConnected(preset, report);
                    return;
                }

                _logger.Info("Сохранённый обход не работает — запускаем полный подбор.");
            }
        }

        await SelectAndConnectAsync(ct, savedProfileFailed: profile is not null)
            .ConfigureAwait(false);
    }

    private async Task ConnectManualAsync(CancellationToken ct)
    {
        var validation = _resolveEngine();
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.EngineRoot))
        {
            PublishError(ConnectionIssue.EngineMissing,
                "Движок не найден. Укажи папку движка в настройках.");
            return;
        }

        _engineRoot = validation.EngineRoot;

        var preset = FindPreset(_config.Connection.ManualPresetId);
        if (preset is null)
        {
            PublishError(ConnectionIssue.AllPresetsFailed,
                "Выбранный пресет не найден. Выбери другой в настройках.");
            return;
        }

        Publish(Snapshot with
        {
            State = ConnectionState.Connecting,
            Issue = ConnectionIssue.None,
            PresetId = preset.Id,
            PresetName = preset.Name,
            ErrorMessage = null,
        });

        var report = await TryConnectPresetAsync(preset, attempts: 1, ct).ConfigureAwait(false);
        if (report is null)
        {
            PublishError(ConnectionIssue.AllPresetsFailed,
                "Пресет не запустился. Проверь права администратора и файлы движка.");
            return;
        }

        OnConnected(preset, report);
    }

    private async Task SelectAndConnectAsync(
        CancellationToken ct, bool savedProfileFailed)
    {
        if (!await _internetProbe.CheckInternetAsync(ct).ConfigureAwait(false))
        {
            PublishError(ConnectionIssue.NoInternet,
                "Интернет не работает. Проверь подключение и попробуй снова.");
            return;
        }

        var validation = _resolveEngine();
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.EngineRoot))
        {
            PublishError(ConnectionIssue.EngineMissing,
                "Движок не найден. Укажи папку движка в настройках.");
            return;
        }

        _engineRoot = validation.EngineRoot;

        if (_engine.IsForeignInstanceRunning())
        {
            var confirmed = _confirmStopForeign is not null
                && await _confirmStopForeign(ct).ConfigureAwait(false);

            if (!confirmed)
            {
                PublishError(ConnectionIssue.ForeignInstance,
                    "Обход уже запущен извне. Останови его и попробуй снова.");
                return;
            }

            _engine.KillForeignInstances();
        }

        var presets = _catalog.Presets
            .OrderBy(preset => preset.Id, PresetNaming.IdComparer)
            .ToList();

        Publish(Snapshot with
        {
            State = ConnectionState.Selecting,
            Issue = savedProfileFailed ? ConnectionIssue.SavedProfileFailed : ConnectionIssue.None,
            PresetId = null,
            PresetName = null,
            LatencyMs = null,
            YoutubeMs = null,
            DiscordMs = null,
            TestedCount = 0,
            TotalCount = presets.Count,
            Progress = 0,
            BestPresetName = null,
            BestLatencyMs = null,
            HasQualifiedCandidate = false,
            ErrorMessage = null,
        });

        _selectCts?.Dispose();
        _selectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _applyBestOnCancel = false;

        AutoSelectOutcome outcome;
        try
        {
            var progress = new InlineProgress<AutoSelectProgress>(item => Publish(Snapshot with
            {
                State = ConnectionState.Selecting,
                TestedCount = item.TestedCount,
                TotalCount = item.TotalCount,
                Progress = item.TotalCount == 0 ? 0 : item.TestedCount / (double)item.TotalCount,
                BestPresetName = item.BestResult?.Name,
                BestLatencyMs = item.BestResult?.ScoreMs,
                HasQualifiedCandidate = item.HasQualifiedCandidate,
            }));

            outcome = await _autoSelect.TestAsync(
                presets, _engineRoot, _config.GameFilter, progress, _selectCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _selectCts?.Dispose();
            _selectCts = null;
        }

        var applyBest = outcome.Canceled && _applyBestOnCancel;
        _applyBestOnCancel = false;

        if (outcome.Canceled && !applyBest)
        {
            Publish(CreateBaseSnapshot());
            return;
        }

        var best = outcome.Best;
        if (best is null)
        {
            PublishError(ConnectionIssue.AllPresetsFailed,
                "Не удалось найти рабочий обход. Проверь, что интернет работает, и попробуй снова.");
            return;
        }

        var candidates = outcome.Results
            .Where(result => result.Qualified)
            .OrderBy(result => result.ScoreMs ?? long.MaxValue)
            .ThenBy(result => (result.YoutubeMs ?? 0) + (result.DiscordMs ?? 0))
            .ThenBy(result => result.PresetId, PresetNaming.IdComparer)
            .Take(Math.Max(1, _policy.MaxStartAttempts))
            .ToList();

        foreach (var candidate in candidates)
        {
            var preset = FindPreset(candidate.PresetId);
            if (preset is null)
                continue;

            var report = await TryConnectPresetAsync(preset, attempts: 1, ct).ConfigureAwait(false);
            if (report is not null)
            {
                OnConnected(preset, report);
                return;
            }
        }

        PublishError(ConnectionIssue.AllPresetsFailed,
            "Не удалось подключить найденный обход. Попробуй ещё раз.");
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        _logger.Info("Сохранённый обход перестал работать — восстановление.");

        var profile = _config.Connection.AutoProfile;
        if (profile is not null)
        {
            var preset = FindPreset(profile.PresetId);
            if (preset is not null)
            {
                Publish(Snapshot with
                {
                    State = ConnectionState.Connecting,
                    Issue = ConnectionIssue.None,
                    PresetId = preset.Id,
                    PresetName = preset.Name,
                    ErrorMessage = null,
                });

                var report = await TryConnectPresetAsync(preset, attempts: 1, ct).ConfigureAwait(false);
                if (report is not null)
                {
                    OnConnected(preset, report);
                    return;
                }
            }
        }

        await SelectAndConnectAsync(ct, savedProfileFailed: true)
            .ConfigureAwait(false);

        if (Snapshot.State != ConnectionState.Connected)
        {
            _cooldownUntil = DateTimeOffset.UtcNow + _policy.HealthCooldown;
            Notify("Не удалось восстановить обход. Попробуй включить вручную.");
        }
    }

    private async Task<ConnectivityReport?> TryConnectPresetAsync(
        Preset preset, int attempts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_engineRoot))
            return null;

        var bin = Path.Combine(_engineRoot, "bin");
        var executable = Path.Combine(bin, "winws.exe");

        for (var attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var arguments = PresetArgumentBuilder.Build(preset, _engineRoot, _config.GameFilter);
                await _engine.StartAsync(executable, arguments, bin, ct).ConfigureAwait(false);
                await Task.Delay(_policy.StartupDelay, ct).ConfigureAwait(false);

                if (_engine.Status != EngineStatus.Running)
                {
                    _logger.Info($"Пресет «{preset.Name}» завершился сразу после запуска.");
                    await StopQuietAsync().ConfigureAwait(false);
                    continue;
                }

                var report = await _connectivity.CheckAsync(ct).ConfigureAwait(false);
                if (report.BothOk)
                    return report;

                await StopQuietAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await StopQuietAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Пресет «{preset.Name}»: {ex.Message}", ex);
                await StopQuietAsync().ConfigureAwait(false);
            }
        }

        return null;
    }

    private void OnConnected(Preset preset, ConnectivityReport report)
    {
        var score = report.ScoreMs ?? 0;

        if (_config.Connection.Mode == ConnectionMode.Auto)
        {
            var profile = new AutoProfile(
                preset.Id,
                preset.Name,
                score,
                report.YouTubeLatencyMs,
                report.DiscordLatencyMs,
                DateTimeOffset.UtcNow,
                _catalog.EngineVersion,
                HashArguments(preset.Arguments));

            SaveConfig(_config with
            {
                Connection = _config.Connection with { AutoProfile = profile },
            });
        }

        _healthFailures = 0;
        _cooldownUntil = DateTimeOffset.MinValue;

        Publish(Snapshot with
        {
            State = ConnectionState.Connected,
            Issue = ConnectionIssue.None,
            PresetId = preset.Id,
            PresetName = preset.Name,
            LatencyMs = score,
            YoutubeMs = report.YouTubeLatencyMs,
            DiscordMs = report.DiscordLatencyMs,
            HasQualifiedCandidate = false,
            ErrorMessage = null,
        });
    }

    private void SaveConfig(AppConfig config)
    {
        _config = config;
        try
        {
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            _logger.Error("Сохранение конфига: " + ex.Message, ex);
        }
    }

    private Preset? FindPreset(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            return null;

        return _catalog.Presets.FirstOrDefault(
            preset => preset.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task StopQuietAsync()
    {
        try
        {
            if (_engine.Status != EngineStatus.Stopped)
                await _engine.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Остановка движка: " + ex.Message, ex);
        }
    }

    private void Notify(string message)
    {
        try
        {
            Notification?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger.Error("Уведомление: " + ex.Message, ex);
        }
    }

    private void PublishError(ConnectionIssue? issue, string message)
    {
        Publish(Snapshot with
        {
            State = ConnectionState.Error,
            Issue = issue ?? Snapshot.Issue,
            HasQualifiedCandidate = false,
            ErrorMessage = message,
        });
    }

    private void Publish(ConnectionSnapshot snapshot)
    {
        snapshot = snapshot with
        {
            IsAutoMode = _config.Connection.Mode == ConnectionMode.Auto,
        };

        lock (_sync) { _snapshot = snapshot; }

        try
        {
            StateChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger.Error("Обработчик состояния подключения: " + ex.Message, ex);
        }
    }

    private ConnectionSnapshot CreateBaseSnapshot() => new(
        ConnectionState.Disconnected,
        ConnectionIssue.None,
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        null,
        null,
        false,
        null,
        _config.Connection.Mode == ConnectionMode.Auto);

    private static string HashArguments(string arguments)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(arguments ?? string.Empty));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
