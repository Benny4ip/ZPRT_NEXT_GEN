# План: настройки с боковым меню + точечные оптимизации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Переделать окно настроек в боковое меню из пяти разделов и попутно убрать зависание первого запуска portable, ускорить тест альтов и починить рассинхрон игрового профиля.

**Architecture:** Настройки остаются одним `SettingsViewModel`; добавляются `SettingsSection`, `SelectedSection`, `IsManualMode` и `OpenAboutCommand`. Раскладка `SettingsWindow.xaml` меняется на две колонки: слева список разделов, справа — одна из пяти секций (видимость по `DataTrigger`). Логика запуска portable выносится в асинхронный `StartPortableAsync` со стартовым окном; `PresetTester` ждёт готовности движка адаптивно.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, xUnit, `System.Text.Json`, PowerShell 5.1 для сборки.

**Spec:** `docs/superpowers/specs/2026-11-19-settings-sidebar-design.md`

## Global Constraints

- Целевой фреймворк `net10.0-windows`, WPF + WinForms только там, где уже используется. Новых NuGet-пакетов не добавлять.
- Весь пользовательский текст — на русском.
- Portable-сборка (встроенный `ZprtNextGen.Engine.zip`) не показывает настройки движка; Standard показывает. Это поведение не ломать.
- Существующие тесты должны остаться зелёными: Core `191/191`, App `46/46`. Новые тесты — по TDD (сначала падающий тест).
- Upstream-скрипты движка (`engine/`) не править. Схему `config.json` (schemaVersion 2) не менять.
- В `SettingsWindow.xaml.cs` — только работа с окнами/диалогами/буфером; переключение разделов и команд — через binding.
- PowerShell 5.1: без `&&`, `[^1]`, `sed`; `dotnet build/test/publish` запускать последовательно, не параллельно.
- Core-код не должен захватывать UI-контекст: в `Core` использовать `ConfigureAwait(false)` на `await`.
- Репозиторий: `C:\Users\Machine\Documents\ZPRT_NEXT_GEN`. Команды выполнять из него.

## Review Focus

1. **Первый запуск portable на медленном диске.** Пользователь должен увидеть «Готовим движок…», а не пустой экран; при ошибке распаковки — понятное сообщение и выход, без зависшего окна. Тест/шаг: Task 7.
2. **Portable с `--tray`.** Закрытие стартового окна не должно завершать процесс до появления трея: окно закрывается только после `StartCore`. Тест/шаг: Task 7 (ручная проверка `--tray`).
3. **winws падает сразу после старта.** Пресет должен помечаться неудачным быстро, без ожидания полных 2 с. Тест: `TestAllAsync_EngineDiesAfterStart_FailsWithoutWaitingFullDelay` (Task 6).
4. **Игровой профиль и тонкая настройка.** Выключение профиля и выбор «Выключен» должны давать фактический `Disabled`, а ComboBox — показывать реальное значение. Тесты Task 1.
5. **Кэш автозапуска.** Переключатель автозапуска продолжает работать, а `Reload()` больше не вызывает `schtasks` повторно. Тесты Task 2.

---

### Task 1: Согласовать `GameFilter` и `DetailedGameFilter`

**Files:**
- Modify: `src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `GameFilterMode` (`All`, `Tcp`, `Udp`, `Disabled`) из `ZprtNextGen.Core.Presets`.
- Produces: `public GameFilterMode DetailedGameFilter { get; set; }` — геттер возвращает фактический `_gameFilter`; сеттер всегда применяет значение через `GameFilter`. Поле `_lastDetailedFilter` удаляется.

- [ ] **Step 1: Написать падающие тесты**

Добавить в `SettingsViewModelTests`:

```csharp
[Fact]
public void DetailedGameFilter_WhenProfileDisabled_ReportsDisabled()
{
    var vm = CreateSut(isPortable: false);
    vm.GameFilter = GameFilterMode.All;
    vm.IsGameProfileEnabled = false;

    Assert.Equal(GameFilterMode.Disabled, vm.DetailedGameFilter);
}

[Fact]
public void DetailedGameFilter_SetDisabled_DisablesActualFilter()
{
    var vm = CreateSut(isPortable: false);
    vm.GameFilter = GameFilterMode.Tcp;

    vm.DetailedGameFilter = GameFilterMode.Disabled;

    Assert.Equal(GameFilterMode.Disabled, vm.GameFilter);
    Assert.False(vm.IsGameProfileEnabled);
    Assert.Equal(GameFilterMode.Disabled, vm.DetailedGameFilter);
}

