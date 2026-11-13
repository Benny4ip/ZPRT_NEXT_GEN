# ZPRT_NEXT_GEN 1.0.0-beta.1 — чек-лист реализации

Основание: `docs/superpowers/specs/2026-11-13-release-beta-design.md` (Approved).

## 1. Core-фундамент
- [x] `AppConfig` / `ConfigStore` / `ConfigMigration` / нормализация + тесты
- [x] `ConnectivityTarget.ServiceKind`, `YouTubeOk` / `DiscordOk` / `BothOk`, `CheckInternetAsync`
- [x] `PresetTestResult` с обоими сервисами и `ScoreMs`
- [x] `AutoSelectService` (все пресеты, лучший по score, отмена с частичными результатами)
- [x] `ConnectionCoordinator` (auto/manual, сохранённый профиль, восстановление, шлюз)
- [x] `HealthMonitor` (60 с, 2 неудачи, cooldown, ручной режим — только уведомление)
- [x] `DiscordFixService` (безопасный кэш + полный сброс)
- [x] `PortableEngineInstaller` (embedded zip, кэш по версии/hash, `.ready`)
- [x] XML-автозапуск `--tray --autoconnect`
- [x] Пользовательские пресеты и слияние с каталогом

## 2. UI
- [x] `ConnectionOrb` (светофор, кольцо прогресса, анимированная галочка, ReduceMotion)
- [x] Новый `MainWindow` (4 угла: название/донат, Fix Discord/настройки + большой орб по центру), `DashboardViewModel`
- [x] `SettingsViewModel` + расширенный `SettingsWindow` (диагностика, полный сброс Discord, пресеты)
- [x] `PresetEditorWindow`
- [x] Трей: Развернуть / Вкл-Выкл / Fix Discord / Выход (`TrayService`)
- [x] Аргументы `--tray`, `--autoconnect`, `--portable-check`

## 3. Portable и сборка
- [x] `Portable` в `csproj`, embedded `ZprtNextGen.Engine.zip`
- [x] `Build-Release.ps1` с `-EnginePath`, автопоиском движка на Desktop, тестами и упаковкой
- [x] `Pack-Release.ps1` для Standard
- [x] Успешная сборка трёх релизных артефактов и валидация через `--portable-check`

## 4. Проверка и релиз
- [x] Все тесты зелёные (225 из 225: 40 App, 185 Core)
- [x] `dotnet build` и `dotnet publish` успешно завершены
- [x] Артефакты версии `1.0.0-beta.1` созданы в `artifacts/`:
  - `ZPRT_NEXT_GEN-v1.0.0-beta.1-standard-win-x64.zip` (67,65 MB)
  - `ZPRT_NEXT_GEN-v1.0.0-beta.1-portable-win-x64.zip` (67,63 MB)
  - `ZPRT_NEXT_GEN-v1.0.0-beta.1-portable-win-x64.exe` (167,02 MB)
