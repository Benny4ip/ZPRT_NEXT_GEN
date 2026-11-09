# AGENTS.md

Краткая карта проекта для ИИ-агентов и людей. Подробная архитектура — [`docs/CODE_MAP.md`](docs/CODE_MAP.md).
Свежий список типов, методов и XAML-ключей — `.ai/code-index.md` (генерируется, в git не хранится).

## Что это

`ZPRT_NEXT_GEN` — русскоязычная WPF-оболочка (net10.0-windows) вокруг `winws.exe` из
[`Flowseal/zapret-discord-youtube`](https://github.com/Flowseal/zapret-discord-youtube).
Приложение само запускает движок с выбранным пресетом, показывает статус, проверяет связь,
умеет жить в трее и добавляться в автозапуск. Сторонних NuGet-пакетов нет, кроме xUnit в тестах.

## Быстрые команды

```powershell
# сборка и тесты (121 тест: 29 App + 92 Core)
dotnet test -c Release

# portable-сборка на Рабочий стол (сначала закрыть запущенный ZprtNextGen!)
dotnet publish src/ZprtNextGen.App -c Release -o "C:\Users\Machine\Desktop\ZPRT_NEXT_GEN"

# обновить индекс кода после правок
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Index-Code.ps1

# импорт пресетов из папки движка
tools\Import-Presets.ps1 -Engine <путь-к-движку> -Out presets\presets.json
```

Проверка результата после `publish`: существуют `presets\presets.json` и `engine\bin\winws.exe`.
Журнал ошибок: `%APPDATA%\ZPRT_NEXT_GEN\logs\app-YYYYMMDD.log` (пишется только при ошибках).

## Где что менять

| Задача | Файлы |
|---|---|
| Главное окно, кнопка обхода, шестёрка настроек | `src/ZprtNextGen.App/MainWindow.xaml` (+ `.xaml.cs`) |
| Логика запуска/остановки, статусы, связь, обновления, тест пресетов | `src/ZprtNextGen.App/ViewModels/MainViewModel.cs` |
| Внешний вид: цвета, кнопки, переключатели, Expander | `src/ZprtNextGen.App/Resources/Brand.xaml`, `Theme.xaml` |
| Блок состояния (пульс, кольцо прогресса, мерцание) | `src/ZprtNextGen.App/Controls/MeditationView.xaml` (+ `.xaml.cs`) |
| Прогресс кольца по состоянию | `src/ZprtNextGen.Core/Presentation/MeditationProgress.cs` |
| Выбор пресетов | `src/ZprtNextGen.App/Controls/PresetSelector.xaml` |
| Отдельное окно настроек | `src/ZprtNextGen.App/SettingsWindow.xaml` (+ `.xaml.cs`) |
| Окно «О программе» (включая Telegram-ссылку) | `src/ZprtNextGen.App/AboutWindow.xaml` (+ `.xaml.cs`) |
| Процесс `winws`, чужие экземпляры | `src/ZprtNextGen.Core/Process/EngineProcessManager.cs` |
| Пресеты и аргументы `winws` | `src/ZprtNextGen.Core/Presets/*` |
| Настройки пользователя | `src/ZprtNextGen.Core/Settings/*` |
| Состояние анимации по статусу/связи | `src/ZprtNextGen.Core/Presentation/AnimationStateResolver.cs` |
| Версии, релизы GitHub, обновление движка и программы | `src/ZprtNextGen.Core/Updates/*` |
| Тесты | `tests/ZprtNextGen.App.Tests`, `tests/ZprtNextGen.Core.Tests` |

## Инварианты и грабли

- **Весь UI — по-русски.** Новые строки не оставлять на английском.
- **Пресеты — не хардкод.** Каталог читается из `presets/presets.json`; движок ищется в `engine\bin`.
- **`IsRunning` — вычисляемое свойство.** При смене статуса движка VM обязана явно уведомить
  `PropertyChanged(nameof(IsRunning))`, иначе кнопка не переключится на «Выключить обход».
- **Не задавайте `Content` кнопки локально**, если его меняет `DataTrigger`: локальное значение
  перекрывает сеттер стиля. `Content` задаётся сеттером в `Style`.
- **`ScrollIntoView` нельзя вызывать внутри `CollectionChanged`** — только через
  `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)`, иначе падает `ItemContainerGenerator`.
- **Запуск не ждёт проверку связи.** После старта `winws` проверка идёт в фоне; остановка должна
  работать всегда, даже пока проверка не завершилась.
- **`StaticResource` не любит ссылки вперёд** в `ResourceDictionary`. Новые стили добавлять после
  тех, на которые они ссылаются (иначе `XamlParseException` при раскрытии настроек).
- **WPF-поток.** События движка приходят из фонового потока — обновлять UI только через `IUiDispatcher`.
- **Не трогать `bin/`, `obj/`, `publish/`, `.ai/`** — это артефакты.
- **PowerShell 5.1:** нет `&&`, индексов `[^1]`, функции вызываются без `()`; `.ps1` с кириллицей
  сохранять в UTF-8 **с BOM**.

## Тесты

`dotnet test -c Release` — 81 тест. Фейки лежат в `tests/ZprtNextGen.App.Tests/Fakes`.
Для проверки гонок у `FakeConnectivity` есть `Gate` (зависшая проверка связи).
Новую логику ViewModel проверять тестом на наблюдаемое поведение (команды, статусы, уведомления),
а не на внутренние поля.

## Релиз

Только вручную: GitHub Actions → `release` → `workflow_dispatch`, либо
`tools\Pack-Release.ps1` после `dotnet publish`. В git — только исходники.
