# ZPRT_NEXT_GEN: настройки, обновления, индикатор — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить мерцание и кольцо-прогресс, исправить выход из трея, вынести настройки в отдельное окно, добавить тест всех альтов и обновление движка/программы.

**Architecture:** Чистая логика (версии, GitHub Releases, обновление движка/программы, прогон пресетов, маппинг прогресса) — в `ZprtNextGen.Core` с внедряемыми интерфейсами. WPF-слой добавляет `SettingsWindow`, DP `Progress` в `MeditationView` и команды в `MainViewModel`. Сеть — `HttpClient`, zip — `System.IO.Compression`, JSON — `System.Text.Json`.

**Tech Stack:** .NET 10, C#, WPF, xUnit, `System.IO.Compression.ZipFile`.

**Spec:** `docs/superpowers/specs/2026-11-09-settings-updates-design.md`

## Global Constraints

- Интерфейс и сообщения — по-русски; идентификаторы — английские.
- Без новых NuGet-пакетов; тесты не используют сеть, администратора и реальный `winws`.
- `IsRunning` уведомляется явно; UI-обновления только через `IUiDispatcher`.
- Обновление движка обязано иметь бэкап и откат; `settings.json` не трогать.
- Portable-приложение: подмена `.exe` — только helper-скриптом после выхода.
- PowerShell 5.1 не использовать в рантайме приложения.
- Перед публикацией закрыть запущенный `ZprtNextGen`.

## Review Focus

1. Обновление движка при недоступном GitHub / битом zip → приложение не теряет рабочий `engine\`, показывает русскую ошибку.
2. Остановка приложения из трея при работающем `winws` → процесс завершается, `winws` не остаётся сиротой.
3. Ошибка связи/старта → ровно одно окно, кольцо на 50%, UI не зависает.
4. Тест альтов: отмена или падение одного пресета → прогон завершается, `winws` остановлен, результаты видны.
5. Обновление программы: `.exe` занят запущенным процессом → helper ждёт PID, заменяет и перезапускает.

---

### Task 1: Прогресс индикатора (Core + тест)

**Files:**
- Create: `src/ZprtNextGen.Core/Presentation/MeditationProgress.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presentation/MeditationProgressTests.cs`

**Interfaces:**
- Produces: `public static class MeditationProgress { public static double Resolve(MeditationState state); }`

- [ ] **Step 1: Write failing test**
```csharp
[Theory]
[InlineData(MeditationState.Stopped, 0.0)]
[InlineData(MeditationState.Connecting, 0.85)]
[InlineData(MeditationState.Connected, 1.0)]
[InlineData(MeditationState.Error, 0.5)]
public void Resolve_MapsState(MeditationState state, double expected) =>
    Assert.Equal(expected, MeditationProgress.Resolve(state));