[Fact]
public void DetailedGameFilter_SetTcp_KeepsProfileOff()
{
    var vm = CreateSut(isPortable: false);
    vm.GameFilter = GameFilterMode.All;

    vm.DetailedGameFilter = GameFilterMode.Tcp;

    Assert.Equal(GameFilterMode.Tcp, vm.GameFilter);
    Assert.False(vm.IsGameProfileEnabled);
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug --filter "FullyQualifiedName~DetailedGameFilter"`
Expected: FAIL — `DetailedGameFilter_WhenProfileDisabled_ReportsDisabled` возвращает `All` вместо `Disabled`, а `SetDisabled` не меняет `GameFilter`.

- [ ] **Step 3: Реализовать**

В `SettingsViewModel.cs`:

1. Удалить поле `private GameFilterMode _lastDetailedFilter = GameFilterMode.Disabled;`.
2. Заменить `GameFilter` на:

```csharp
public GameFilterMode GameFilter
{
    get => _gameFilter;
    set
    {
        if (!SetField(ref _gameFilter, value))
            return;

        OnPropertyChanged(nameof(DetailedGameFilter));

        if (_initializing)
            return;

        _initializing = true;
        IsGameProfileEnabled = value == GameFilterMode.All;
        _initializing = false;

        _config = _config with { GameFilter = value };
        SaveAndApply();
        MarkNeedsRestart();
    }
}
```

3. Заменить `DetailedGameFilter` на:

```csharp
/// <summary>Тонкая настройка фильтра, когда игровой профиль выключен.</summary>
public GameFilterMode DetailedGameFilter
{
    get => _gameFilter;
    set => GameFilter = value;
}
```

4. В `LoadFromConfig` удалить блок:

```csharp
_lastDetailedFilter = _config.GameFilter is GameFilterMode.Tcp or GameFilterMode.Udp or GameFilterMode.All
    ? _config.GameFilter
    : GameFilterMode.All;
```

- [ ] **Step 4: Прогнать тесты**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug`
Expected: PASS, `46 + 3 = 49` тестов.

- [ ] **Step 5: Коммит**

```powershell
cd 'C:\Users\Machine\Documents\ZPRT_NEXT_GEN'
git add src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "fix(settings): report and persist actual game filter"
```

---

### Task 2: Кэшировать состояние автозапуска

**Files:**
- Modify: `src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs`
- Modify: `tests/ZprtNextGen.App.Tests/Fakes/FakeAutostart.cs`
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `IAutostartService.IsEnabledAsync`.
- Produces: конструктор `SettingsViewModel` один раз вызывает `RefreshAutostartAsync()`; `Reload()` больше не вызывает `schtasks`.

- [ ] **Step 1: Добавить счётчик в фейк**

В `FakeAutostart` добавить свойство и инкремент:

```csharp
public int IsEnabledCalls { get; private set; }

public Task<bool> IsEnabledAsync(CancellationToken ct = default)
{
    IsEnabledCalls++;
    return Task.FromResult(Enabled);
}
```

- [ ] **Step 2: Написать падающие тесты**

В `SettingsViewModelTests` расширить `CreateSut` параметром и использованием фейка:

```csharp
private static SettingsViewModel CreateSut(
    bool isPortable,
    FakeGitHubReleaseClient? gitHub = null,
    string? tempDir = null,
    FakeAutostart? autostart = null)
```

и в `SettingsDependencies` заменить `new FakeAutostart()` на `autostart ?? new FakeAutostart()`.

Добавить тесты:

```csharp
[Fact]
public void Constructor_ReadsAutostartOnce()
{
    var autostart = new FakeAutostart();
    _ = CreateSut(isPortable: false, autostart: autostart);

    Assert.Equal(1, autostart.IsEnabledCalls);
}

[Fact]
public void Reload_DoesNotReadAutostartAgain()
{
    var autostart = new FakeAutostart();
    var vm = CreateSut(isPortable: false, autostart: autostart);

    vm.Reload();

    Assert.Equal(1, autostart.IsEnabledCalls);
}
```

- [ ] **Step 3: Убедиться, что тесты падают**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug --filter "FullyQualifiedName~Autostart"`
Expected: FAIL — `Reload_DoesNotReadAutostartAgain` получает `2` вызова.

- [ ] **Step 4: Реализовать**

1. В `LoadFromConfig` удалить строку `_ = RefreshAutostartAsync();` (в конце метода).
2. В конструкторе после `LoadFromConfig(); RefreshPresets();` добавить `_ = RefreshAutostartAsync();`.

- [ ] **Step 5: Прогнать тесты**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug`
Expected: PASS, `49 + 2 = 51` тест.

- [ ] **Step 6: Коммит**

```powershell
git add src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs tests/ZprtNextGen.App.Tests/Fakes/FakeAutostart.cs tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "perf(settings): read autostart state once instead of on every reload"
```

---

### Task 3: Добавить разделы и команду «О программе»

**Files:**
- Create: `src/ZprtNextGen.App/ViewModels/SettingsSection.cs`
- Modify: `src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/ZprtNextGen.App/SettingsWindow.xaml.cs`
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Produces:
  - `public enum SettingsSection { Main = 0, Presets = 1, Startup = 2, Updates = 3, Diagnostics = 4 }`
  - `public SettingsSection SelectedSection { get; set; }` (по умолчанию `Main`)
  - `public ICommand OpenAboutCommand { get; }`
  - `public event EventHandler? AboutRequested;`
- Consumes: `RelayCommand` из `ZprtNextGen.App.ViewModels`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Fact]
public void SelectedSection_DefaultsToMain()
{
    var vm = CreateSut(isPortable: false);

    Assert.Equal(SettingsSection.Main, vm.SelectedSection);
}

