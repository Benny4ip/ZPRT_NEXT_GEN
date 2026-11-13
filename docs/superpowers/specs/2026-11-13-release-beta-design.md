# ZPRT_NEXT_GEN — релизная бета 1.0.0-beta.1

**Date:** 2026-11-13  
**Status:** Approved — 2026-11-13  
**Подход:** B — сервисный слой в Core + два VM в App  
**База:** `docs/superpowers/specs/2026-11-08-zprt-next-gen-design.md`,
`docs/superpowers/specs/2026-11-09-settings-updates-design.md`

## 1. Intent

Превратить текущую оболочку в продукт «для чайников»: пользователь скачал, нажал одну
большую кнопку, подождал подбор, получил работающие YouTube и Discord — без списков,
логов и технических терминов на главном экране. Настройки остаются для тех, кому нужно
вручную выбрать обход, добавить свой пресет или включить игровой профиль.

Главный экран — это светофор из одной кнопки:

- **красная** — выключено, надо нажать;
- **янтарная** — идёт подбор или подключение, видно прогресс;
- **зелёная** — всё работает, нарисована галочка.

Под капотом приложение само перебирает все 22 обхода, проверяет **оба** сервиса
(YouTube и Discord), выбирает самый быстрый рабочий вариант, сохраняет его и при
следующих запусках поднимает сразу, без перебора.

## 2. Goals

- **G1. Один клик.** Первый запуск: одна кнопка → автоподбор всех альтов → понятный
  прогресс → автоматическое подключение лучшего.
- **G2. Оба сервиса.** Альт считается рабочим только если доступны и YouTube, и Discord.
  Задержка измеряется по каждому сервису; выбирается вариант с лучшим худшим показателем.
- **G3. Повторный запуск — быстро.** Сохранённый профиль поднимается сразу, без перебора.
  При неудаче — автоматический перебор заново.
- **G4. Понятный светофор.** Красный / янтарный / зелёный, крупные тексты, анимированная
  галочка, подсказки человеческим языком.
- **G5. Нетерпеливый пользователь.** Как только найден первый рабочий альт, появляется
  кнопка «Остановить и применить лучший» — не нужно ждать все 22.
- **G6. Fix Discord.** Безопасная очистка кэша без выхода из аккаунта; в диагностике —
  отдельный полный сброс с подтверждением.
- **G7. Трей и автозапуск.** Закрытие в трей по настройке; автозапуск через Планировщик
  задач со стартом свёрнутым и поднятием последнего рабочего альта.
- **G8. Две сборки.** Standard (exe + `engine/`) и Portable (один exe со встроенным
  движком), сборка одной командой.
- **G9. Конфиг v2.** `config.json` с миграцией текущего `settings.json`, без потери
  выбранного пресета, автозапуска и пути к движку.
- **G10. Дружелюбные ошибки.** Вместо стектрейсов — короткий текст и кнопка «Что делать».
- **G11. Никаких лишних окон.** На главном экране только центр, донат, Fix Discord и
  настройки.
- **G12. Русский интерфейс.** Все новые строки — по-русски.

## 3. Non-goals

- Установщик/MSI, автообновление без действия пользователя, телеметрия.
- Собственная реализация `winws` или замена WinDivert.
- Поддержка macOS/Linux.
- Полноценный игровой VPN, выбор игр, отдельные профили для каждой игры.
- Автоматическое обновление встроенного движка в Portable-сборке (см. 7.11).
- Переделка upstream-скриптов `general*.bat`, `service.bat`, `test zapret.ps1`.

## 4. Anti-goals

- Потеря текущих настроек при миграции.
- Запуск двухминутного перебора при каждом старте.
- Перебор, который оставляет `winws` запущенным.
- Подмена рабочего обхода без ведома пользователя в ручном режиме.
- Удаление cookies/авторизации Discord в обычном «Fix Discord».
- Блокировка UI на время теста, скачивания или очистки кэша.
- Кириллица в путях/аргументах, ломающая `ProcessStartInfo`.
- Новые NuGet-пакеты.

## 5. Constraints

- .NET 10 WPF, `Core` + `App`, xUnit. Без новых NuGet.
- Все тексты интерфейса — русский; код и комментарии — английский.
- Приложение требует прав администратора (`requireAdministrator`).
- Движок ищется в `engine\bin`; в репозитории его нет.
- PowerShell 5.1: без `&&`, `[^1]`, `sed`; `.ps1` с кириллицей — UTF-8 с BOM.
- `dotnet build/test/publish` запускать последовательно (бывает `CS2012`).
- Upstream-скрипты не правим.
- GitHub может быть недоступен; приложение продолжает работать.
- Тесты не требуют сети, администратора и реального `winws`.

## 6. Зафиксированные решения

| № | Решение |
|---|---|
| 6.1 | Новый файл настроек — `%APPDATA%\ZPRT_NEXT_GEN\config.json`, `schemaVersion: 2`. Старый `settings.json` читается и мигрирует; оригинал не удаляем, рядом создаём `.bak`. Существующая установка остаётся в **ручном** режиме с текущим пресетом; чистая установка — в **авто**. |
| 6.2 | На первом автоподборе проверяются **все 22** пресета каталога, плюс пользовательские (если есть). Прогресс, отмена и досрочное применение лучшего обязательны. |
| 6.3 | Рабочий альт = доступны **и YouTube, и Discord**. `Success` пресета = `YouTubeOk && DiscordOk`. Оценка = `max(youtubeMs, discordMs)`, тай-брейк — сумма. Обе задержки сохраняются. |
| 6.4 | Обычный `Fix Discord` не удаляет `Cookies`, `Local Storage`, `Session Storage`, `IndexedDB`. Чистятся только кэш и сетевое состояние. Полный сброс — отдельно в диагностике с подтверждением. |
| 6.5 | Процессы Discord завершаются по пути установки, а не только по имени `Update.exe`. `taskkill /IM Update.exe` без фильтра не используем. |
| 6.6 | `winws` — не служба. При выходе останавливаем принадлежащий процесс; WinDivert выгружается вместе с ним. Чужие экземпляры не убиваем без подтверждения. |
| 6.7 | Автозапуск — Task Scheduler, XML-задача с аргументами `--tray --autoconnect`, `RunLevel=HighestAvailable`, `LogonTrigger`. |
| 6.8 | Если профиля нет, автозапуск **не** запускает перебор молча: приложение стартует в трее и показывает уведомление. Если профиль есть, но не работает — запускается восстановление/перебор. |
| 6.9 | Фоновая проверка: 60 с, 2 неудачи подряд, затем перезапуск сохранённого альта, затем полный перебор. Cooldown 5 минут. В ручном режиме — только уведомление. |
| 6.10 | Portable распаковывается в `%LOCALAPPDATA%\ZPRT_NEXT_GEN\engine\<версия>-<hash8>`. Кэш переживает перезапуск; при смене версии старые папки удаляются best-effort. Конфиг — в `%APPDATA%`. |
| 6.11 | «Игровой профиль» — переключатель `All`/`Disabled`; тонкая настройка `Tcp`/`Udp` остаётся в «Дополнительно». В конфиге хранится только `gameFilter`. |
| 6.12 | Пользовательские пресеты — в `config.json`, не в `presets/presets.json`. Подмешиваются в каталог как группа «Мои пресеты». |
| 6.13 | Логи и проверка связи — в «Настройки → Диагностика». На главном экране логов нет. |
| 6.14 | Обновление движка, тест и подключение сериализуются общим шлюзом операций. |
| 6.15 | Версия беты — `1.0.0-beta.1`; GitHub-релиз — prerelease. |
| 6.16 | Кнопка «Поддержи проект денежкой» — в правом верхнем углу, ссылка `https://www.donationalerts.com/r/benny_chip` через `Process.Start` с `UseShellExecute = true`. |
| 6.17 | `Build-Release.ps1` принимает `-EnginePath` и умеет сам искать `engine` рядом с проектом/на Рабочем столе. |