```
- [ ] **Step 2: Run test → FAIL (type missing)**
- [ ] **Step 3: Implement mapping via switch**
- [ ] **Step 4: Run test → PASS**
- [ ] **Step 5: Commit** `feat(core): add meditation progress mapping`

### Task 2: Кольцо и мерцание в MeditationView

**Files:**
- Modify: `src/ZprtNextGen.App/Controls/MeditationView.xaml`, `.xaml.cs`
- Test: запуск приложения вручную

**Interfaces:**
- Consumes: `MeditationProgress.Resolve`.
- Produces: DP `Progress` (double, 0…1).

- [ ] **Step 1: Add `ProgressArc` Path + `PulseShimmer` Path to XAML; base ring remains**
- [ ] **Step 2: Add `Progress` DP; in callback rebuild arc geometry (full ellipse at 1.0, arc otherwise)**
- [ ] **Step 3: In `ApplyState` animate Progress: Connecting→0.85/7s, Connected→1/0.6s, Error→0.5/0.4s, Stopped→0/0.4s; ReduceMotion sets instantly**
- [ ] **Step 4: Add `ShimmerStoryboard` with `DoubleAnimationUsingKeyFrames` on `PulseShimmer.Opacity` (2.2s loop); start for non-stopped states, stop otherwise**
- [ ] **Step 5: Manual check: full ring on success, half on failure, flicker visible**
- [ ] **Step 6: Commit** `feat(ui): progress ring and pulse shimmer`

### Task 3: Выход из трея и окно ошибки

**Files:**
- Modify: `src/ZprtNextGen.App/MainWindow.xaml.cs`, `src/ZprtNextGen.App/ViewModels/MainViewModel.cs`, `src/ZprtNextGen.App/App.xaml.cs`
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Produces: `event EventHandler<string>? ErrorRaised`, `string LastErrorMessage`; `RequestExit()` closes window; `MainViewModel.HandleError` raises event.

- [ ] **Step 1: Failing test: `HandleError` raises `ErrorRaised` with message**
```csharp
vm.ErrorRaised += (_, msg) => captured = msg;
await vm.ChangeEnginePathAsync(@"C:\X"); // fake locate throws
Assert.Contains("boom", captured);
```
- [ ] **Step 2: Run → FAIL**
- [ ] **Step 3: Add event + message; implement `OnClosing` two-phase shutdown (`_shutdownCompleted`), `RequestExit()` calls `Close()`**
- [ ] **Step 4: Subscribe `MainWindow` to `ErrorRaised` → single `MessageBox` (`_errorDialogOpen` guard)**
- [ ] **Step 5: Tray exit action marshals to dispatcher and calls `RequestExit()`**
- [ ] **Step 6: Run tests + manual tray exit**
- [ ] **Step 7: Commit** `fix(app): exit from tray and single error dialog`

### Task 4: Отдельное окно настроек

**Files:**
- Create: `src/ZprtNextGen.App/SettingsWindow.xaml`, `.xaml.cs`
- Modify: `src/ZprtNextGen.App/MainWindow.xaml`, `.xaml.cs`, `src/ZprtNextGen.App/Resources/Theme.xaml`
- Test: ручная проверка

**Interfaces:**
- Consumes: `MainViewModel` (existing settings/comands), `AboutWindow`.
- Produces: `SettingsWindow(MainViewModel viewModel)`.

- [ ] **Step 1: Remove bottom Expander and 4th row; add gear `Button` top-right of hero (`Click="OnSettings"`)**
- [ ] **Step 2: Add `IconButton`, `DarkProgressBar`, `SettingsSection` styles to Theme**
- [ ] **Step 3: Build SettingsWindow with sections: Движок, Поведение, Обновления, Проверка пресетов, footer (О программе / Закрыть)**
- [ ] **Step 4: Wire engine browse, About, close; reuse single window instance**
- [ ] **Step 5: Manual check: all controls work, no overlaps**
- [ ] **Step 6: Commit** `feat(ui): move settings to dedicated window with gear button`

### Task 5: GitHub Releases, версии, загрузка (Core)

**Files:**
- Create: `src/ZprtNextGen.Core/Updates/{ReleaseInfo,GitHubReleaseClient,VersionComparer,HttpFileDownloader}.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Updates/{VersionComparerTests,GitHubReleaseClientTests}.cs`

**Interfaces:**
- `record ReleaseAsset(string Name, string DownloadUrl, long Size)`
- `record ReleaseInfo(string Tag, string Name, string? Body, IReadOnlyList<ReleaseAsset> Assets) { ReleaseAsset? FindZip(); }`
- `interface IGitHubReleaseClient { Task<ReleaseInfo?> GetLatestAsync(string repository, CancellationToken ct); }`
- `static class VersionComparer { int Compare(string? a, string? b); bool IsNewer(string? candidate, string? current); }`
- `interface IFileDownloader { Task DownloadAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken ct); }`
- `GitHubReleaseClient.CreateDefault()`, `HttpFileDownloader.CreateDefault()`

- [ ] **Step 1: Failing tests for `VersionComparer` (v-prefix, 1.10.4>1.10.2, null, equal)**
- [ ] **Step 2: Implement comparer; run → PASS**
- [ ] **Step 3: Failing tests for client with fake `HttpMessageHandler`: parses tag/assets; 404 → null**
- [ ] **Step 4: Implement with `JsonDocument`; run → PASS**
- [ ] **Step 5: Implement downloader with Content-Length progress**
- [ ] **Step 6: Commit** `feat(core): github releases client and version comparison`

### Task 6: Обновление движка и запись каталога

**Files:**
- Create: `src/ZprtNextGen.Core/Presets/PresetCatalogWriter.cs`, `src/ZprtNextGen.Core/Updates/EngineUpdater.cs`
- Test: `tests/ZprtNextGen.Core.Tests/{Presets/PresetCatalogWriterTests,Updates/EngineUpdaterTests}.cs`

**Interfaces:**
- `static PresetCatalogWriter { string Serialize(PresetCatalog); void Save(PresetCatalog, string path); }`
- `record EngineUpdateResult(bool Updated, string? OldVersion, string? NewVersion, int PresetCount, string? Message)`
- `interface IEngineUpdater { Task<EngineUpdateResult> UpdateAsync(ReleaseInfo release, string engineRoot, string presetsPath, Func<Task> stopEngine, IProgress<string>? progress, CancellationToken ct); }`
- `EngineUpdater(IFileDownloader downloader)`

- [ ] **Step 1: Failing round-trip test for writer (Cyrillic + args)**
- [ ] **Step 2: Implement writer (same JSON shape as PresetTool); run → PASS**
- [ ] **Step 3: Failing updater test: local zip with `bin/winws.exe`, `bin/WinDivert.dll`, `bin/WinDivert64.sys`, `lists/list-general.txt`, `general.bat` → replaces engine, writes presets, backs up old**
- [ ] **Step 4: Failing rollback test: zip without `winws.exe` → old engine intact, throws**
- [ ] **Step 5: Implement: download → extract → validate → stopEngine → move to backup → copy → import presets → write; rollback on error**
- [ ] **Step 6: Run tests → PASS; commit** `feat(core): engine updater with backup and rollback`

### Task 7: Обновление программы

**Files:**
- Create: `src/ZprtNextGen.Core/Updates/AppUpdater.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Updates/AppUpdaterTests.cs`

**Interfaces:**
- `record AppUpdatePlan(string ScriptPath, string NewExecutablePath, string TargetExecutablePath)`
- `interface IAppUpdater { Task<AppUpdatePlan> PrepareAsync(ReleaseInfo release, string appDirectory, IProgress<string>? progress, CancellationToken ct); void Launch(AppUpdatePlan plan); }`
- `static string AppUpdater.CreateScript(int pid, string newExe, string targetExe, string tempDirectory)`

- [ ] **Step 1: Failing test: script contains PID wait, quoted paths, `start ""`**
- [ ] **Step 2: Failing test: `PrepareAsync` extracts zip and finds `ZprtNextGen.exe`**
- [ ] **Step 3: Implement + run → PASS; commit** `feat(core): portable app updater with restart script`

### Task 8: Прогон всех пресетов

**Files:**
- Create: `src/ZprtNextGen.Core/Presets/PresetTester.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presets/PresetTesterTests.cs`

**Interfaces:**
- `record PresetTestResult(string PresetId, string Name, bool Success, long ElapsedMs, string? Detail) { string StatusText { get; } }`
- `record PresetTestProgress(int Index, int Total, Preset Preset, PresetTestResult? Result)`
- `interface IPresetTester { Task<IReadOnlyList<PresetTestResult>> TestAllAsync(IReadOnlyList<Preset> presets, string engineRoot, GameFilterMode filter, IProgress<PresetTestProgress>? progress, CancellationToken ct); }`
- `PresetTester(IEngineProcessManager engine, IConnectivityService connectivity, TimeSpan? startupDelay = null)`

- [ ] **Step 1: Failing test: two presets, connectivity true → two results, engine stopped after each**
- [ ] **Step 2: Failing test: start throws on first → result false, second still runs**
- [ ] **Step 3: Failing test: pre-cancelled token → `OperationCanceledException`, engine stopped**
- [ ] **Step 4: Implement sequentially, always stop in `finally`; run → PASS; commit** `feat(core): presets tester`

### Task 9: Команды и состояние в MainViewModel

**Files:**
- Modify: `src/ZprtNextGen.App/ViewModels/MainViewModel.cs`
- Modify: `src/ZprtNextGen.App/App.xaml.cs` (composition)
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/MainViewModelTests.cs`, new fakes

