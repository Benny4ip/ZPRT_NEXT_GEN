# Карта кода ZPRT_NEXT_GEN

Документ для быстрого входа в проект. Обновлять при изменении архитектуры.
Список типов и методов с номерами строк генерируется отдельно: `tools\Index-Code.ps1` → `.ai/code-index.md`.

## Слои

```
ZprtNextGen.App (WPF, net10.0-windows)
  ├── MainWindow / AboutWindow            разметка и code-behind
  ├── Controls/                           MeditationView, PresetSelector
  ├── ViewModels/                         MainViewModel и команды
  ├── Services/                           трей, единственный экземпляр, диспетчер UI
  ├── Converters/                         XAML-конвертеры
  └── Resources/                          Brand.xaml (палитра), Theme.xaml (стили)

ZprtNextGen.Core (net10.0, без WPF)
  ├── Presets/                            каталог, импорт, аргументы winws
  ├── Process/                            жизненный цикл winws
  ├── Diagnostics/                        проверка доступности сервисов
  ├── Presentation/                       состояние индикатора (MeditationState)
  ├── Settings/                           settings.json
  ├── Startup/                            автозапуск через schtasks
  └── Logging/                            файловый журнал
```

Зависимость односторонняя: `App → Core`. В `Core` нет ссылок на WPF/WinForms.

## Поток данных

1. **Старт.** `App.OnStartup` проверяет единственный экземпляр и права администратора,
   загружает `settings.json`, каталог `presets\presets.json`, находит движок в `engine\bin`,
   собирает сервисы и создаёт `MainViewModel` + `MainWindow` + `TrayService`.
2. **Пресеты.** `PresetCatalogLoader` читает `presets.json` → `MainViewModel.Groups`
   (`PresetGroupViewModel`) → `PresetSelector`. Выбор сохраняется в настройках.
3. **Запуск обхода.** `MainViewModel.ToggleAsync` → `EngineProcessManager.StartAsync`
   с аргументами из `PresetArgumentBuilder` (плюс игровой фильтр) → `winws.exe`.
   После запуска проверка связи идёт **в фоне**, UI не блокируется.
4. **Статусы.** `EngineProcessManager.StatusChanged` → `MainViewModel.OnEngineStatusChanged`
   → `IsRunning`, `StatusText`, команды. `IsRunning` — вычисляемое, уведомляется явно.
5. **Связь.** `ConnectivityService.CheckAsync` → `ConnectivityReport` → строки в панели
   «Связь с сервисами» + `AnimationStateResolver.Resolve(status, report)` →
   `MeditationView.State`.
6. **Индикатор.** `MeditationState` (Stopped / Connecting / Connected / Error) выбирает
   раскадровку в `MeditationView.xaml`. При `ReduceMotion` — статичный кадр.

## Ключевые типы

| Тип | Ответственность |
|---|---|
| `MainViewModel` | Единая точка состояния UI: команды, статусы, пресеты, связь, настройки |
| `MainViewModelDependencies` | Явная композиция зависимостей (без DI-контейнера) |
| `EngineProcessManager` | Запуск/остановка `winws`, чтение вывода, чужие экземпляры |
| `EngineLocator` / `EngineValidation` | Поиск и проверка `winws.exe` и обязательных файлов |
| `PresetCatalogLoader` / `Preset` | Каталог пресетов и модель пресета |
| `PresetArgumentBuilder` | Сборка аргументов командной строки `winws` |
| `ConnectivityService` | HTTP/сокет-проверки Discord/YouTube с таймаутом |
| `AnimationStateResolver` | Чистая функция «статус + отчёт → MeditationState» |
| `SettingsStore` | Чтение/запись `%APPDATA%\ZPRT_NEXT_GEN\settings.json` |
| `ScheduledTaskAutostart` | Автозапуск через планировщик Windows |
| `AppLogger` | Журнал только при ошибках, `%APPDATA%\ZPRT_NEXT_GEN\logs` |
| `AsyncRelayCommand` | Команда с защитой от повторного запуска |
| `IUiDispatcher` / `WpfDispatcher` | Маршалинг событий движка в UI-поток |

## Рецепты изменений

### Добавить/изменить пресет
Правьте `presets/presets.json` (или импортируйте из движка через `tools\Import-Presets.ps1`).
Модель и парсер: `Core/Presets/Preset*.cs`. UI-группы строятся автоматически.

### Изменить внешний вид
- Цвета — только `Resources/Brand.xaml` (кисти ссылаются на цвета).
- Кнопки, поля, переключатели, Expander — `Resources/Theme.xaml`.
- Компоновка главного окна — `MainWindow.xaml`.
- Блок состояния — `Controls/MeditationView.xaml` (+ анимации в `UserControl.Resources`).

### Добавить настройку
1. Поле в `Core/Settings/AppSettings.cs` (record + значение по умолчанию).
2. Свойство в `MainViewModel` с сохранением через `SaveSettings()`.
3. Контрол в `MainWindow.xaml` (секция «Настройки»).
4. Тест в `tests/ZprtNextGen.App.Tests/ViewModels/MainViewModelTests.cs`.

### Добавить проверку связи
Цели задаются в `ConnectivityService.CreateDefault()` (`ConnectivityTarget`).
Правило «связь подтверждена» — в `ConnectivityReport.IsConnected`, тесты — в
`tests/ZprtNextGen.Core.Tests/Diagnostics/ConnectivityServiceTests.cs`.

### Изменить индикатор состояния
`MeditationView.xaml`: раскадровки `StoppedStoryboard`, `ConnectingStoryboard`,
`ConnectedStoryboard`, `ErrorStoryboard`; статичный режим — `ApplyReduceMotionStatic()`
в code-behind. Имена элементов должны совпадать с `Storyboard.TargetName`.

## Тесты

- `tests/ZprtNextGen.App.Tests` — ViewModel, команды, сервисы приложения; фейки в `Fakes/`.
- `tests/ZprtNextGen.Core.Tests` — парсеры, аргументы, процесс, настройки, автозапуск.
- Быстрый прогон: `dotnet test -c Release`.

## Соглашения

- Язык интерфейса и сообщений — русский; идентификаторы кода — английские.
- Комментарии — по делу, на русском, только там, где неочевидна причина решения.
- Никаких сторонних пакетов в рантайме; тесты — xUnit.
- Сборка релиза — self-contained single-file `win-x64`, манифест `requireAdministrator`.