## 7. Архитектура

### 7.1. Слои

```text
Core
  Connection     — состояние, автоподбор, восстановление, шлюз операций
  Diagnostics    — проверка YouTube/Discord по группам
  Discord        — безопасная очистка и перезапуск
  Engine         — поиск движка, portable-распаковка
  Presets        — каталог, пользовательские пресеты, тестер
  Settings       — config.json, миграция, нормализация
  Startup        — XML-задача автозапуска
App
  MainViewModel  — главный экран: светофор, кнопки, донат, Fix Discord
  SettingsViewModel — настройки, тесты, пресеты, обновления, диагностика
  ConnectionOrb  — визуальный контрол главной кнопки
  TrayService    — меню трея и balloon-уведомления
  App            — composition root, аргументы запуска, жизненный цикл
```

Без новых проектов и DI-контейнера. Зависимости — через конструкторы, как сейчас.
`MainViewModel` больше не хранит настройки, обновления и таблицу тестов: это уходит в
`SettingsViewModel`. Тяжёлая логика подключения — в `ConnectionCoordinator`, а не в UI.

### 7.2. Состояния подключения

```csharp
public enum ConnectionState
{
    Disconnected,
    Selecting,      // автоподбор всех альтов
    Connecting,     // запуск выбранного/сохранённого
    Connected,
    Stopping,
    Error
}

public enum ConnectionIssue
{
    None,
    EngineMissing,
    ForeignInstance,
    NoInternet,
    AllPresetsFailed,
    SavedProfileFailed
}
```

Снимок состояния для UI:

```csharp
public sealed record ConnectionSnapshot(
    ConnectionState State,
    ConnectionIssue Issue,
    string? PresetId,
    string? PresetName,
    long? LatencyMs,
    long? YoutubeMs,
    long? DiscordMs,
    int TestedCount,
    int TotalCount,
    double Progress,
    string? BestPresetName,
    long? BestLatencyMs,
    bool HasQualifiedCandidate,
    string? ErrorMessage);
```

`ConnectionCoordinator` — единственный владелец переходов:

```csharp
Task InitializeAsync(CancellationToken ct);
Task ToggleAsync(CancellationToken ct);
Task ConnectAsync(CancellationToken ct);
Task DisconnectAsync(CancellationToken ct);
Task RetestAsync(CancellationToken ct);
Task StopAndApplyBestAsync(CancellationToken ct);
Task CancelSelectionAsync(CancellationToken ct);
event EventHandler<ConnectionSnapshot> StateChanged;
```

Правила:

- `Disconnected` + клик → `ConnectAsync`.
- `Connected` + клик → `DisconnectAsync`.
- `Selecting` + клик по орбу → ничего (орб не кликабелен), отмена — отдельной кнопкой.
- `Connecting` + отмена → остановить и вернуться в `Disconnected`/предыдущий профиль.
- Все операции, меняющие `winws`, проходят через `SemaphoreSlim(1,1)`.
- `ConnectionCoordinator` не знает о WPF; UI подписывается на `StateChanged` через
  `IUiDispatcher`.

### 7.3. Алгоритм автоподбора

0. Быстрая проверка интернета по нейтральным узлам (не YouTube/Discord):
   `https://www.msftconnecttest.com/connecttest.txt` и
   `https://captive.apple.com/hotspot-detect.html`. Если оба недоступны —
   `Error/NoInternet`, не запускаем перебор 22 альтов.
1. Проверить движок. Если нет — `Error/EngineMissing`, кнопка «Открыть настройки».
2. Если запущен чужой `winws` — спросить «Остановить чужой обход и продолжить?».
   Отказ → `Error/ForeignInstance`, ничего не трогаем.
3. Собрать список: все пресеты каталога + пользовательские, естественная сортировка.
4. Для каждого пресета:
   - собрать аргументы через `PresetArgumentBuilder`;
   - запустить `winws` с рабочей папкой `engine\bin`;
   - подождать `StartupDelay` (1,5 с);
   - если процесс не `Running` — результат «не запустился», перейти к следующему;
   - проверить YouTube и Discord параллельно (таймаут 4 с на цель);
   - посчитать `youtubeMs`, `discordMs`, `scoreMs`, `qualified`;
   - остановить процесс в `finally`;
   - отправить прогресс: `TestedCount`, `BestPresetName`, `BestLatencyMs`.
5. После каждого результата обновлять лучший. Если появился первый `qualified`,
   `HasQualifiedCandidate = true` — UI показывает «Остановить и применить лучший».
6. Досрочная остановка: отменить оставшиеся тесты, взять лучший из уже проверенных.
7. В конце — отсортировать `qualified` по `scoreMs`, затем по сумме, затем по id.
8. Попробовать подключить лучший. Если не поднялся/не подтвердился — следующий из
   списка (максимум 3 попытки), чтобы не тратить ещё 2 минуты.
9. Сохранить `autoProfile`, перейти в `Connected`.

Контракты:

```csharp
public sealed record AutoSelectProgress(
    int TestedCount,
    int TotalCount,
    PresetTestResult? LastResult,
    PresetTestResult? BestResult,
    bool HasQualifiedCandidate);

public sealed record AutoSelectOutcome(
    IReadOnlyList<PresetTestResult> Results,
    PresetTestResult? Best,
    bool Canceled);

Task<AutoSelectOutcome> TestAsync(
    IReadOnlyList<Preset> presets,
    string engineRoot,
    GameFilterMode filter,
    IProgress<AutoSelectProgress> progress,
    CancellationToken ct);
```

Параметры (`AutoSelectPolicy`):