[Fact]
public void SelectedSection_CanBeChanged()
{
    var vm = CreateSut(isPortable: false);

    vm.SelectedSection = SettingsSection.Diagnostics;

    Assert.Equal(SettingsSection.Diagnostics, vm.SelectedSection);
}

[Fact]
public void OpenAboutCommand_RaisesAboutRequested()
{
    var vm = CreateSut(isPortable: false);
    var raised = false;
    vm.AboutRequested += (_, _) => raised = true;

    vm.OpenAboutCommand.Execute(null);

    Assert.True(raised);
}
```

- [ ] **Step 2: Убедиться, что тесты не компилируются**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug`
Expected: FAIL компиляции — `SettingsSection` и `OpenAboutCommand` не существуют.

- [ ] **Step 3: Создать enum**

`src/ZprtNextGen.App/ViewModels/SettingsSection.cs`:

```csharp
namespace ZprtNextGen.App.ViewModels;

/// <summary>Разделы окна настроек. Порядок совпадает с порядком в боковом меню.</summary>
public enum SettingsSection
{
    Main = 0,
    Presets = 1,
    Startup = 2,
    Updates = 3,
    Diagnostics = 4,
}
```

- [ ] **Step 4: Реализовать в ViewModel**

В поля добавить:

```csharp
private readonly RelayCommand _openAboutCommand;
private SettingsSection _selectedSection = SettingsSection.Main;
```

В конструкторе после `_restartConnectionCommand = ...` добавить:

```csharp
_openAboutCommand = new RelayCommand(_ => AboutRequested?.Invoke(this, EventArgs.Empty));
```

В события добавить:

```csharp
/// <summary>Пользователь нажал «О программе».</summary>
public event EventHandler? AboutRequested;
```

Добавить свойства:

```csharp
public SettingsSection SelectedSection
{
    get => _selectedSection;
    set => SetField(ref _selectedSection, value);
}

public ICommand OpenAboutCommand => _openAboutCommand;
```

- [ ] **Step 5: Подключить событие в окне**

В `SettingsWindow.xaml.cs` в конструкторе после `_viewModel.ErrorRaised += OnErrorRaised;`:

```csharp
_viewModel.AboutRequested += OnAboutRequested;
```

Заменить метод:

```csharp
private void OnAboutRequested(object? sender, EventArgs e)
{
    var window = new AboutWindow(_viewModel.EngineVersion) { Owner = this };
    window.ShowDialog();
}
```

(Старый `OnAbout(object, RoutedEventArgs)` удалить; XAML на него больше не ссылается.)

- [ ] **Step 6: Прогнать тесты**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug`
Expected: PASS, `51 + 3 = 54` теста.

- [ ] **Step 7: Коммит**

```powershell
git add src/ZprtNextGen.App/ViewModels/SettingsSection.cs src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs src/ZprtNextGen.App/SettingsWindow.xaml.cs tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat(settings): add section state and about command"
```

---

### Task 4: Добавить `IsManualMode` для плиток режима

**Files:**
- Modify: `src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: `public bool IsManualMode { get; set; }` — `true`, когда `IsAutoMode == false`; установка `true` выключает авторежим.

- [ ] **Step 1: Написать падающий тест**

```csharp
[Fact]
public void IsManualMode_ReflectsAutoMode()
{
    var vm = CreateSut(isPortable: false);

    vm.IsAutoMode = true;
    Assert.False(vm.IsManualMode);

    vm.IsManualMode = true;
    Assert.False(vm.IsAutoMode);
    Assert.True(vm.IsManualMode);
}
```