**Interfaces:**
- `record MaintenanceDependencies(IGitHubReleaseClient GitHub, IEngineUpdater EngineUpdater, IAppUpdater AppUpdater, IPresetTester PresetTester, string AppVersion, string PresetsPath)`
- New optional last ctor param: `MaintenanceDependencies? Maintenance = null`
- VM: `AppVersion`, `UpdateStatusText`, `UpdateProgress`, `IsMaintenanceBusy`, `PresetTestResults` (`ObservableCollection<PresetTestResult>`), `PresetTestStatus`, `PresetTestProgress`, `IsTestingPresets`, events `RestartRequested`
- Commands: `CheckUpdatesCommand`, `UpdateEngineCommand`, `UpdateAppCommand`, `TestAllPresetsCommand`, `CancelPresetTestCommand`

- [ ] **Step 1: Failing tests: `CheckUpdatesAsync` fills text for newer engine; `TestAllPresetsAsync` fills results; cancel stops tester**
- [ ] **Step 2: Implement properties/commands and `ReloadCatalogAsync` (preserve selected id)**
- [ ] **Step 3: Compose services in `App.OnStartup`; `RestartRequested` → `Shutdown()`**
- [ ] **Step 4: Run tests → PASS; commit** `feat(app): update and preset-test commands`

### Task 10: About, версия, документация, релиз-сборка

**Files:**
- Modify: `src/ZprtNextGen.App/AboutWindow.xaml`, `.xaml.cs`, `ZprtNextGen.App.csproj`, `AGENTS.md`, `docs/CODE_MAP.md`
- Test: full suite + manual

- [ ] **Step 1: Telegram link `https://t.me/benny_4ip` (clickable)**
- [ ] **Step 2: `<Version>0.2.0</Version>`; app version shown in settings/About**
- [ ] **Step 3: Run `dotnet test -c Release` → all pass**
- [ ] **Step 4: `dotnet publish` to Desktop; verify `presets\presets.json` and `engine\bin\winws.exe`; launch and smoke-test**
- [ ] **Step 5: Update `docs/CODE_MAP.md`, `AGENTS.md`, run `tools/Index-Code.ps1`**
- [ ] **Step 6: Commit + push** `feat: settings window, updates, progress ring, tray exit fix`