```csharp
StartupDelay        = TimeSpan.FromMilliseconds(1500);
CheckTimeout        = TimeSpan.FromSeconds(4);
VerifyDelay         = TimeSpan.FromMilliseconds(1500);
MaxStartAttempts    = 3;
HealthInterval      = TimeSpan.FromSeconds(60);
HealthFailures      = 2;
HealthCooldown      = TimeSpan.FromMinutes(5);
```

Важно: тестер не должен останавливать движок, если он не его; `StopAsync` вызывается
только для принадлежащего процесса.

### 7.4. Проверка YouTube и Discord

```csharp
public enum ServiceKind { YouTube, Discord }

public sealed record ConnectivityTarget(
    string Id,
    string DisplayName,
    string Url,
    ServiceKind Service,
    bool IsPrimary);
```

Цели:

| Сервис | Id | URL | Primary |
|---|---|---|---|
| YouTube | `youtube` | `https://www.youtube.com/generate_204` | да |
| YouTube | `youtube-cdn` | `https://redirector.googlevideo.com/generate_204` | да |
| YouTube | `youtube-img` | `https://i.ytimg.com/generate_204` | нет |
| Discord | `discord-api` | `https://discord.com/api/v10/gateway` | да |
| Discord | `discord-gateway` | `https://gateway.discord.gg/?v=10&encoding=json` | да |
| Discord | `discord-cdn` | `https://cdn.discordapp.com/generate_204` | нет |

Правила:

- HTTP-статус `< 500` — цель доступна. `404` — это нормальный ответ, а не блокировка.
- `YouTubeOk` = есть успешная primary-цель YouTube.
- `DiscordOk` = есть успешная primary-цель Discord.
- `BothOk = YouTubeOk && DiscordOk`.
- `youtubeMs` / `discordMs` — минимум по успешным primary-целям сервиса.
- `scoreMs = max(youtubeMs, discordMs)`.
- Таймаут/исключение — неуспех, но не падение всего прогона.
- Все цели проверяются параллельно; общий таймаут 4 с.
- `*.googlevideo.com` без конкретного видео-ID проверяем через
  `redirector.googlevideo.com`; этого достаточно для доступности CDN.

`ConnectivityReport` получает свойства `YouTubeOk`, `DiscordOk`, `BothOk`,
`YouTubeLatencyMs`, `DiscordLatencyMs`, `ScoreMs`. Старое `IsConnected` остаётся для
совместимости, но новый код использует `BothOk`.

Для предварительной проверки «интернет вообще есть» используется отдельный метод:

```csharp
Task<bool> CheckInternetAsync(CancellationToken ct);
```

Нейтральные цели: `https://www.msftconnecttest.com/connecttest.txt` и
`https://captive.apple.com/hotspot-detect.html`. Если обе недоступны — `NoInternet`,
перебор не запускаем. Если нейтральная цель работает, а YouTube/Discord нет — это
нормальная ситуация для обхода, запускаем подбор.

`PresetTestResult` расширяется:

```csharp
public sealed record PresetTestResult(
    string PresetId,
    string Name,
    bool Success,          // = Qualified, оставлено для совместимости
    long? YoutubeMs,
    long? DiscordMs,
    bool YoutubeOk,
    bool DiscordOk,
    bool TimedOut,
    string? Detail)
{
    public bool Qualified => YoutubeOk && DiscordOk;
    public long? ScoreMs => YoutubeMs is { } y && DiscordMs is { } d ? Math.Max(y, d) : null;
    public long? ElapsedMs => ScoreMs; // старый UI-контракт
}
```

`Success` и `ElapsedMs` сохраняются, чтобы не ломать существующие тесты и таблицу
настроек; новый код использует `Qualified` и `ScoreMs`.

### 7.5. Подключение сохранённого профиля

1. Взять `autoProfile` из конфига.
2. Проверить, что пресет ещё существует в каталоге.
3. Запустить, подождать 1,5 с, проверить оба сервиса.
4. Если не вышло — один повторный запуск.
5. Если снова не вышло — перейти в `Selecting` с баннером
   «Сохранённый обход перестал работать — ищем новый».
6. Если пресета нет в каталоге (обновился движок) — сразу перебор.

### 7.6. Фоновая проверка

`HealthMonitor` — `PeriodicTimer` на 60 с:

- работает только в `Connected` + `auto` + не занят шлюз;
- проверяет оба сервиса, не останавливая движок;
- успех — сброс счётчика;
- две неудачи подряд — `RecoverAsync`:
  1. остановить;
  2. запустить сохранённый альт;
  3. проверить;
  4. если не вышло — полный автоподбор;
  5. если и это не вышло — `Error/AllPresetsFailed`, cooldown 5 минут.
- в ручном режиме — только balloon «Discord/YouTube перестали отвечать. Проверь обход.»
- во время `Selecting`, `Connecting`, `FixDiscord`, обновления движка — пропуск.

### 7.7. Шлюз операций

```csharp
public interface IEngineOperationGate
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken ct);
}
```

Через него проходят:

- подключение/отключение;
- автоподбор;
- тест из настроек;
- обновление движка;
- Fix Discord (не обязательно, но полезно для статуса «занято»).

UI показывает `IsBusy`, кнопки блокируются, статус объясняет, что происходит.

### 7.8. Fix Discord

`DiscordFixService`:

```csharp
Task<DiscordFixResult> FixAsync(bool fullReset, CancellationToken ct);
```

Обычный режим (без выхода из аккаунта):

- завершить `Discord.exe`, `DiscordPTB.exe`, `DiscordCanary.exe`, `Update.exe`;
- фильтр по пути: только процессы под `%LOCALAPPDATA%\Discord`,
  `%LOCALAPPDATA%\DiscordPTB`, `%LOCALAPPDATA%\DiscordCanary`;
- подождать завершения до 5 с;
- удалить содержимое:
  - `%APPDATA%\Discord\Cache`;
  - `%APPDATA%\Discord\Code Cache`;
  - `%APPDATA%\Discord\GPUCache`;
  - `%APPDATA%\Discord\DawnCache`;
  - `%APPDATA%\Discord\ShaderCache`;
  - `%APPDATA%\Discord\Service Worker`;
  - `%APPDATA%\Discord\Network Persistent State`.
- **не трогать**: `Cookies`, `Local Storage`, `Session Storage`, `IndexedDB`;
- запустить `%LOCALAPPDATA%\Discord\Update.exe --processStart Discord.exe`;
- fallback: найти `%LOCALAPPDATA%\Discord\app-*\Discord.exe` и запустить;
- если Discord не установлен — вернуть `NotInstalled`, показать понятный текст.

Полный сброс (только из «Настройки → Диагностика»):

- дополнительно удалить `Cookies`, `Local Storage`, `Session Storage`, `IndexedDB`,
  `Network`;
- перед этим показать диалог:
  «Это удалит cookies и локальные данные Discord. Придётся войти в аккаунт заново.
  Продолжить?»;