- [ ] **Step 2: Убедиться, что тест не компилируется**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug`
Expected: FAIL компиляции — `IsManualMode` отсутствует.

- [ ] **Step 3: Реализовать**

В `IsAutoMode` setter перестроить начало:

```csharp
set
{
    if (!SetField(ref _isAutoMode, value))
        return;

    OnPropertyChanged(nameof(IsManualMode));

    if (_initializing)
        return;

    _config = _config with
    {
        Connection = _config.Connection with
        {
            Mode = value ? ConnectionMode.Auto : ConnectionMode.Manual,
        },
    };
    SaveAndApply();
    OnPropertyChanged(nameof(AutoModeHint));
    MarkNeedsRestart();
}
```

Добавить свойство рядом с `IsAutoMode`:

```csharp
/// <summary>Ручной режим (для плиток «Авто» / «Ручной»).</summary>
public bool IsManualMode
{
    get => !_isAutoMode;
    set
    {
        if (value)
            IsAutoMode = false;
    }
}
```

- [ ] **Step 4: Прогнать тесты**

Run: `dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug`
Expected: PASS, `54 + 1 = 55` тестов.

- [ ] **Step 5: Коммит**

```powershell
git add src/ZprtNextGen.App/ViewModels/SettingsViewModel.cs tests/ZprtNextGen.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat(settings): expose manual mode for mode tiles"
```

---

### Task 5: Переписать `SettingsWindow.xaml` под боковое меню

**Files:**
- Modify: `src/ZprtNextGen.App/SettingsWindow.xaml`
- Modify: `src/ZprtNextGen.App/SettingsWindow.xaml.cs` (только если обработчик всё ещё ссылается на `OnAbout`)

**Interfaces:**
- Consumes: `SelectedSection`, `OpenAboutCommand`, `IsManualMode`, `IsAutoMode`, `IsGameProfileEnabled`, `DetailedGameFilter`, `IsAutostartEnabled`, `MinimizeToTray`, `ReduceMotion`, `EngineSectionTitle`, `IsStandard`, `IsPortable`, `NeedsRestart`, `SaveStatus`.
- Produces: визуальное окно 900×720 (мин. 780×600); переключение секций без логики в code-behind.

**Раскладка (обязательные элементы):**

1. `Window`: `Width="900" Height="720" MinWidth="780" MinHeight="600"`, `Style="{StaticResource DarkWindow}"`, `Loaded="OnLoaded"`.
2. Корневой `Grid` — две колонки `210` и `*`.
3. Левая колонка (`Border`, фон `{StaticResource Brush.Surface}`, правая граница `Brush.Border`):
   - сверху название «ZPRT NEXT GEN» и подпись «Настройки»;
   - `ListBox` со стилем `SettingsNavList`, `SelectedValue="{Binding SelectedSection, Mode=TwoWay}"`, `SelectedValuePath="Tag"`, `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`, пять `ListBoxItem`:
     - «Основное» → `Tag="{x:Static vm:SettingsSection.Main}"`
     - «Пресеты» → `Presets`
     - «Запуск и трей» → `Startup`
     - «Обновления» → `Updates`
     - «Диагностика» → `Diagnostics`
   - снизу две кнопки: «О программе» (`Command="{Binding OpenAboutCommand}"`), «Закрыть» (`Click="OnClose"`).
4. Правая колонка — `Grid` с тремя строками `Auto/Auto/*`:
   - шапка: заголовок «Настройки» и `SaveStatus`;
   - плашка перезапуска (существующий `Border` с `Visibility="{Binding NeedsRestart, ...}"` и кнопкой `RestartConnectionCommand`);
   - контент — `Grid` с пятью `ScrollViewer`, каждый со стилем:
     ```xml
     <Style TargetType="ScrollViewer" BasedOn="{StaticResource DarkScrollViewer}">
       <Setter Property="Visibility" Value="Collapsed" />
       <Style.Triggers>
         <DataTrigger Binding="{Binding SelectedSection}" Value="{x:Static vm:SettingsSection.Main}">
           <Setter Property="Visibility" Value="Visible" />
         </DataTrigger>
       </Style.Triggers>
     </Style>
     ```
     (для каждого раздела — своё значение enum). Внутри — `StackPanel` с `MaxWidth="760"`.

**Содержимое разделов (перенести существующие биндинги без изменения логики):**

- **Основное**
  - `Card` «Как подключаться»: заголовок; два `RadioButton` со стилем `SettingsModeTile`, `GroupName="connection-mode"`: «Авто» (`IsChecked="{Binding IsAutoMode, Mode=TwoWay}"`) и «Ручной» (`IsChecked="{Binding IsManualMode, Mode=TwoWay}"`); `TextBlock` `AutoModeHint`; `ComboBox` ручного пресета (как сейчас, `IsEnabled` по `IsAutoMode == False`); `CurrentProfileText`.
  - `Card` «Игровой профиль»: `ToggleButton` `IsGameProfileEnabled`; подсказка «Включай, если играешь.»; `Expander` «Дополнительно» с `ComboBox` `DetailedGameFilter` (пункты «Все/TCP/UDP/Выключен»).
- **Пресеты**
  - `Card` «Пресеты»: кнопки «Протестировать альты» / «Остановить», прогресс `IsTestingPresets`, `PresetTestStatus`, список `PresetTestResults` с действиями `UsePresetCommand`/`MakeAutomaticCommand`.
  - `Expander` «Мои пресеты»: `CustomPresets`, кнопки «Изменить/Удалить», «Добавить пресет».
- **Запуск и трей** — `Card`: автозапуск (`ToggleAutostartCommand`, `IsAutostartEnabled`, `IsChecked` через `CommandParameter`), «Сворачивать в трей при закрытии» (`MinimizeToTray`), «Уменьшить анимацию» (`ReduceMotion`).
- **Обновления** — `Card` с заголовком `{Binding EngineSectionTitle}`:
  - Standard-блок (`IsStandard`): `TextBox` `EnginePath`, «Обзор…» (`OnBrowseEngine`), `EngineStatusText`, подсказка;
  - `EngineVersion`;
  - кнопки «Проверить обновление», «Обновить движок» (только Standard), «Обновить программу», «GitHub releases»;
  - portable-подсказка «Движок встроен…» (`IsPortable`);
  - `UpdateStatusText`, `UpdateProgress`.
- **Диагностика** — `Card`: «Проверить связь», «Открыть папку логов», «Скопировать лог» (`OnCopyLog`), `ConnectivityStatus`, `ConnectivityRows`, «Полный сброс Discord» (`FullDiscordResetCommand`), `DiscordStatus`.

**Стили в `Window.Resources` (локально, без правки `Theme.xaml`):**

- `SettingsNavList` (`ListBox`): `Background="Transparent"`, `BorderThickness="0"`, `Padding="8"`.
- `SettingsNavItem` (`ListBoxItem`): шаблон `Border` с `CornerRadius="8"`, `Padding="10,8"`, `ContentPresenter`; триггеры:
  - `IsMouseOver` → `Background="{StaticResource Brush.Surface}"`;
  - `IsSelected` → `Background="#2A1215"`, `Foreground="{StaticResource Brush.AccentGlow}"`.
- `SettingsModeTile` (`RadioButton`): шаблон `Border` (`CornerRadius="10"`, `Padding="14,12"`, `BorderThickness="1"`, `BorderBrush="{StaticResource Brush.Border}"`, `Background="{StaticResource Brush.Background}"`) с `ContentPresenter`; триггер `IsChecked` → `BorderBrush="{StaticResource Brush.Accent}"`, `Background="#2A1215"`.
- Сохранить существующие `SectionTitle`, `Muted`, `RowLabel`, `SmallButton` и конвертеры.

- [ ] **Step 1: Переписать XAML по раскладке выше**

- [ ] **Step 2: Собрать проект**

Run: `dotnet build src\ZprtNextGen.App\ZprtNextGen.App.csproj -c Debug`
Expected: `Build succeeded`, 0 ошибок XAML.

- [ ] **Step 3: Проверить, что старые обработчики не потеряны**

Убедиться, что в `SettingsWindow.xaml.cs` остались `OnLoaded`, `OnErrorRaised`, `OnBrowseEngine`, `OnAddCustomPreset`, `OnEditCustomPreset`, `OnDeleteCustomPreset`, `OnCopyLog`, `OnAboutRequested`, `OnClose`, а `OnAbout` удалён.

- [ ] **Step 4: Ручная проверка**

Запустить Standard-сборку, открыть настройки, переключить все пять разделов, проверить, что:
- плашка «перезапусти обход» видна в любом разделе и не дублируется;
- «О программе» открывает окно;
- «Закрыть» закрывает окно;
- при выключенном игровом профиле ComboBox показывает «Выключен».

- [ ] **Step 5: Коммит**

```powershell
git add src/ZprtNextGen.App/SettingsWindow.xaml src/ZprtNextGen.App/SettingsWindow.xaml.cs
git commit -m "feat(settings): sidebar layout with five sections"
```

---

### Task 6: Адаптивное ожидание в `PresetTester`

**Files:**
- Modify: `src/ZprtNextGen.Core/Presets/PresetTester.cs`
- Modify: `tests/ZprtNextGen.Core.Tests/Fakes/FakeEngineProcess.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presets/PresetTesterTests.cs`

**Interfaces:**
- Produces: конструктор `PresetTester(IEngineProcessManager engine, IConnectivityService connectivity, TimeSpan? startupDelay = null, TimeSpan? pollInterval = null, TimeSpan? settleDelay = null)`; значения по умолчанию `2 с`, `200 мс`, `300 мс`.
- Consumes: `IEngineProcessManager.Status` (`Stopped`, `Starting`, `Running`, `Error`).

- [ ] **Step 1: Расширить фейк**

В `tests/ZprtNextGen.Core.Tests/Fakes/FakeEngineProcess.cs` добавить:

```csharp
public EngineStatus StatusAfterStart { get; set; } = EngineStatus.Running;
```

и в `StartAsync` заменить `Status = EngineStatus.Running;` на `Status = StatusAfterStart;`.

- [ ] **Step 2: Написать падающие тесты**

Добавить в `PresetTesterTests` (и `using System.Diagnostics;`):

```csharp
[Fact]
public async Task TestAllAsync_AdaptiveWait_DoesNotWaitFullStartupDelay()
{
    var engine = new FakeEngineProcess();
    var connectivity = new FakeConnectivity();
    var sut = new PresetTester(
        engine, connectivity,
        startupDelay: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10),
        settleDelay: TimeSpan.FromMilliseconds(10));

    var stopwatch = Stopwatch.StartNew();
    var results = await sut.TestAllAsync(
        [Presets[0]], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);
    stopwatch.Stop();

    Assert.True(results[0].Success);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"заняло {stopwatch.Elapsed}");
}

[Fact]
public async Task TestAllAsync_EngineDiesAfterStart_FailsWithoutWaitingFullDelay()
{
    var engine = new FakeEngineProcess { StatusAfterStart = EngineStatus.Stopped };
    var connectivity = new FakeConnectivity();
    var sut = new PresetTester(
        engine, connectivity,
        startupDelay: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10),
        settleDelay: TimeSpan.FromMilliseconds(10));

    var stopwatch = Stopwatch.StartNew();
    var results = await sut.TestAllAsync(
        [Presets[0]], @"C:\E", GameFilterMode.Disabled, null, CancellationToken.None);
    stopwatch.Stop();

    Assert.False(results[0].Success);
    Assert.Contains("завершился", results[0].Detail);
    Assert.Equal(0, connectivity.CheckCalls);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"заняло {stopwatch.Elapsed}");
}
```

- [ ] **Step 3: Убедиться, что тесты падают/долго идут**

Run: `dotnet test tests\ZprtNextGen.Core.Tests\ZprtNextGen.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AdaptiveWait|FullyQualifiedName~EngineDiesAfterStart"`
Expected: FAIL — новый конструктор не компилируется (или тесты идут дольше 2 с из-за фиксированной паузы 5 с).

- [ ] **Step 4: Реализовать**

В `PresetTester.cs`:

1. Добавить поля `_pollInterval` и `_settleDelay`.
2. Расширить конструктор:

```csharp
public PresetTester(
    IEngineProcessManager engine,
    IConnectivityService connectivity,
    TimeSpan? startupDelay = null,
    TimeSpan? pollInterval = null,
    TimeSpan? settleDelay = null)
{
    _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
    _startupDelay = startupDelay ?? TimeSpan.FromSeconds(2);
    _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);
    _settleDelay = settleDelay ?? TimeSpan.FromMilliseconds(300);
}
```

3. Заменить `await Task.Delay(_startupDelay, ct);` на `await WaitForEngineRunningAsync(ct);`.
4. Добавить метод:

```csharp
private async Task WaitForEngineRunningAsync(CancellationToken ct)
{
    var deadline = DateTime.UtcNow + _startupDelay;

    while (_engine.Status != EngineStatus.Running)
    {
        // Процесс уже упал — ждать предел бессмысленно.
        if (_engine.Status is EngineStatus.Stopped or EngineStatus.Error)
            return;

        if (DateTime.UtcNow >= deadline)
            return;

        await Task.Delay(_pollInterval, ct);
    }

    // Дать winws стабилизироваться после Running.
    await Task.Delay(_settleDelay, ct);
}
```

5. В существующих тестах `PresetTesterTests` заменить вызовы
   `new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10))`
   на
   `new PresetTester(engine, connectivity, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))`,
   чтобы тесты не ждали стабилизацию 300 мс.

- [ ] **Step 5: Прогнать тесты**

Run: `dotnet test tests\ZprtNextGen.Core.Tests\ZprtNextGen.Core.Tests.csproj -c Debug`
Expected: PASS, `191 + 2 = 193` теста.

- [ ] **Step 6: Коммит**

```powershell
git add src/ZprtNextGen.Core/Presets/PresetTester.cs tests/ZprtNextGen.Core.Tests/Fakes/FakeEngineProcess.cs tests/ZprtNextGen.Core.Tests/Presets/PresetTesterTests.cs
git commit -m "perf(presets): wait for engine readiness adaptively"
```

---

### Task 7: Не блокировать UI при распаковке portable-движка

**Files:**
- Modify: `src/ZprtNextGen.Core/Engine/PortableEngineInstaller.cs`
- Create: `src/ZprtNextGen.App/StartupWindow.xaml`
- Create: `src/ZprtNextGen.App/StartupWindow.xaml.cs`
- Modify: `src/ZprtNextGen.App/App.xaml.cs`

**Interfaces:**
- Produces:
  - `PortableEngineInstaller.InstallAsync` распаковывает zip в `Task.Run` (без захвата UI-контекста);
  - `private async Task StartPortableAsync(AppStartupOptions options, IAppLogger logger, string logsDirectory, byte[] embeddedEngine, string version)`;
  - `private void StartCore(AppStartupOptions options, IAppLogger logger, string logsDirectory, string version, bool isPortable, PortableEngineResult? portableEngine)`;
  - `public partial class StartupWindow : Window`.

- [ ] **Step 1: Сделать распаковку асинхронной**

В `PortableEngineInstaller.InstallAsync` заменить:

```csharp
Directory.CreateDirectory(temporary);
Extract(_zip!, temporary);
```

на:

```csharp
Directory.CreateDirectory(temporary);
await Task.Run(() => Extract(_zip!, temporary), ct).ConfigureAwait(false);
```

`ConfigureAwait(false)` обязателен: `RunPortableCheck` вызывает `InstallAsync(...).GetAwaiter().GetResult()` и заблокировал бы UI-поток иначе.

- [ ] **Step 2: Создать стартовое окно**

`src/ZprtNextGen.App/StartupWindow.xaml`:

```xml
<Window x:Class="ZprtNextGen.App.StartupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ZPRT NEXT GEN"
        Width="380" Height="150"
        WindowStyle="None"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="False"
        Topmost="True"
        Background="{StaticResource Brush.Background}"
        Foreground="{StaticResource Brush.Text}">
  <Border BorderBrush="{StaticResource Brush.Accent}" BorderThickness="1" Padding="24">
    <StackPanel>
      <TextBlock Text="ZPRT NEXT GEN" FontSize="18" FontWeight="Bold"
                 Foreground="{StaticResource Brush.Text}" />
      <TextBlock Text="Готовим движок…" Margin="0,8,0,0"
                 Foreground="{StaticResource Brush.TextMuted}" />
      <ProgressBar IsIndeterminate="True" Height="4" Margin="0,18,0,0" />
    </StackPanel>
  </Border>
</Window>
```

`src/ZprtNextGen.App/StartupWindow.xaml.cs`:

```csharp
using System.Windows;

namespace ZprtNextGen.App;

/// <summary>Короткое окно на время распаковки portable-движка.</summary>
public partial class StartupWindow : Window
{
    public StartupWindow() => InitializeComponent();
}
```

- [ ] **Step 3: Разделить запуск в `App.xaml.cs`**

1. В `OnStartup` после `InstallGlobalExceptionHandlers(...)` заменить блок `try { Start(...) } catch { ... }` на:

```csharp
var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";
var embeddedEngine = PortableEngineInstaller.ReadEmbeddedEngine(typeof(App).Assembly);
var isPortable = embeddedEngine is { Length: > 0 };

if (options.PortableCheck)
{
    RunPortableCheck(embeddedEngine, version, logger);
    return;
}

if (isPortable)
{
    _ = StartPortableAsync(options, logger, logsDirectory, embeddedEngine!, version);
    return;
}

try
{
    StartCore(options, logger, logsDirectory, version, isPortable: false, portableEngine: null);
}
catch (Exception ex)
{
    logger.Error("Не удалось запустить приложение: " + ex, ex);
    MessageBox.Show(
        "Не удалось запустить ZPRT NEXT GEN:\n" + ex.Message +
        $"\n\nПодробности в журнале:\n{logsDirectory}",
        "ZPRT NEXT GEN",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
    Shutdown(1);
}
```

2. Удалить из начала старого `Start` вычисление `version`/`embeddedEngine`/`isPortable`, блок `if (options.PortableCheck)` и блок распаковки `if (isPortable) { ... }`.
3. Переименовать `private void Start(...)` в:

```csharp
private void StartCore(
    AppStartupOptions options,
    IAppLogger logger,
    string logsDirectory,
    string version,
    bool isPortable,
    PortableEngineResult? portableEngine)
```

Метод начинается с `var baseDirectory = AppContext.BaseDirectory;` и использует переданные `version`, `isPortable`, `portableEngine`.

4. Добавить метод:

```csharp
private async Task StartPortableAsync(
    AppStartupOptions options,
    IAppLogger logger,
    string logsDirectory,
    byte[] embeddedEngine,
    string version)
{
    StartupWindow? splash = options.StartInTray ? null : new StartupWindow();
    splash?.Show();

    try
    {
        var installer = new PortableEngineInstaller(embeddedEngine, version, logger: logger);
        var portableEngine = await installer.InstallAsync(CancellationToken.None);
        if (portableEngine is not null)
            logger.Info("Portable-движок: " + portableEngine.EngineRoot);

        // Сначала показываем главное окно (или создаём трей), только потом закрываем splash.
        // Иначе при ShutdownMode=OnLastWindowClose закрытие splash завершит процесс.
        StartCore(options, logger, logsDirectory, version, isPortable: true, portableEngine);
        splash?.Close();
    }
    catch (Exception ex)
    {
        splash?.Close();
        logger.Error("Не удалось запустить приложение: " + ex, ex);
        MessageBox.Show(
            "Не удалось запустить ZPRT NEXT GEN:\n" + ex.Message +
            $"\n\nПодробности в журнале:\n{logsDirectory}",
            "ZPRT NEXT GEN",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }
}
```

- [ ] **Step 4: Собрать и проверить CLI-режим**

Run: `dotnet build src\ZprtNextGen.App\ZprtNextGen.App.csproj -c Debug`
Expected: `Build succeeded`.

Run: `dotnet test tests\ZprtNextGen.Core.Tests\ZprtNextGen.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~PortableEngineInstaller"`
Expected: PASS (тесты не должны зависнуть).

- [ ] **Step 5: Ручная проверка portable**

- Обычный запуск portable: появляется окно «Готовим движок…», затем главное окно.
- Запуск `ZprtNextGen.exe --tray`: процесс не завершается, появляется иконка в трее.
- Повторный запуск: splash мелькает или не появляется (движок уже готов).

- [ ] **Step 6: Коммит**

```powershell
git add src/ZprtNextGen.Core/Engine/PortableEngineInstaller.cs src/ZprtNextGen.App/StartupWindow.xaml src/ZprtNextGen.App/StartupWindow.xaml.cs src/ZprtNextGen.App/App.xaml.cs
git commit -m "perf(portable): extract engine off the UI thread with startup window"
```

---

### Task 8: Финальная проверка и коммит спеки

**Files:**
- Modify: `docs/superpowers/specs/2026-11-19-settings-sidebar-design.md` (статус уже обновлён)
- Add: `docs/superpowers/plans/2026-11-19-settings-sidebar.md`

- [ ] **Step 1: Прогнать всё**

```powershell
cd 'C:\Users\Machine\Documents\ZPRT_NEXT_GEN'
dotnet test tests\ZprtNextGen.Core.Tests\ZprtNextGen.Core.Tests.csproj -c Debug
dotnet test tests\ZprtNextGen.App.Tests\ZprtNextGen.App.Tests.csproj -c Debug
dotnet build src\ZprtNextGen.App\ZprtNextGen.App.csproj -c Debug
```

Expected: Core `193/193`, App `55/55`, сборка без ошибок.

- [ ] **Step 2: Проверить диффом, что ничего лишнего**

Run: `git status --short` и `git diff --stat`
Expected: только файлы из задач 1–7 плюс спека и план.

- [ ] **Step 3: Коммит документации**

```powershell
git add docs/superpowers/specs/2026-11-19-settings-sidebar-design.md docs/superpowers/plans/2026-11-19-settings-sidebar.md
git commit -m "docs: approve settings sidebar design and implementation plan"
```

---

## Проверка плана (self-review)

- [x] **Покрытие спеки:** разделы (Task 5), portable-скрытие движка (Task 5, существующие биндинги `IsStandard`/`IsPortable`), фикс игрового профиля (Task 1), кэш автозапуска (Task 2), адаптивный тест (Task 6), portable-распаковка (Task 7), «О программе» (Task 3).
- [x] **Шаги:** каждый тестовый шаг содержит код и команду; каждый шаг реализации называет файл и сигнатуру.
- [x] **Согласованность типов:** `SettingsSection` используется в Task 3 и Task 5; `IsManualMode` — в Task 4 и Task 5; `pollInterval`/`settleDelay` — в Task 6.
- [x] **Review Focus:** все пять пунктов привязаны к тестам/ручным шагам (Task 7, Task 6, Task 1, Task 2).
- [x] **Пропорция:** план не дублирует весь XAML и не содержит тел методов, кроме мест, где алгоритм не выводится из сигнатур (ожидание движка, запуск portable).