- после перезапуска пользователь увидит экран входа.

Уведомление об успехе:

- balloon в трее: «Discord исправлен: кэш очищен, клиент перезапущен.»;
- на главном экране кнопка `Fix Discord` на 3 секунды меняет подпись на «Готово ✓»;
- ошибка — короткий текст в статусе/диалоге, подробности в лог.

### 7.9. Конфиг и миграция

`ConfigStore`:

- путь `%APPDATA%\ZPRT_NEXT_GEN\config.json`;
- атомарная запись (как сейчас: temp + move);
- потокобезопасность;
- повреждённый JSON → `.bad`, значения по умолчанию, приложение запускается;
- неизвестная будущая `schemaVersion` → `.bak`, значения по умолчанию, предупреждение.

Порядок загрузки:

1. Есть `config.json` → читаем, нормализуем.
2. Нет `config.json`, есть `settings.json` → читаем старую плоскую модель, мапим в v2,
   пишем `config.json`, копируем старый в `settings.json.bak` (оригинал оставляем).
3. Ничего нет → значения по умолчанию, `mode = auto`, `autoProfile = null`.

Маппинг:

| Старое | Новое |
|---|---|
| `EnginePath` | `engine.path` |
| `PresetId` | `connection.manualPresetId`, `connection.mode = manual` |
| `GameFilter` | `gameFilter` |
| `Autostart` | `behavior.autostart` |
| `MinimizeToTray` | `behavior.minimizeToTray` |
| `Theme` | `behavior.theme` |
| `ReduceMotion` | `behavior.reduceMotion` |
| `CollapsedGroups` | `ui.collapsedGroups` |

Почему существующая установка остаётся в `manual`: у пользователя уже выбран
`general-alt2`; нельзя молча заменить его другим обходом после обновления.

### 7.10. Пользовательские пресеты

```csharp
public sealed record CustomPreset(
    string Id,          // custom:<guid>
    string Name,
    string Description,
    string Arguments);
```

- хранятся в `config.json`;
- валидация: непустые имя и аргументы, уникальный id;
- подмешиваются в каталог группой «Мои пресеты»;
- сортировка — естественная;
- поддерживают `{{BIN}}`, `{{LISTS}}`, `{{GameFilterTCP}}`, `{{GameFilterUDP}}`;
- при обновлении движка не затрагиваются;
- ошибка аргументов показывается в результате теста, а не роняет приложение.

### 7.11. Portable-движок

`PortableEngineInstaller`:

1. Проверяет наличие embedded resource `ZprtNextGen.Engine.zip`.
2. Если ресурса нет — это Standard, ничего не делает.
3. Считает SHA-256 ресурса, получает hash8.
4. Целевая папка:
   `%LOCALAPPDATA%\ZPRT_NEXT_GEN\engine\<assemblyVersion>-<hash8>`.
5. Если есть `.ready` и обязательные файлы — использует папку.
6. Иначе распаковывает во временную папку рядом, проверяет обязательные файлы,
   переименовывает в целевую, пишет `.ready`.
7. Удаляет старые `engine-*` папки best-effort.
8. Возвращает `EngineRoot` и `PresetsPath`.

Переменная `ZPRT_ENGINE_CACHE_ROOT` позволяет подменить корень кэша в тестах и
build-скрипте, чтобы не мусорить в реальном `%LOCALAPPDATA%`.

Обязательные файлы:

```text
bin\winws.exe
bin\WinDivert.dll
bin\WinDivert64.sys
lists\list-general.txt
presets\presets.json
```

При полном выходе: остановить `winws`, не удалять кэш. Иначе каждый автозапуск будет
распаковывать движок заново.

**Обновление движка в Portable:** кнопка «Обновить движок» недоступна с пояснением
«Движок встроен в portable-версию. Обновите программу целиком.» Это избегает ситуации,
когда обновлённый кэш затирается встроенной версией при следующем запуске. В Standard
обновление движка работает как раньше.

### 7.12. Автозапуск

XML-задача:

```xml
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>C:\path\ZprtNextGen.exe</Command>
      <Arguments>--tray --autoconnect</Arguments>
    </Exec>
  </Actions>
</Task>
```

- `EnableAsync`: пишет временный XML (UTF-16), вызывает
  `schtasks /Create /TN "ZPRT_NEXT_GEN" /XML <file> /F`.
- `IsEnabledAsync`: `schtasks /Query /TN ... /XML`, проверяет `Command` и `Arguments`.
- `DisableAsync`: `schtasks /Delete /TN ... /F`.
- Ошибка создания задачи показывается в настройках понятным текстом.
- При `--tray` окно не показывается; при `--autoconnect` вызывается
  `ConnectionCoordinator.ConnectAsync`.
- Если профиля нет — только balloon с подсказкой, без фонового перебора.
- Если профиль есть и не работает — запускается восстановление/перебор с balloon.

## 8. UI/UX

### 8.1. Главный экран: 4 угла + центр

Окно: 760×720, минимум 640×640, тёмная тема, без списков и логов.

```text
┌──────────────────────────────────────────────────────────────┐
│  ZPRT NEXT GEN              [♥ Поддержи проект денежкой]     │
│                                                              │
│                                                              │
│                      ┌────────────────┐                      │
│                      │                │                      │
│                      │  ConnectionOrb │                      │
│                      │                │                      │
│                      └────────────────┘                      │
│                     Обход включён                            │
│          Пользуйся YouTube и Discord в один клик             │
│              Пресет: Альтернатива 7 • 135 мс                 │
│                                                              │
│  [🔧 Fix Discord]                                ⚙ Настройки │
└──────────────────────────────────────────────────────────────┘
```

- **Центр:** `ConnectionOrb` 320×320 + статус + подзаголовок + профиль/пинг.
- **Правый верхний угол:** донат-кнопка.
- **Левый нижний угол:** `Fix Discord`.
- **Правый нижний угол:** крупная шестерёнка + подпись «Настройки».
- **Левый верхний угол:** маленький логотип/название, не отвлекает.
- Никаких панелей связи, логов, селектора пресетов и версий на главном.

### 8.2. Светофор `ConnectionOrb`

| Состояние | Цвет | Текст в орбе | Подпись снизу | Дополнительно |
|---|---|---|---|---|
| `Disconnected` | красный `#E01B2E` | `Включить` | `Нажми для запуска YouTube и Discord` | мягкое красное свечение |
| `Selecting` | янтарный `#F5A524` | `Ищем обход…` | `Проверено X из 22 · Лучший: … · … мс` | вращающееся кольцо, баннер первого запуска, кнопки |
| `Connecting` | янтарный `#F5A524` | `Подключаемся…` | `Проверяем YouTube и Discord` | пульсация кольца |
| `Connected` | зелёный `#22C55E` | `Обход включён` | `Пользуйся YouTube и Discord в один клик` | анимированная галочка, профиль/пинг |
| `Stopping` | серый/янтарный | `Выключаем…` | `Останавливаем обход` | кнопка недоступна |
| `Error` | красный `#E01B2E` | `Включить` | `Нажми для запуска YouTube и Discord` | ниже мелким красным — причина ошибки; клик повторяет попытку |
| `Error / EngineMissing` | красный `#E01B2E` | `Включить` | `Движок не найден — открой настройки` | клик открывает настройки |

Детали:

- Кольцо вокруг орба — индикатор: 0% в покое, растёт по прогрессу теста,
  заполнено на 100% при подключении.
- Внутри орба — крупная иконка питания/галочка и текст; всё масштабируется.
- При `Connected` внутри рисуется анимированная галочка: `Path` с
  `StrokeDashOffset` от длины к 0 за 450 мс. При `ReduceMotion` — сразу.
- При наведении на зелёный орб — тултип «Нажми, чтобы отключить обход»,
  `AutomationProperties.HelpText` такой же.
- При наведении на красный — «Нажми, чтобы подобрать и включить обход».
- Клик по орбу в `Disconnected` → подключение; в `Connected` → отключение;
  в `Selecting`/`Connecting` — ничего (защита от случайной отмены).
- Клавиатура: `Enter`/`Space` работают как клик; видимый фокус.
- `AutomationProperties.Name` меняется по состоянию:
  «Включить обход», «Идёт подбор обхода», «Обход включён, нажми чтобы выключить».

### 8.3. Первый автоподбор: тексты и кнопки

Над орбом или под ним — заметная плашка:

> **Ищем самый быстрый обход под твоего провайдера.**
> Пожалуйста, подожди пару минут — это нужно сделать всего один раз.

Прогресс:

> `Проверено 7 из 22 · Лучший: Альтернатива 3 · 142 мс`

Если у пользователя есть свои пресеты, вместо 22 показывается фактическое число
(например, `7 из 25`). В примерах ниже оставляем 22 как базовый случай.

Кнопки, как только найден первый рабочий вариант:

- **Primary:** `Остановить и применить лучший`
- **Secondary:** `Отмена`

Поведение:

- «Остановить и применить лучший» отменяет оставшиеся тесты, останавливает текущий
  `winws`, применяет лучший из уже проверенных, проверяет его и сохраняет профиль.
- «Отмена» останавливает тест, не сохраняет новый профиль. Если раньше был рабочий
  профиль — возвращаемся к нему; если это первый запуск — остаёмся в `Disconnected`
  с текстом «Подбор отменён. Можно попробовать снова.»
- Пока нет ни одного рабочего варианта, primary-кнопки нет — только «Отмена».
- Если все 22 проверены и рабочих нет:
  «Не удалось найти рабочий обход. Проверь, что интернет работает, и попробуй снова.»
  Кнопки: «Попробовать снова», «Настройки».
- Если найден рабочий, но пользователь не нажимал — по завершении он применяется
  автоматически.

### 8.4. Подключено

- Орб зелёный, галочка анимированная.
- Главный статус: **«Обход включён»**.
- Подзаголовок: **«Пользуйся YouTube и Discord в один клик»**.
- Ниже мелким приглушённым: `Пресет: Альтернатива 7 • 135 мс`.
- Тултип: «Нажми, чтобы отключить обход».
- Если пользователь навёл курсор, можно мягко подсветить текст «Выключить» рядом
  с орбом, но не менять основной статус.

### 8.5. Донат

- Правый верхний угол.
- Аккуратная outline-кнопка с золотым акцентом, иконка сердца/монеты (вектор).
- Текст: `Поддержи проект денежкой`.
- Тултип: `Поддержи проект денежкой`.
- Клик:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "https://www.donationalerts.com/r/benny_chip",
    UseShellExecute = true
});
```

- Ошибка открытия браузера — короткий статус, без падения.
- Ссылка не показывается на главном текстом, только кнопка.

### 8.6. Fix Discord на главном

- Левый нижний угол, вторичная кнопка с иконкой.
- Текст: `Fix Discord`.
- Тултип: `Починить вечную загрузку Discord (сброс кэша без выхода из аккаунта)`.
- На время работы: `Чиним Discord…`, кнопка disabled.
- Успех: `Готово ✓` на 3 секунды + balloon.
- Ошибка: короткий текст, подробности в логе.

### 8.7. Настройки

Окно 760×820, прокрутка, тёмная тема. Порядок секций — от простого к сложному:

1. **Как подключаться**
   - Переключатель «Автоматически подбирать лучший обход» (рекомендуется, включён
     по умолчанию для чистой установки).
   - Пояснение: «Приложение проверит все обходы и выберет самый быстрый. Это занимает
     пару минут и нужно только при первом запуске или после сбоя.»
   - Ручной режим: «Выбирать обход вручную» + `PresetSelector`/ComboBox.
   - Текущий профиль: `Альтернатива 7 · YouTube 131 мс · Discord 143 мс`.
   - Кнопка `Протестировать альты` + `Остановить`.
   - Пояснение: «Проверим все 22 обхода и покажем, где работают YouTube и Discord.
     Текущий обход будет временно выключен.»
   - Прогресс, статус, список результатов:
     `Название | YouTube ✓/— | Discord ✓/— | пинг | [Использовать]`.
   - «Использовать» ставит ручной режим и выбранный пресет; отдельная кнопка
     «Сделать автоматическим» сохраняет лучший как авто-профиль.
2. **Игровой профиль**
   - Переключатель «Игровой профиль» + пояснение «Улучшает соединение в играх.
     Включай, если играешь.»
   - «Дополнительно»: `Выключен / TCP / UDP / Все`.
3. **Мои пресеты** (Expander, по умолчанию свёрнут)
   - Список, «Добавить», «Изменить», «Удалить».
   - Редактор: имя, описание, аргументы, подсказка про плейсхолдеры.
   - Ошибки валидации — рядом с полями.
4. **Запуск и трей**
   - «Запускать вместе с Windows» + пояснение «Стартует свёрнутым в трей и сразу
     включает последний рабочий обход.»
   - «Сворачивать в трей при закрытии» (включено по умолчанию).
   - «Уменьшить анимацию».
5. **Движок и обновления**
   - Путь к движку, «Обзор…», версия.
   - «Проверить обновление», «Обновить движок» (в Portable — disabled с пояснением),
     «Обновить программу», «Открыть GitHub releases».
   - Статус и прогресс.
6. **Диагностика** (Expander)
   - «Проверить связь» (YouTube/Discord по отдельности).
   - «Открыть папку логов», «Скопировать лог».
   - «Полный сброс Discord (с выходом из аккаунта)» — красная вторичная кнопка,
     с диалогом подтверждения.
7. **О программе**
   - Версия, Telegram, лицензии.

Поведение:

- Настройки сохраняются сразу; внизу/в заголовке — «Сохранено».
- Если изменить пресет/фильтр при активном подключении, показать ненавязчивую
  плашку: «Чтобы применить, перезапусти обход» + кнопка «Перезапустить».
- Тест при активном подключении сначала спрашивает подтверждение.
- Автозапуск: при ошибке переключатель возвращается в фактическое состояние,
  показывается понятный текст.

### 8.8. Трей

Меню:

- `Развернуть`
- `Включить обход` / `Выключить обход` / `Отменить подбор` (динамически)
- `Fix Discord`
- разделитель
- `Выход`

Детали:

- Tooltip: `ZPRT NEXT GEN — обход включён (Альтернатива 7)`.
- Двойной клик и левый клик — развернуть.
- Balloon:
  - успех Fix Discord;
  - «Сохранённый обход перестал работать — ищем новый»;
  - «Не удалось найти рабочий обход»;
  - «Нет сохранённого профиля. Открой приложение и нажми «Включить».»
- «Выход»: остановить `winws`, закрыть приложение, без диалога.
- Пункты блокируются, пока идёт операция.

### 8.9. Закрытие окна

- `MinimizeToTray = true` и это не «Выход» → `Hide()`, приложение живёт в трее.
- Иначе → если обход включён, один короткий диалог:
  «Выключить обход и закрыть приложение?» Да/Нет.
  - Да — остановить, закрыть.
  - Нет — остаться.
- Если обход выключен — закрыть без вопросов.
- `Application.Shutdown` и трей-выход всегда останавливают движок.

### 8.10. Ошибки и уведомления

- Одно окно на эпизод, без каскада.
- На главном — короткий человеческий текст, без `Exception.ToString()`.
- Технические подробности — в `%APPDATA%\ZPRT_NEXT_GEN\logs`.
- Примеры:
  - «Не удалось запустить обход. Проверь права администратора и папку с движком.»
  - «Интернет не отвечает. Проверь подключение и попробуй снова.»
  - «Discord не найден на этом компьютере.»
  - «Не удалось открыть браузер.»
- Кнопки действий: «Попробовать снова», «Настройки», «Открыть лог».

## 9. Схема `config.json`

```json
{
  "schemaVersion": 2,
  "engine": {
    "path": null
  },
  "connection": {
    "mode": "auto",
    "manualPresetId": "general-alt2",
    "autoProfile": {
      "presetId": "general-alt7",
      "name": "Альтернатива 7",
      "latencyMs": 143,
      "youtubeMs": 131,
      "discordMs": 143,
      "testedAtUtc": "2026-11-13T09:15:00Z",
      "engineVersion": "1.10.4",
      "presetArgsHash": "sha256:..."
    },
    "healthCheck": {
      "enabled": true,
      "intervalSeconds": 60,
      "failuresBeforeRetest": 2,
      "retestCooldownSeconds": 300
    }
  },
  "customPresets": [
    {
      "id": "custom:6f1c2a",
      "name": "Мой пресет",
      "description": "Для домашнего интернета",
      "args": "--wf-tcp=80,443 {{GameFilterTCP}} --wf-udp=443,50000-50100 {{GameFilterUDP}} ..."
    }
  ],
  "gameFilter": "All",
  "behavior": {
    "autostart": true,
    "minimizeToTray": true,
    "reduceMotion": false,
    "theme": "dark"
  },
  "updates": {
    "autoUpdateEngine": true
  },
  "ui": {
    "collapsedGroups": []
  }
}
```

Поля:

| Поле | Назначение |
|---|---|
| `engine.path` | Путь к движку для Standard. `null` — искать автоматически. В Portable игнорируется, путь вычисляется установщиком. |
| `connection.mode` | `auto` — автоподбор, `manual` — выбранный пресет. |
| `connection.manualPresetId` | Ручной выбор; сохраняется даже в авто, чтобы вернуться. |
| `connection.autoProfile` | Последний рабочий профиль. При старте поднимается сразу. |
| `autoProfile.latencyMs` | `max(youtubeMs, discordMs)`. |
| `autoProfile.presetArgsHash` | Позволяет понять, что аргументы пресета изменились после обновления движка. |
| `connection.healthCheck` | Пороги фоновой проверки. |
| `customPresets` | Пользовательские пресеты, не затираются движком. |
| `gameFilter` | `All` = игровой профиль, `Disabled` = выкл., `Tcp`/`Udp` = тонкий режим. |
| `behavior.autostart` | Задача в Планировщике. |
| `behavior.minimizeToTray` | Закрытие в трей. |
| `behavior.reduceMotion` | Меньше анимаций. |
| `updates.autoUpdateEngine` | Автопроверка движка при старте (Standard). |
| `ui.collapsedGroups` | Состояние групп пресетов в настройках. |

Нормализация:

- `mode` вне `auto|manual` → `auto`;
- `gameFilter` вне enum → `Disabled`;
- `customPresets` с пустым id/name/args → отбрасываются с записью в лог;
- дубли id → последний отбрасывается;
- `autoProfile` с несуществующим пресетом → `null` (при следующем подключении
  будет перебор);
- `healthCheck` вне диапазона → значения по умолчанию.

## 10. Структура файлов

```text
src/ZprtNextGen.Core/
  Connection/
    ConnectionCoordinator.cs
    ConnectionState.cs
    ConnectionSnapshot.cs
    AutoSelectService.cs
    AutoSelectPolicy.cs
    HealthMonitor.cs
    EngineOperationGate.cs
  Diagnostics/
    Connectivity.cs                 // + ServiceKind, группы
    ConnectivityService.cs          // + YouTube/Discord отдельно
  Discord/
    DiscordFixService.cs
    DiscordFixResult.cs
    DiscordInstallation.cs
  Engine/
    EngineLocator.cs                // + portable root
    PortableEngineInstaller.cs
    EmbeddedEnginePayload.cs
  Presets/
    Preset.cs                       // + Source
    CustomPreset.cs
    PresetCatalogBuilder.cs
    PresetTester.cs                 // + оба сервиса, score
    PresetTestResult.cs
  Settings/
    AppConfig.cs
    ConfigStore.cs
    ConfigMigration.cs
    ConfigDefaults.cs
  Startup/
    Autostart.cs                    // XML + --tray --autoconnect

src/ZprtNextGen.App/
  ViewModels/
    MainViewModel.cs                // главный экран
    SettingsViewModel.cs            // настройки, тесты, пресеты, обновления
    AsyncRelayCommand.cs
    RelayCommand.cs
  Controls/
    ConnectionOrb.xaml(.cs)         // новый
    PresetSelector.xaml(.cs)        // переезжает в настройки
  Services/
    TrayService.cs                  // новое меню + balloon
    AppStartupOptions.cs            // --tray / --autoconnect
    DarkTitleBar.cs
    SingleInstanceGuard.cs
  MainWindow.xaml(.cs)              // переписать
  SettingsWindow.xaml(.cs)          // переписать
  PresetEditorWindow.xaml(.cs)      // новый
  App.xaml.cs                       // composition root

tools/
  Build-Release.ps1                 // новый: Standard + Portable
  Pack-Release.ps1                  // обновить

tests/ZprtNextGen.Core.Tests/
  Connection/...
  Diagnostics/ConnectivityServiceTests.cs
  Discord/DiscordFixServiceTests.cs
  Engine/PortableEngineInstallerTests.cs
  Settings/ConfigMigrationTests.cs
  Startup/AutostartTests.cs
tests/ZprtNextGen.App.Tests/
  ViewModels/MainViewModelTests.cs
  ViewModels/SettingsViewModelTests.cs
  Controls/ConnectionOrbTests.cs
```

`MeditationView` на главном экране больше не используется. Если после UI-шага он не
нужен нигде, удаляем контрол и его тест; если нужен как декор в «О программе» —
оставляем. Решение фиксируем при реализации UI, чтобы не тащить мёртвый код.

## 11. Потоки данных

### 11.1. Первый запуск

1. `App` читает аргументы, конфиг, каталог.
2. Профиля нет → `Disconnected`, красный орб.
3. Клик → `ConnectionCoordinator.ConnectAsync`.
4. `mode = auto`, `autoProfile = null` → `Selecting`.
5. Тест 22 альтов, прогресс, при первом успехе — кнопка «Остановить и применить».
6. Лучший применяется, проверяется, сохраняется в `config.json`.
7. `Connected`, зелёный орб, галочка.

### 11.2. Повторный запуск

1. `autoProfile` есть → `Connecting`.
2. Запуск сохранённого, проверка обоих сервисов.
3. Успех → `Connected` за ~2–3 с.
4. Неудача → повтор, затем `Selecting` с баннером.

### 11.3. Автозапуск

1. Планировщик запускает `ZprtNextGen.exe --tray --autoconnect`.
2. Окно не показывается, создаётся трей.
3. Профиль есть → подключение; успех → balloon.
4. Профиль есть, но не работает → восстановление/перебор.
5. Профиля нет → balloon с подсказкой, без перебора.

### 11.4. Ручной режим

1. В настройках выбран пресет → `mode = manual`.
2. Клик по орбу → запуск выбранного, проверка.
3. Фоновая проверка не переключает автоматически; только уведомляет.

### 11.5. Восстановление

1. `HealthMonitor` видит 2 неудачи.
2. Останавливает, запускает сохранённый, проверяет.
3. Не вышло → автоподбор.
4. Не вышло → `Error`, cooldown 5 минут, balloon.

### 11.6. Fix Discord

1. Клик.
2. Остановка процессов Discord по путям.
3. Очистка безопасного кэша.
4. Перезапуск.
5. Balloon + «Готово ✓».

### 11.7. Portable

1. `App` видит embedded resource.
2. Установщик проверяет кэш, при необходимости распаковывает.
3. `EngineLocator` получает распакованный корень.
4. Каталог читается из распакованного `presets/presets.json`.
5. Дальше всё как в Standard.

## 12. Сборка

### 12.1. Standard

Содержимое архива:

```text
ZprtNextGen.exe
engine\bin\winws.exe
engine\bin\WinDivert.dll
engine\bin\WinDivert64.sys
engine\lists\...
engine\utils\...
presets\presets.json
assets\icon.ico
LICENSE
THIRD-PARTY-NOTICES.md
VERSION.txt
```

GUI — self-contained single-file exe. Движок лежит рядом, чтобы его можно было
обновлять отдельно.

### 12.2. Portable

Один `ZprtNextGen.exe`. Внутри:

- GUI;
- `ZprtNextGen.Engine.zip` (bin, lists, utils, presets);
- иконка (embedded resource или `ExtractAssociatedIcon`);
- `LICENSE` и `THIRD-PARTY-NOTICES.md` как embedded resource, чтобы лицензии
  оставались доступны из «О программе» без отдельных файлов.

Рядом нет `engine/` и `presets/`. При старте — распаковка в `%LOCALAPPDATA%`.

`csproj`:

```xml
<PropertyGroup Condition="'$(Portable)' == 'true'">
  <DefineConstants>$(DefineConstants);PORTABLE</DefineConstants>
</PropertyGroup>

<ItemGroup Condition="'$(Portable)' == 'true'">
  <EmbeddedResource Include="$(PortableEngineZip)"
                    LogicalName="ZprtNextGen.Engine.zip" />
</ItemGroup>

<ItemGroup Condition="'$(Portable)' != 'true'">
  <Content Include="..\..\presets\presets.json"
           Link="presets\presets.json"
           CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`PortableEngineZip` передаётся абсолютным путём из build-скрипта.

### 12.3. `Build-Release.ps1`

Параметры:

```powershell
param(
    [string] $EnginePath,
    [string] $Version = "1.0.0-beta.1",
    [string] $OutputDir = "artifacts",
    [switch] $SkipTests
)
```

`-EnginePath` принимает либо корень движка, либо путь к `bin\winws.exe`; во втором
случае корень вычисляется как два уровня выше.

Автопоиск движка, если `-EnginePath` не задан:

1. `<repo>\engine`;
2. `<repo>\..\engine`;
3. `%USERPROFILE%\Desktop\ZPRT_NEXT_GEN\engine`;
4. `%USERPROFILE%\Desktop\NOVII ZAPRET\engine`;
5. `Get-ChildItem %USERPROFILE%\Desktop -Directory -Depth 2`, где есть
   `engine\bin\winws.exe`;
6. `Get-ChildItem %USERPROFILE%\Desktop -Filter winws.exe -Recurse -Depth 3` и
   подняться к корню движка.

Если найдено несколько — печатает выбранный путь и требует `-EnginePath` при
неоднозначности. Проверяет обязательные файлы и `lists\list-general.txt`.

Шаги:

1. Проверить движок.
2. `dotnet test -c Release` (если не `-SkipTests`).
3. Создать `artifacts\portable\engine.zip`:
   `bin`, `lists`, `utils`, `presets\presets.json`.
4. `dotnet publish src/ZprtNextGen.App -c Release -o artifacts\standard`
   с версией.
5. `Pack-Release.ps1` → `ZPRT_NEXT_GEN-v<version>-standard-win-x64.zip`.
6. `dotnet publish src/ZprtNextGen.App -c Release -o artifacts\portable`
   с `-p:Portable=true -p:PortableEngineZip=<abs>`.
7. Проверить, что в portable-папке только exe (и pdb, если есть), нет `engine/`.
8. Запустить `ZprtNextGen.exe --portable-check` с
   `ZPRT_ENGINE_CACHE_ROOT` в temp; код 0 — движок распаковался и валиден.
   Запускать из elevated PowerShell (манифест требует администратора); в CI
   runner уже elevated. Если прав нет — шаг пропускается с предупреждением.
9. Скопировать raw exe в
   `artifacts\ZPRT_NEXT_GEN-v<version>-portable-win-x64.exe`.
10. Упаковать portable exe в
    `ZPRT_NEXT_GEN-v<version>-portable-win-x64.zip`.
11. Напечатать пути и размеры.

`--portable-check` — скрытый режим без окна: распаковывает движок, проверяет файлы,
возвращает 0/2. Нужен для CI и сборки.

`Pack-Release.ps1` обновляем:

- `-PublishDir`, `-EngineDir`, `-OutputZip`, `-Version`;
- не удаляет чужие артефакты;
- проверяет обязательные файлы;
- пишет `VERSION.txt`;
- сохраняет текущее поведение для Standard.

Артефакты:

```text
ZPRT_NEXT_GEN-v1.0.0-beta.1-standard-win-x64.zip
ZPRT_NEXT_GEN-v1.0.0-beta.1-portable-win-x64.zip
ZPRT_NEXT_GEN-v1.0.0-beta.1-portable-win-x64.exe
```

### 12.4. CI

`.github/workflows/release.yml`:

- принимает `version` и `engine_url`;
- скачивает движок;
- запускает `tools\Build-Release.ps1 -EnginePath engine -Version ...`;
- загружает оба zip и raw portable exe;
- создаёт prerelease `v<version>` с тремя ассетами.

## 13. Тестирование

### 13.1. Unit-тесты Core

- `ConfigMigrationTests`: старый `settings.json` → v2, все поля, `.bak`,
  `mode=manual`, отсутствие файла → `auto`.
- `ConfigStoreTests`: битый JSON → `.bad`, атомарная запись, нормализация,
  неизвестная версия.
- `ConnectivityServiceTests`: группировка YouTube/Discord, `BothOk`, таймауты,
  404 = успех, 500 = неуспех.
- `PresetTesterTests`: требует оба сервиса; `score = max`; остановка в `finally`;
  отмена с частичными результатами; foreign instance.
- `AutoSelectServiceTests`: выбирает минимальный score; игнорирует нерабочие;
  досрочное применение; пустой список; все пресеты не прошли проверку.
- `ConnectionCoordinatorTests`: состояния, сохранённый профиль, повтор,
  перебор, отмена, возврат к предыдущему профилю, шлюз операций.
- `HealthMonitorTests`: 2 неудачи → recovery; cooldown; manual → только уведомление.
- `DiscordFixServiceTests`: пути, фильтр процессов, безопасный список, полный сброс,
  отсутствие Discord, заблокированный файл.
- `PortableEngineInstallerTests`: распаковка zip, `.ready`, повторное использование,
  смена hash, `ZPRT_ENGINE_CACHE_ROOT`.
- `AutostartTests`: XML содержит `--tray --autoconnect`, `HighestAvailable`,
  корректное экранирование пути, `IsEnabled` проверяет аргументы.
- `CustomPresetTests`: валидация, слияние с каталогом, плейсхолдеры.

### 13.2. Unit-тесты App

- `MainViewModelTests`: маппинг состояний в тексты/цвета, клик по орбу,
  Fix Discord, донат, ошибки.
- `SettingsViewModelTests`: авто/ручной режим, сохранение, тест, применение,
  кастомные пресеты, автозапуск, игровой профиль.
- `ConnectionOrbTests`: DP состояний, доступность, `ReduceMotion`.

### 13.3. Ручная проверка

- Чистая установка без конфига: первый клик → все 22, прогресс, «Остановить и
  применить лучший», сохранение, зелёный орб.
- Повторный запуск: сохранённый альт за < 5 с.
- Отключить интернет/заблокировать: 2 неудачи → перезапуск → перебор.
- Ручной режим: выбор пресета, подключение, отсутствие самовольного переключения.
- Fix Discord при запущенном/закрытом Discord; авторизация сохраняется.
- Полный сброс: появляется экран входа.
- Трей: закрытие, развернуть, включить/выключить, Fix Discord, выход.
- Автозапуск: задача содержит аргументы, после входа стартует в трее, поднимает
  профиль; без профиля — только balloon.
- Portable: пустая папка, первый запуск (распаковка), второй (быстро), выход
  гасит `winws`, папка кэша остаётся.
- Standard: zip содержит `engine\bin\winws.exe`, `presets\presets.json`.
- Донат открывает нужный URL.
- `ReduceMotion`: без вращения/галочки.
- UIA: имена кнопок, статусы, фокус, клавиатура.

## 14. Миграция и откат

- Перед миграцией: `settings.json` → `settings.json.bak` (копия), оригинал остаётся.
- `config.json` пишется только после успешного чтения старого.
- Если `config.json` битый: `.bad`, значения по умолчанию, приложение запускается.
- Если schemaVersion больше поддерживаемой: `.bak`, значения по умолчанию,
  предупреждение в лог; данные не затираем.
- Откат на старую версию: старый exe читает `settings.json`, который мы не удалили.
- Пользовательские пресеты и `autoProfile` при откате не теряются, потому что
  `config.json` остаётся на диске.

## 15. Порядок реализации

1. **Core-фундамент:** `AppConfig`/`ConfigStore`/миграция, группы connectivity,
   `PresetTester` с двумя сервисами, `AutoSelectService`, `ConnectionCoordinator`,
   `HealthMonitor`, `EngineOperationGate`, `DiscordFixService`. Тесты.
2. **UI:** `ConnectionOrb`, новый `MainWindow`, `SettingsViewModel`, новый
   `SettingsWindow`, `PresetEditorWindow`, `TrayService`, `--tray/--autoconnect`,
   автозапуск XML. Тесты.
3. **Portable и сборка:** `PortableEngineInstaller`, embedded zip, `Build-Release.ps1`,
   `Pack-Release.ps1`, `--portable-check`, CI.
4. **Проверка и релиз:** тесты, ручной чек-лист, UIA, два артефакта,
   prerelease `v1.0.0-beta.1`, обновление README/AGENTS.

## 16. Принятые допущения

- Все 22 встроенных пресета тестируются всегда; пользовательские добавляются к ним.
- Существующая установка после миграции — ручной режим; чистая — авто.
- Portable не обновляет движок отдельно; обновляется вся программа.
- Конфиг общий для Standard и Portable (`%APPDATA%`).
- Логи остаются локальными, телеметрии нет.
- Дизайн-токены: красный `#E01B2E`, янтарный `#F5A524`, зелёный `#22C55E`,
  золото `#FFC65C`, фон `#0A0A0A` — в рамках текущей темы.

## 17. Что нужно для старта

- Подтвердить этот документ.
- После подтверждения — `writing-plans`: пошаговый план с задачами, тестами и
  критериями готовности.
- Код начинаем только после утверждения плана.
