# ZPRT_NEXT_GEN Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Построить русскоязычную WPF-оболочку `ZPRT_NEXT_GEN`, которая управляет `winws.exe` из `Flowseal/zapret-discord-youtube` через нативные пресеты, показывает статус, проверяет связь и анимирует «просветление» при подключении.

**Architecture:** Три слоя: `ZprtNextGen.Core` (чистая логика без WPF: парсинг `.bat`, каталог пресетов, настройки, процесс, сеть), `ZprtNextGen.App` (WPF, MVVM, трей, анимация), `tools` (импорт пресетов, иконка, упаковка). Пресеты конвертируются из upstream-`.bat` в `presets/presets.json` на этапе разработки; приложение запускает `bin\winws.exe` напрямую.

**Tech Stack:** .NET 10, C#, WPF, System.Text.Json, WinForms `NotifyIcon`, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-zprt-next-gen-design.md`

## Global Constraints

- .NET SDK 10.0.401, C# `latest`, `Nullable=enable`, `ImplicitUsings=enable`.
- Только Windows 10/11 x64; приложение — `requireAdministrator`.
- Весь пользовательский текст — русский; идентификаторы, типы и имена файлов — английские.
- Сторонних NuGet-пакетов нет, кроме тестовых (`xunit`, `Microsoft.NET.Test.Sdk`, `coverlet.collector` из шаблона). Трей — `System.Windows.Forms.NotifyIcon`. JSON — `System.Text.Json`.
- `engine/`, `bin/`, `obj/`, `*.exe`, `*.dll`, `*.sys`, `*.zip` не попадают в git.
- Лицензия проекта MIT; upstream MIT (bol-van 2016–2026, Flowseal 2024–2026) — атрибуция обязательна.
- Релиз собирается только вручную через `workflow_dispatch`.
- PowerShell 5.1: `.ps1` с кириллицей сохранять в UTF-8 **с BOM**; запускать через `powershell -NoProfile -ExecutionPolicy Bypass -File`. В строках использовать `${i}`, не `"$i:"`.
- Тесты не должны требовать администратора, реального `winws`, `schtasks` или сети.
- Тестовые процессы — только `cmd.exe`/`ping` с уникальными именами; реальный `winws` на машине разработчика не трогать.
- Все пути с пробелами и кириллицей должны работать; аргументы `winws` передаются одной строкой без пересборки кавычек.

## Review Focus

1. **Незнакомый/изменённый `.bat`** (новая строка, `%VAR%`, `start` без `/min`) → импорт обязан упасть с понятной ошибкой, а не выпустить пресет с мусором в аргументах.
2. **Путь к движку с пробелами/кириллицей или неполная папка** → приложение показывает русскую ошибку со списком недостающих файлов, не падает и не запускает `winws` наугад.
3. **Чужой `winws` уже запущен** (из `.bat` или службы) → приложение не убивает его молча: статус «запущен извне» + подтверждение.
4. **Проверка связи частично успешна или висит** (captive portal, один хост недоступен) → `Connected` только при успехе primary-хоста; таймаут завершается, UI не блокируется.
5. **`settings.json` повреждён/недоступен** → приложение стартует с настройками по умолчанию и не теряет работоспособность.

---

### Task 1: Каркас решения, git, CI

**Files:**
- Create: `ZprtNextGen.sln`, `Directory.Build.props`, `.gitignore`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, `licenses/zapret-LICENSE.txt`, `README.md`, `.github/workflows/build.yml`
- Create: `src/ZprtNextGen.Core/ZprtNextGen.Core.csproj`, `src/ZprtNextGen.App/ZprtNextGen.App.csproj`, `src/ZprtNextGen.App/app.manifest`, `tests/ZprtNextGen.Core.Tests/`, `tests/ZprtNextGen.App.Tests/`

**Interfaces:**
- Consumes: nothing.
- Produces: solution that builds; `Core` (net10.0), `App` (net10.0-windows, WPF), two test projects (Core net10.0, App net10.0-windows + UseWPF) referencing their targets.

- [ ] **Step 1: Create projects**

```powershell
git init
dotnet new sln -n ZprtNextGen
dotnet new classlib -n ZprtNextGen.Core -o src/ZprtNextGen.Core -f net10.0
dotnet new wpf -n ZprtNextGen.App -o src/ZprtNextGen.App -f net10.0
dotnet new xunit -n ZprtNextGen.Core.Tests -o tests/ZprtNextGen.Core.Tests -f net10.0
dotnet new xunit -n ZprtNextGen.App.Tests -o tests/ZprtNextGen.App.Tests -f net10.0
dotnet sln add src/ZprtNextGen.Core src/ZprtNextGen.App tests/ZprtNextGen.Core.Tests tests/ZprtNextGen.App.Tests
dotnet add tests/ZprtNextGen.Core.Tests reference src/ZprtNextGen.Core
dotnet add tests/ZprtNextGen.App.Tests reference src/ZprtNextGen.App
```

- [ ] **Step 2: Fix target frameworks and shared props**

In `tests/ZprtNextGen.App.Tests/ZprtNextGen.App.Tests.csproj` set `<TargetFramework>net10.0-windows</TargetFramework>` and `<UseWPF>true</UseWPF>`. In `src/ZprtNextGen.App/ZprtNextGen.App.csproj` confirm `net10.0-windows` and `<UseWPF>true</UseWPF>`; add `<UseWindowsForms>true</UseWindowsForms>` and `<ApplicationManifest>app.manifest</ApplicationManifest>`.

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`app.manifest` (used from Task 1 so the build does not reference a missing file):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 3: Write repo hygiene files**

`.gitignore` must include `bin/`, `obj/`, `engine/`, `*.user`, `*.exe`, `*.dll`, `*.sys`, `*.zip`, `.vs/`, `*_wpftmp.csproj`, `publish/`.
`LICENSE` — MIT, `Copyright (c) 2026 Benny4ip`.
`THIRD-PARTY-NOTICES.md` — upstream URL, лицензия MIT, copyright bol-van/Flowseal, дисклеймер «проект не связан с авторами».
`licenses/zapret-LICENSE.txt` — полный текст MIT upstream.
`README.md` — заголовок, одно предложение о проекте, ссылка на спек, строка «Релиз собирается вручную». Полный README — Task 18.

- [ ] **Step 4: CI**

`.github/workflows/build.yml`: `on: [push, pull_request]`, `runs-on: windows-latest`, steps: checkout, `actions/setup-dotnet@v4` с `dotnet-version: 10.0.x`, `dotnet restore`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build`.

- [ ] **Step 5: Verify build and tests**

Run: `dotnet build -c Release`
Expected: `Build succeeded` for all four projects.
Run: `dotnet test -c Release --no-build`
Expected: both template test projects pass (2 tests).

- [ ] **Step 6: Create public repo and push**

```powershell
git add .
git commit -m "chore: scaffold ZPRT_NEXT_GEN solution"
gh repo create Benny4ip/ZPRT_NEXT_GEN --public --source . --remote origin --push --description "Русскоязычная графическая оболочка для zapret-discord-youtube"
gh repo edit --add-topic zapret,dpi,windows,wpf
```

Expected: репозиторий открыт, `git remote -v` показывает `origin`.

---

### Task 2: Модель каталога пресетов

**Files:**
- Create: `src/ZprtNextGen.Core/Presets/Preset.cs`, `src/ZprtNextGen.Core/Presets/PresetCatalogLoader.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presets/PresetCatalogLoaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record PresetGroup(string Id, string Name)`
  - `record Preset(string Id, string GroupId, string Name, string Description, string Arguments)`
  - `record PresetCatalog(int Version, string Engine, string EngineVersion, IReadOnlyList<PresetGroup> Groups, IReadOnlyList<Preset> Presets)`
  - `static PresetCatalog PresetCatalogLoader.Load(string path)` — JSON, `PropertyNameCaseInsensitive`. DTO использует точные ключи: `version`, `engine`, `engineVersion`, `groups[{id,name}]`, `presets[{id,group,name,description,args}]`.

- [ ] **Step 1: Write failing tests**

`PresetCatalogLoaderTests.cs` with three tests:
- `Load_ValidCatalog_ReturnsGroupsAndPresets`: temp JSON `{"version":1,"engine":"Flowseal/zapret-discord-youtube","engineVersion":"1.10.2","groups":[{"id":"general","name":"Обычные"}],"presets":[{"id":"general","group":"general","name":"Основной","description":"Базовый","args":"--wf-tcp=80"}]}` → `Presets.Count == 1`, group name `Обычные`, args preserved.
- `Load_PresetWithUnknownGroup_Throws`: preset `"group":"missing"` → `InvalidDataException` containing `missing`.
- `Load_DuplicatePresetId_Throws`: two presets with id `general` → `InvalidDataException`.
- `Load_UnsupportedVersion_Throws`: `"version":2` → `InvalidDataException`.

Use `Path.GetTempFileName()` and delete in `finally`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~PresetCatalogLoaderTests"`
Expected: FAIL — `PresetCatalog`/`PresetCatalogLoader` not defined.

- [ ] **Step 3: Implement model and loader**

`Preset.cs` holds the three records. `PresetCatalogLoader.Load` deserializes into a private DTO with `[JsonPropertyName]` attributes (`version`, `engine`, `engineVersion`, `groups`, `presets`, `args`), then validates: version `1`, at least one group and preset, unique group ids, unique preset ids, every `Preset.GroupId` exists. Throw `InvalidDataException` with the offending id. Do not silently skip entries.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~PresetCatalogLoaderTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Presets tests/ZprtNextGen.Core.Tests/Presets
git commit -m "feat(core): add preset catalog model and loader"
```

---

### Task 3: Парсер `.bat` → аргументы `winws`

**Files:**
- Create: `src/ZprtNextGen.Core/Presets/BatPresetParser.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presets/BatPresetParserTests.cs`
- Test fixtures: `tests/ZprtNextGen.Core.Tests/Fixtures/general.bat`, `tests/ZprtNextGen.Core.Tests/Fixtures/unresolved-var.bat`
- Modify: `tests/ZprtNextGen.Core.Tests/ZprtNextGen.Core.Tests.csproj` (copy fixtures to output)

**Interfaces:**
- Consumes: nothing.
- Produces: `static string BatPresetParser.ParseArguments(string batText, string presetId)`.
- Rules: joins `^` continuations, removes `start "…" /min "…winws.exe"`, replaces `%BIN%`→`{{BIN}}`, `%LISTS%`→`{{LISTS}}`, `%GameFilterTCP%`→`{{GameFilterTCP}}`, `%GameFilterUDP%`→`{{GameFilterUDP}}`, unescapes `^X`→`X`, preserves quotes/order/spacing inside arguments.

- [ ] **Step 1: Write failing tests**

- `Parse_GeneralFixture_ReturnsExpectedArguments`: read fixture containing the real first preset line, one continuation, and `--dpi-desync-fake-tls=^!`; assert result starts with `--wf-tcp=80,443`, contains `"{{LISTS}}list-general.txt"`, contains `--dpi-desync-fake-tls=!`, does not contain `start`, `%BIN%`, `%LISTS%`, `^`.
- `Parse_UnresolvedVariable_Throws`: fixture with `%UNKNOWN%` → `InvalidDataException` message contains `UNKNOWN` and `unresolved-var`.
- `Parse_NoWinwsLine_Throws`: text without `winws.exe` → `InvalidDataException`.
- `Parse_EmptyArguments_Throws`: `start "x" /min "%BIN%winws.exe"` with nothing after → `InvalidDataException`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~BatPresetParserTests"`
Expected: FAIL — parser not defined.

- [ ] **Step 3: Implement parser**

Algorithm:
1. Normalize `\r\n` and `\r` to `\n`.
2. Split lines; find the first line matching regex `^\s*start\s+"[^"]*"\s+/min\s+"[^"]*winws\.exe"\s+(.*)$`.
3. Take group 1; while it ends with `^`, drop the `^`, append ` ` + next line trimmed.
4. Replace `^` + any character with that character (regex `\^(.)` → `$1`), after continuations are joined.
5. Replace the four `%…%` variables with placeholders.
6. If regex `%[A-Za-z_][A-Za-z0-9_]*%` still matches → `InvalidDataException($"preset '{presetId}': unresolved variable {match}")`.
7. Trim; if empty → throw.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~BatPresetParserTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Presets/BatPresetParser.cs tests/ZprtNextGen.Core.Tests
git commit -m "feat(core): parse zapret bat presets into winws arguments"
```

---

### Task 4: Импортёр, группировка и русские названия

**Files:**
- Create: `src/ZprtNextGen.Core/Presets/PresetNaming.cs`, `src/ZprtNextGen.Core/Presets/PresetImporter.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presets/PresetImporterTests.cs`
- Test fixtures: `tests/ZprtNextGen.Core.Tests/Fixtures/bats/general.bat`, `general (ALT5).bat`, `general (EXP).bat`, `general (FAKE TLS AUTO).bat`, `service.bat` (must be ignored). Engine folders (`bin/winws.exe`, `lists/list-general.txt`) are created **at test runtime** in a temp directory — `.gitignore` excludes `bin/`/`*.exe`, so they must not be committed.

**Interfaces:**
- Consumes: `BatPresetParser.ParseArguments`, `PresetCatalog`, `PresetGroup`, `Preset`.
- Produces:
  - `static string PresetNaming.ToId(string fileName)`
  - `static (string Name, string Description) PresetNaming.Describe(string id)`
  - `static PresetCatalog PresetImporter.Import(string engineRoot, string? engineVersion = null)`
- Group order is fixed: `general`, `faketls`, `simplefake`, `experimental`. Names: `Обычные`, `Fake TLS`, `Simple Fake`, `Экспериментальные`.
- Group by id: prefix `general-exp` → experimental; contains `fake-tls` → faketls; contains `simple-fake` → simplefake; else general.
- `ToId`: lowercase; `(`/`)` removed; spaces → `-`; collapse repeated `-`; trim `-`. Examples: `general (ALT5)` → `general-alt5`; `general (FAKE TLS AUTO ALT)` → `general-fake-tls-auto-alt`.
- `Describe` rules (exact):
  - `general` → `("Основной", "Базовый набор: Discord, YouTube и общий список. Начни с него.")`
  - `general-alt{N}` (no number = 1) → `($"Альтернатива {N}", $"Вариант {N}: другая комбинация стратегий обхода.")`
  - `general-exp` → `("Экспериментальный", "Экспериментальные стратегии. Может работать нестабильно.")`
  - `general-fake-tls-auto` → `("Fake TLS (авто)", "Автоматический подбор fake TLS. Часто помогает на мобильных операторах.")`
  - `general-fake-tls-auto-alt{N}` → `($"Fake TLS, вариант {N}", $"Fake TLS, альтернативная комбинация {N}.")`
  - `general-simple-fake` → `("Simple Fake", "Упрощённый fake без сложных модификаций.")`
  - `general-simple-fake-alt{N}` → `($"Simple Fake, вариант {N}", $"Simple Fake, альтернативная комбинация {N}.")`
  - unknown → `(fileNameWithoutExtension, "Пресет из upstream.")`

- [ ] **Step 1: Write failing tests**

- `ToId_ConvertsFileNames` theory: four examples above → expected ids.
- `Describe_AltWithoutNumber_IsFirst` → `Альтернатива 1`.
- `Import_RealPresetNames_ClassifiesGroups`: test builds a temp engine dir with empty `bin\winws.exe` and `lists\list-general.txt`, copies the 5 bat fixtures into it → 4 presets; group counts 2 general (general, ALT5), 1 faketls, 1 experimental; `service` absent; every arguments non-empty and contains `{{BIN}}`.
- `Import_MissingBin_ThrowsInvalidDataException`: empty engine dir → message contains `bin`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~PresetImporterTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement**

`PresetImporter.Import`:
1. Validate `engineRoot\bin\winws.exe` and `engineRoot\lists\list-general.txt`; throw `InvalidDataException` naming the missing path.
2. Enumerate `general*.bat`, exclude `service.bat`, sort by `ToId` ordinal.
3. For each: `File.ReadAllText(path, Encoding.UTF8)`, id, arguments, name/description, group.
4. Build groups in fixed order, omitting empty groups.
5. Return `new PresetCatalog(1, "Flowseal/zapret-discord-youtube", engineVersion ?? "unknown", groups, presets)`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~PresetImporterTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Presets tests/ZprtNextGen.Core.Tests
git commit -m "feat(core): import and group zapret presets with Russian names"
```

---

### Task 5: CLI импорта, PowerShell-обёртка, реальный `presets.json`

**Files:**
- Create: `tools/ZprtNextGen.PresetTool/ZprtNextGen.PresetTool.csproj`, `tools/ZprtNextGen.PresetTool/Program.cs`, `tools/Import-Presets.ps1`
- Create: `presets/presets.json` (generated)
- Modify: `ZprtNextGen.sln` (add tool)

**Interfaces:**
- Consumes: `PresetImporter.Import`, `PresetCatalog`.
- Produces: CLI `--engine <dir> --out <file> [--engine-version <ver>]`; exit `0` on success, `1` on error with message to stderr; wrapper script with same parameters.

- [ ] **Step 1: Implement CLI**

`Program.cs`: parse args manually (no packages); required `--engine`, `--out`; optional `--engine-version`; call importer. Serialize a **DTO с точными ключами контракта** (`version`, `engine`, `engineVersion`, `groups`, `presets`, `args`), а не сам record (иначе получатся `Arguments`/`Groups` и загрузчик их не прочитает). `JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }`; создать папку вывода; UTF-8 без BOM. On exception print `Ошибка импорта: {message}` to stderr and return 1.

- [ ] **Step 2: Add wrapper with BOM**

`tools/Import-Presets.ps1`:

```powershell
param(
    [Parameter(Mandatory = $true)][string]$Engine,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$EngineVersion = "unknown"
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet run --project tools/ZprtNextGen.PresetTool -- --engine $Engine --out $Out --engine-version $EngineVersion
} finally { Pop-Location }
```

Save UTF-8 with BOM.

- [ ] **Step 3: Generate from the real upstream**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Import-Presets.ps1 -Engine "C:\Users\Machine\Desktop\zapret-discord-youtube-main" -Out "presets\presets.json" -EngineVersion "1.10.2"
```

Expected: exit 0. Verify:

```powershell
$c = Get-Content presets\presets.json -Raw | ConvertFrom-Json
$c.presets.Count          # 22
$c.groups | ForEach-Object { "$($_.id)=$($_.name)" }
```

Expected counts by group: `general=14`, `faketls=4`, `simplefake=3`, `experimental=1`; total 22.

- [ ] **Step 4: Verify generator fails loudly**

Run the CLI against an empty temp folder.
Expected: exit code 1, stderr contains `winws.exe`, `presets.json` not created.

- [ ] **Step 5: Commit**

```powershell
git add tools presets/presets.json ZprtNextGen.sln
git commit -m "feat(tools): add preset importer CLI and generated presets"
```

---

### Task 6: Подстановка путей и game filter

**Files:**
- Create: `src/ZprtNextGen.Core/Presets/GameFilterMode.cs`, `src/ZprtNextGen.Core/Presets/PresetArgumentBuilder.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presets/PresetArgumentBuilderTests.cs`

**Interfaces:**
- Consumes: `Preset`.
- Produces:
  - `enum GameFilterMode { Disabled, Tcp, Udp, All }`
  - `static string PresetArgumentBuilder.Build(Preset preset, string engineRoot, GameFilterMode mode)`
- Mapping: `Disabled` → both `12`; `Tcp` → TCP `1024-65535`, UDP `12`; `Udp` → TCP `12`, UDP `1024-65535`; `All` → both `1024-65535`.
- `{{BIN}}` → `Path.Combine(engineRoot, "bin") + Path.DirectorySeparatorChar`, likewise `{{LISTS}}`.

- [ ] **Step 1: Write failing tests**

Theory for four modes asserting the exact resulting fragments `--wf-tcp=80,%GF_TCP%` and `--wf-udp=443,%GF_UDP%`; one test with `C:\Program Files\Запрет` asserting result contains `"C:\Program Files\Запрет\lists\list-general.txt"`; one test that an unresolved `{{NOPE}}` throws `InvalidOperationException`; one that empty `Arguments` throws.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~PresetArgumentBuilderTests"`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement**

Replace placeholders with `string.Replace(..., StringComparison.Ordinal)`; then if result contains `{{` throw with the remaining token. Use `Path.GetFullPath(engineRoot)` before combining.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~PresetArgumentBuilderTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Presets tests/ZprtNextGen.Core.Tests/Presets
git commit -m "feat(core): resolve preset paths and game filter mode"
```

---

### Task 7: Настройки

**Files:**
- Create: `src/ZprtNextGen.Core/Settings/AppSettings.cs`, `src/ZprtNextGen.Core/Settings/SettingsStore.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Settings/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `GameFilterMode`.
- Produces:
  - `record AppSettings` with init properties: `string? EnginePath`, `string PresetId = "general"`, `GameFilterMode GameFilter = Disabled`, `bool Autostart`, `bool MinimizeToTray = true`, `string Theme = "dark"`, `bool ReduceMotion`, `IReadOnlyList<string> CollapsedGroups = []`.
  - `interface ISettingsStore { string Path { get; } AppSettings Load(); void Save(AppSettings settings); }`
  - `class SettingsStore : ISettingsStore` with `SettingsStore(string path)` and `static SettingsStore CreateDefault()` → `%APPDATA%\ZPRT_NEXT_GEN\settings.json`.

- [ ] **Step 1: Write failing tests**

- `Load_MissingFile_ReturnsDefaults`: default preset `general`, filter `Disabled`, `MinimizeToTray true`, empty collapsed groups.
- `SaveThenLoad_RoundTrips`: custom values incl. collapsed groups and Cyrillic engine path.
- `Load_CorruptJson_ReturnsDefaultsAndRenames`: file `{oops` → defaults, `settings.json.bad` exists.
- `Load_WrongEnumValue_ReturnsDefaults`: `"gameFilter":"nonsense"` → defaults, no throw.
- `Save_CreatesDirectory`: nested temp path.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }`. On `JsonException`/`IOException`: move corrupt file to `path + ".bad"` (ignore failure) and return new defaults. `Save` creates directory, writes UTF-8 without BOM via temp file + `File.Move(overwrite: true)`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Settings tests/ZprtNextGen.Core.Tests/Settings
git commit -m "feat(core): add resilient settings store"
```

---

### Task 8: Поиск и валидация движка

**Files:**
- Create: `src/ZprtNextGen.Core/Engine/EngineValidation.cs`, `src/ZprtNextGen.Core/Engine/EngineLocator.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Engine/EngineLocatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record EngineValidation(bool IsValid, string? EngineRoot, IReadOnlyList<string> MissingFiles)`
  - `static EngineValidation EngineLocator.Locate(string? configuredPath, string baseDirectory)`
  - `static IReadOnlyList<string> EngineLocator.RequiredFiles` = `bin\winws.exe`, `bin\WinDivert.dll`, `bin\WinDivert64.sys`, `lists\list-general.txt`.

- [ ] **Step 1: Write failing tests**

- `Locate_BundledEngine_Valid`: temp `app\engine\{bin,lists}` with all files → valid, root ends with `engine`.
- `Locate_ConfiguredPathWins`: configured valid path + bundled valid → configured root.
- `Locate_BrokenConfigured_FallsBackToBundled`: configured missing files, bundled valid → bundled root.
- `Locate_NothingValid_ReturnsMissingFiles`: empty `app` → invalid, `MissingFiles` contains `bin\winws.exe`.
- `Locate_PathWithSpacesAndCyrillic_Valid`: folder `C:\Temp\Запрет движок`.
- `Locate_AcceptsUpstreamRoot`: root itself contains `bin`/`lists` and no `engine` subfolder → valid.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~EngineLocatorTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

Candidates: configured (if non-empty) → `baseDirectory\engine` → `baseDirectory`; deduplicate by full path; first with all required files wins. On failure return invalid with `MissingFiles` computed against the first candidate, using names relative to the candidate root.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~EngineLocatorTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Engine tests/ZprtNextGen.Core.Tests/Engine
git commit -m "feat(core): locate and validate engine directory"
```

---

### Task 9: Управление процессом `winws`

**Files:**
- Create: `src/ZprtNextGen.Core/Process/EngineStatus.cs`, `src/ZprtNextGen.Core/Process/EngineProcessManager.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Process/EngineProcessManagerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum EngineStatus { Stopped, Starting, Running, Stopping, Error }`
  - `class EngineStatusChangedEventArgs : EventArgs { EngineStatus Status; string? Detail; }`
  - `interface IEngineProcessManager : IDisposable` with `EngineStatus Status`, `int? OwnedProcessId`, `event EventHandler<EngineStatusChangedEventArgs> StatusChanged`, `event EventHandler<string> OutputReceived`, `Task StartAsync(string executablePath, string arguments, string workingDirectory, CancellationToken ct)`, `Task StopAsync(CancellationToken ct)`, `bool IsForeignInstanceRunning()`
  - `class EngineProcessManager : IEngineProcessManager` with `EngineProcessManager(string engineProcessName = "winws")`.

- [ ] **Step 1: Write failing tests**

Use `Environment.GetEnvironmentVariable("ComSpec")` as the executable. Unique process name per test for foreign detection.

- `Start_CapturesOutputAndRuns`: args `/c "echo ready & ping -n 3 127.0.0.1 >nul"` → wait for output `ready`, status `Running`, after exit status `Stopped`, `OwnedProcessId` null.
- `Stop_KillsOwnedProcess`: args `/c "ping -n 30 127.0.0.1 >nul"` → start, `StopAsync`, `Process.GetProcessById(pid)` throws, status `Stopped`.
- `IsForeignInstanceRunning_DetectsCopiedExe`: copy `cmd.exe` to temp `zprt-test-engine.exe`, start it, manager with that name → true; after kill → false.
- `Start_MissingExecutable_Throws`: path to nonexistent exe → `FileNotFoundException` or `Win32Exception`, status `Error`.

All waits use `TaskCompletionSource` + `Task.WaitAsync(TimeSpan.FromSeconds(10))`, no `Thread.Sleep` loops.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~EngineProcessManagerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`StartAsync`: `ProcessStartInfo { FileName, Arguments, WorkingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 }`; `EnableRaisingEvents = true`; wire `OutputDataReceived`/`ErrorDataReceived` → `OutputReceived`; `Exited` → status `Stopped` (only if current PID is ours), `OwnedProcessId = null`. Set `Starting` before `Start`, `Running` after. `StopAsync`: if own process alive → `Kill(entireProcessTree: true)`, set `Stopping`, await exit up to 5 s, then `Stopped`; if it is foreign (PID mismatch) → throw `InvalidOperationException`. `IsForeignInstanceRunning`: `GetProcessesByName(name)` any PID != `OwnedProcessId`. Dispose unsubscribes and kills owned process only.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~EngineProcessManagerTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Process tests/ZprtNextGen.Core.Tests/Process
git commit -m "feat(core): manage winws process with PID ownership"
```

---

### Task 10: Проверка связи

**Files:**
- Create: `src/ZprtNextGen.Core/Diagnostics/Connectivity.cs`, `src/ZprtNextGen.Core/Diagnostics/ConnectivityService.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Diagnostics/ConnectivityServiceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record ConnectivityTarget(string Id, string DisplayName, string Url, bool IsPrimary)`
  - `record ConnectivityResult(ConnectivityTarget Target, bool Success, long ElapsedMs, string? Error)`
  - `record ConnectivityReport(IReadOnlyList<ConnectivityResult> Results) { bool IsConnected { get; } }`
  - `interface IConnectivityService { Task<ConnectivityReport> CheckAsync(CancellationToken ct); }`
  - `class ConnectivityService : IConnectivityService` with `ConnectivityService(HttpClient client)`, `static IReadOnlyList<ConnectivityTarget> DefaultTargets`, `static ConnectivityService CreateDefault()` (5 s timeout).
- Targets: `discord` (`https://discord.com`, primary), `discord-gateway` (`https://gateway.discord.gg`, primary), `youtube` (`https://www.youtube.com`, primary), `youtube-cdn` (`https://redirector.googlevideo.com`, secondary). Names: `Discord`, `Discord Gateway`, `YouTube`, `YouTube CDN`.
- `IsConnected` = any result with `Target.IsPrimary && Success`.

- [ ] **Step 1: Write failing tests**

Fake `HttpMessageHandler` with per-host behavior.
- `CheckAsync_AllSuccess_IsConnectedTrue` and 4 results.
- `CheckAsync_OnlySecondarySuccess_IsConnectedFalse`.
- `CheckAsync_PrimaryFailure_RecordedWithError` → `Success false`, `Error` non-null.
- `CheckAsync_Timeout_CompletesAsFailure`: handler delays 5 s, token canceled after 100 ms → returns 4 failures, no exception.
- `CheckAsync_RunsInParallel`: handler records start times; 4 targets × 300 ms delay → total under 1 s.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~ConnectivityServiceTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

Use `HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, ct)`, `Stopwatch` for elapsed, success = response status < 500 (3xx/4xx count as reachable). Catch `OperationCanceledException`/`HttpRequestException` per target → result with error text. Run all with `Task.WhenAll`; preserve target order.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~ConnectivityServiceTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Diagnostics tests/ZprtNextGen.Core.Tests/Diagnostics
git commit -m "feat(core): add parallel connectivity checks"
```

---

### Task 11: Автозапуск и логи

**Files:**
- Create: `src/ZprtNextGen.Core/Startup/Autostart.cs`, `src/ZprtNextGen.Core/Logging/AppLogger.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Startup/AutostartTests.cs`, `tests/ZprtNextGen.Core.Tests/Logging/AppLoggerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static class AutostartCommand` with `BuildCreateArguments(string exePath)`, `BuildDeleteArguments()`.
  - `interface IAutostartService { bool IsEnabled(); void Enable(); void Disable(); }`
  - `class ScheduledTaskAutostart : IAutostartService` (uses `schtasks.exe`, hidden window, throws `InvalidOperationException` with RU message on non-zero exit).
  - `interface IAppLogger { void Info(string message); void Error(string message, Exception? exception = null); }`
  - `class AppLogger : IAppLogger` with `AppLogger(string directory)`.

- [ ] **Step 1: Write failing tests**

- `BuildCreateArguments_QuotesExeAndUsesHighest`: path `C:\Program Files\ZPRT\ZprtNextGen.exe` → contains `/Create`, `/TN "ZPRT_NEXT_GEN"`, `\"C:\Program Files\ZPRT\ZprtNextGen.exe\"`, `/SC ONLOGON`, `/RL HIGHEST`, `/F`.
- `BuildDeleteArguments_ContainsTaskName`.
- `Logger_WritesDailyFile`: temp dir → `Info("привет")`, `Error("бум", new Exception("x"))`; exactly one file `app-YYYYMMDD.log`, contains `[INFO] привет`, `[ERROR] бум`, `x`.
- `Logger_ConcurrentWrites_NoCorruption`: 100 parallel `Info` calls → 100 lines.
- `Logger_CreatesDirectory`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~AutostartTests|FullyQualifiedName~AppLoggerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`BuildCreateArguments` returns exactly:
`/Create /TN "ZPRT_NEXT_GEN" /TR "\"{exePath}\"" /SC ONLOGON /RL HIGHEST /F`
`AppLogger` uses a `lock`, `File.AppendAllText(path, line, new UTF8Encoding(false))`; line format `yyyy-MM-dd HH:mm:ss.fff [LEVEL] message`; `Error` appends exception type/message/stack on following lines. `ScheduledTaskAutostart` runs `schtasks.exe` with `CreateNoWindow = true`, `UseShellExecute = false`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~AutostartTests|FullyQualifiedName~AppLoggerTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.Core/Startup src/ZprtNextGen.Core/Logging tests/ZprtNextGen.Core.Tests
git commit -m "feat(core): add autostart command builder and file logger"
```

---

### Task 12: Состояние анимации и `MeditationView`

**Files:**
- Create: `src/ZprtNextGen.Core/Presentation/AnimationStateResolver.cs`
- Create: `src/ZprtNextGen.App/Controls/MeditationView.xaml`, `src/ZprtNextGen.App/Controls/MeditationView.xaml.cs`
- Test: `tests/ZprtNextGen.Core.Tests/Presentation/AnimationStateResolverTests.cs`

**Interfaces:**
- Consumes: `EngineStatus`, `ConnectivityReport`.
- Produces:
  - `enum MeditationState { Stopped, Connecting, Connected, Error }`
  - `static MeditationState AnimationStateResolver.Resolve(EngineStatus status, ConnectivityReport? report)`
  - WPF `MeditationView` with DPs `State` (`MeditationState`) and `ReduceMotion` (`bool`).

- [ ] **Step 1: Write failing resolver tests**

Cases:
- `Stopped` + null → `Stopped`
- `Starting` + null → `Connecting`
- `Running` + null → `Connecting`
- `Running` + report with primary success → `Connected`
- `Running` + report with only secondary success → `Error`
- `Error` + report → `Error`
- `Stopping` + report → `Connecting`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.Core.Tests --filter "FullyQualifiedName~AnimationStateResolverTests"`
Expected: FAIL.

- [ ] **Step 3: Implement resolver**

`Stopped`; `Error`; `Starting`/`Stopping` → `Connecting`; `Running` → report is null ? `Connecting` : report.IsConnected ? `Connected` : `Error`.

- [ ] **Step 4: Build `MeditationView.xaml`**

Canvas `240×240`, black background, viewbox-stretch. Named elements:
- `FigureGroup` with `RenderTransform` = `TransformGroup` { `LiftTransform` (TranslateY), `BreathTransform` (TranslateY), `ScaleTransform` }.
- `HaloDiamond`: `Path` `M120,20 L212,120 L120,220 L28,120 Z`, `Stroke=#E01B2E`, `StrokeThickness=2`, `Opacity=0.18`. Кисти анимируемых элементов задавать inline на элементе, не в `ResourceDictionary`, иначе WPF заморозит их и `ColorAnimation` упадёт.
- `AuraGlow`: `Ellipse` 150×150 centered, `Fill` radial gradient red→transparent, `Opacity=0.12`.
- `RaysGroup`: 12 `Line` elements from center to radius 105, `Stroke=#FFC65C`, `Opacity=0`, `RotateTransform` named `RaysRotate`, center 120,120.
- Figure paths (stroke line-art, `Stroke=#FF3B4E`, `StrokeThickness=3`, `StrokeLineJoin=Round`):
  - head `Ellipse` center (120,74) r 16;
  - torso `M96,96 C104,90 136,90 144,96 L136,150 C128,158 112,158 104,150 Z`;
  - arms `M99,104 C86,116 78,140 76,156` and mirrored `M141,104 C154,116 162,140 164,156`;
  - knees/hands: small ellipses at (76,158) and (164,158), r 5;
  - crossed legs `M78,168 C96,184 144,184 162,168` and `M92,172 C108,160 132,160 148,172`;
  - inner detail `Stroke=#EDEDED`, `Opacity=0.45`, thickness 1.5 — shoulder arc and chest line.
- `Particles`: 12 small diamond `Path` (`M0,-4 L4,0 L0,4 L-4,0 Z`) distributed on an arc, each `Opacity=0`, named `Particle01`…`Particle12`.
- `ErrorFlash`: full-canvas `Rectangle` `Fill=#E01B2E`, `Opacity=0`.

- [ ] **Step 5: Wire states in code-behind**

`MeditationView.xaml.cs`:
- DP `State` and `ReduceMotion`.
- `OnStateChanged` calls `ApplyState()`; `ApplyState` first `RemoveStoryboards()` then starts the matching storyboard defined in `Resources`:
  - `StoppedStoryboard`: breath 0→-3→0, 4 s, `SineEase`, forever; halo opacity 0.18; rays 0; lift 0; flash 0.
  - `ConnectingStoryboard`: breath 0→-2→0, 1.6 s; aura opacity pulse 0.25→0.55 (1 s autoreverse); rays 0.
  - `ConnectedStoryboard`: lift 0→-52, 1.2 s `CubicEase EaseOut`; scale 1→1.04; bob -52→-58→-52, 3 s forever; halo color `#E01B2E`→`#FFC65C` and opacity 0.18→0.85; aura opacity 0.12→0.8; rays opacity 0→0.7, rotate 0→360 over 24 s forever; particles each `Opacity 0→0.9→0` and `TranslateY 0→-150` over 2.5 s with `BeginTime` 0.2 s × index, repeat forever.
  - `ErrorStoryboard`: flash 0→0.35→0 over 0.5 s; lift/scale return to 0 over 0.4 s; rays 0.
- `ReduceMotion == true`: apply equivalent static values (lift -32, halo gold at 0.5, rays 0.25, no particles, no rotation) and skip looping storyboards.
- Do not start storyboards when `State == Stopped` and `ReduceMotion` (static dim figure).
- `Loaded`/`Unloaded` start/stop the current storyboard.

- [ ] **Step 6: Verify build and manual visual check**

Run: `dotnet build src/ZprtNextGen.App`
Expected: build succeeds.
Manual: temporarily host the control in `MainWindow.xaml` with a `ComboBox` bound to `State`, launch, confirm four states animate, no CPU spike above ~5% idle, `ReduceMotion` disables particles.

- [ ] **Step 7: Commit**

```powershell
git add src/ZprtNextGen.Core/Presentation src/ZprtNextGen.App/Controls tests/ZprtNextGen.Core.Tests/Presentation
git commit -m "feat(app): add meditation animation with state resolver"
```

---

### Task 13: Иконка и фирменные ресурсы

**Files:**
- Create: `tools/New-Icon.ps1`, `assets/icon.svg`, `assets/icon.ico` (generated)
- Modify: `src/ZprtNextGen.App/ZprtNextGen.App.csproj` (ApplicationIcon, copy ico)
- Create: `src/ZprtNextGen.App/Resources/Brand.xaml` (brush/palette resources)

**Interfaces:**
- Consumes: nothing.
- Produces: `assets/icon.ico` with 16,24,32,48,64,128,256 entries; `Brand.xaml` with `Color.Background #0A0A0A`, `Color.Surface #141414`, `Color.Accent #E01B2E`, `Color.AccentGlow #FF3B4E`, `Color.Text #EDEDED`, `Color.TextMuted #8A8A8A`, `Color.Enlightenment #FFC65C`, brushes `Brush.Background`, `Brush.Surface`, `Brush.Accent`, `Brush.AccentGlow`, `Brush.Text`, `Brush.TextMuted`, `Brush.Enlightenment`.

- [ ] **Step 1: Author `assets/icon.svg`**

`viewBox="0 0 256 256"`, black rounded square (`rx=48`), outer diamond `M128,28 L228,128 L128,228 L28,128 Z` stroke `#E01B2E` width 10, inner diamond 60% scale `M128,68 L188,128 L128,188 L68,128 Z` stroke `#FF3B4E` width 4, center dot r 6 `#FFC65C`. No text.

- [ ] **Step 2: Write `tools/New-Icon.ps1`**

Param `-Out assets\icon.ico`. Uses `System.Drawing`:
- for each size in `16,24,32,48,64,128,256`: create `Bitmap(size,size)`, `SmoothingMode.AntiAlias`, black background (rounded rect for ≥48), draw both diamonds and center dot scaled from 256.
- for each bitmap: `hIcon = bitmap.GetHicon()`, `[System.Drawing.Icon]::FromHandle($hIcon)` → `Save` to a `MemoryStream`; the result is a one-entry ICO — copy its 16-byte directory entry and its DIB payload, then `DestroyIcon($hIcon)` via `Add-Type` P/Invoke `user32.dll`.
- pack one ICO: 6-byte header (`0,0,1,0,count`), all 16-byte entries (width byte 0 for 256, height, 0 colors, 0 reserved, planes 1, bitcount 32, payload size, offset), then concatenated DIB payloads with adjusted offsets. **Do not use PNG-compressed entries** — `System.Drawing.Icon` does not reliably load them.
- Save UTF-8 with BOM.

- [ ] **Step 3: Generate and verify icon**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-Icon.ps1 -Out assets\icon.ico
$b=[IO.File]::ReadAllBytes('assets\icon.ico'); "entries=$([BitConverter]::ToUInt16($b,4)) size=$($b.Length)"
```

Expected: `entries=6`; file loads as `[System.Drawing.Icon]::new('assets\icon.ico')` without error.

- [ ] **Step 4: Wire into project**

App csproj: `<ApplicationIcon>..\..\assets\icon.ico</ApplicationIcon>` and `<Content Include="..\..\assets\icon.ico" Link="assets\icon.ico" CopyToOutputDirectory="PreserveNewest" />`. `Brand.xaml` as `ResourceDictionary` with the colors/brushes above; merge it in `App.xaml`.

- [ ] **Step 5: Verify and commit**

Run: `dotnet build src/ZprtNextGen.App`
Expected: succeeds; `bin\...\assets\icon.ico` exists.

```powershell
git add assets tools/New-Icon.ps1 src/ZprtNextGen.App
git commit -m "feat(app): add diamond brand icon and palette"
```

---

### Task 14: `MainViewModel` и команды

**Files:**
- Create: `src/ZprtNextGen.App/ViewModels/MainViewModel.cs`, `.../PresetGroupViewModel.cs`, `.../ConnectivityRowViewModel.cs`, `.../RelayCommand.cs`, `.../AsyncRelayCommand.cs`, `.../IUiDispatcher.cs`
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/MainViewModelTests.cs`, `tests/ZprtNextGen.App.Tests/Fakes/*.cs`

**Interfaces:**
- Consumes: `PresetCatalog`, `AppSettings`, `ISettingsStore`, `EngineValidation`, `IEngineProcessManager`, `IConnectivityService`, `IAutostartService`, `IAppLogger`, `AnimationStateResolver`.
- Produces:
  - `record MainViewModelDependencies(PresetCatalog Catalog, ISettingsStore Settings, EngineValidation Engine, IEngineProcessManager EngineProcess, IConnectivityService Connectivity, IAutostartService Autostart, IAppLogger Logger, IUiDispatcher Dispatcher, Func<string, bool> Confirm, Func<string?, EngineValidation>? LocateEngine = null)`
  - `class MainViewModel : INotifyPropertyChanged` with:
    - `IReadOnlyList<PresetGroupViewModel> Groups`
    - `Preset? SelectedPreset { get; set; }`
    - `bool IsRunning`, `bool IsBusy`, `bool CanStart`, `bool IsAutostartEnabled`, `bool MinimizeToTray`, `bool ReduceMotion`
    - `string EnginePath`, `GameFilterMode GameFilter`
    - `string StatusText`, `MeditationState AnimationState`
    - `ObservableCollection<string> LogLines`
    - `IReadOnlyList<ConnectivityRowViewModel> ConnectivityRows`
    - `ICommand ToggleCommand`, `TestConnectionCommand`, `SelectPresetCommand`, `OpenLogsCommand`, `ToggleAutostartCommand`, `ExitCommand`
    - `Task InitializeAsync()`, `Task ToggleAsync()`, `Task TestConnectionAsync()`, `Task ChangeEnginePathAsync(string path)`, `Task ShutdownAsync(bool stopEngine)`
  - `interface IUiDispatcher { void Post(Action action); }`
  - `PresetGroupViewModel { string Id; string Name; bool IsExpanded; IReadOnlyList<Preset> Presets; }`
  - `ConnectivityRowViewModel { string Name; bool Success; long ElapsedMs; string? Error; }`

- [ ] **Step 1: Write fake collaborators**

`FakeSettingsStore`, `FakeEngineProcess` (records start args, raises `StatusChanged`, can simulate failure), `FakeConnectivity` (returns configured report), `FakeAutostart`, `FakeLogger`, `ImmediateDispatcher` (runs action inline), `ConfirmAlways(bool)`.

- [ ] **Step 2: Write failing VM tests**

- `Initialize_SelectsSavedPresetAndBuildsGroups`: settings preset `general-alt5` → `SelectedPreset.Id == "general-alt5"`; groups count from catalog.
- `Initialize_InvalidEngine_CanStartFalse`: `EngineValidation(IsValid:false, ...)` → `CanStart false`, status contains «движок».
- `ChangeEnginePath_Valid_UpdatesEngineAndPersists`: locate delegate returns valid `C:\NewEngine` → `EnginePath` updated, settings saved, `CanStart true`.
- `Toggle_WhenStopped_StartsEngineWithResolvedArguments`: fake engine root `C:\E`; selected preset args `--wf-tcp=80,{{GameFilterTCP}}`; filter `Tcp` → recorded args contain `--wf-tcp=80,1024-65535` and `C:\E\bin\`.
- `Toggle_WhenRunning_StopsEngine`.
- `Toggle_StartFailure_SetsErrorStateAndLogs`: fake throws → `AnimationState == Error`, `StatusText` contains «Не удалось запустить», logger has error.
- `Toggle_AfterStart_RunsConnectivityAndConnects`: successful report → `AnimationState == Connected`, `IsRunning true`.
- `Toggle_AfterStart_ConnectivityFail_SetsError`.
- `TestConnection_WhenRunning_UpdatesRows` and sets `Connected` only on primary success.
- `SelectPreset_PersistsToSettings`.
- `LogLines_CappedAt500`.
- `Toggle_ForeignProcessAndUserDeclines_DoesNotStart`: fake `IsForeignInstanceRunning true`, confirm false → start not called, `StatusText` mentions «извне».

Use `await vm.InitializeAsync()` and `await vm.ToggleAsync()`; commands are thin wrappers over these methods.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.App.Tests --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — VM missing.

- [ ] **Step 4: Implement VM**

Rules:
- `InitializeAsync`: load settings, build groups with `IsExpanded = !settings.CollapsedGroups.Contains(id)`, select preset (fallback first), set `IsAutostartEnabled`, `MinimizeToTray`, `ReduceMotion`, `EnginePath`, `GameFilter`, and `CanStart = Engine.IsValid`; if engine invalid → `StatusText = "Движок не найден: " + string.Join(", ", Engine.MissingFiles)`; if `EngineProcess.IsForeignInstanceRunning()` → `StatusText = "Обход запущен извне"` and `AnimationState = Connecting`; otherwise `Stopped`.
- `ChangeEnginePathAsync(string path)`: call `LocateEngine(path)`; valid → update `Engine`, `EnginePath`, `settings.EnginePath`, `CanStart = true`, save, `StatusText = "Движок найден"`; invalid → keep current engine, `StatusText = "Папка не подходит: " + string.Join(", ", result.MissingFiles)`.
- `ToggleAsync`: guard `IsBusy` and `CanStart`. If running → `StopAsync`, state `Stopped`. Else if foreign → `Confirm("Обход уже запущен другим способом. Остановите его вручную или закройте оригинальное окно.")`; false → status «извне», no start; true → status «Остановите обход вручную», no start (чужой процесс не убиваем).
- `OpenLogsCommand`: `Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true })`; errors → `StatusText`.
- `ToggleAutostartCommand`: call `Autostart.Enable()`/`Disable()`, persist `Autostart`, on exception revert and set `StatusText`.
- Start: resolve args via `PresetArgumentBuilder.Build(SelectedPreset, engineRoot, settings.GameFilter)`, `StartAsync(Path.Combine(root,"bin","winws.exe"), args, Path.Combine(root,"bin"))`, then `await TestConnectionAsync()`.
- `TestConnectionAsync`: `IsBusy` guard optional; run service, map rows on dispatcher, set `AnimationState = AnimationStateResolver.Resolve(EngineProcess.Status, report)`, if not connected → `StatusText = "Связь не подтверждена"`.
- Logs: subscribe `OutputReceived` → dispatcher → add line; drop oldest beyond 500.
- Persist on preset/autostart/minimize/reduce motion changes.
- All user-facing strings RU; catch exceptions → `logger.Error`, `AnimationState = Error`, `StatusText = $"Ошибка: {ex.Message}"`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ZprtNextGen.App.Tests --filter "FullyQualifiedName~MainViewModelTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```powershell
git add src/ZprtNextGen.App/ViewModels tests/ZprtNextGen.App.Tests
git commit -m "feat(app): add main view model and command wiring"
```

---

### Task 15: Тёмная тема и главное окно

**Files:**
- Create: `src/ZprtNextGen.App/Resources/Theme.xaml`
- Modify: `src/ZprtNextGen.App/App.xaml`, `src/ZprtNextGen.App/MainWindow.xaml`, `src/ZprtNextGen.App/MainWindow.xaml.cs`
- Create: `src/ZprtNextGen.App/Controls/StatusPill.xaml` (optional; may be inline)

**Interfaces:**
- Consumes: `MainViewModel`, `MeditationView`, `Brand.xaml`.
- Produces: window layout with named regions: `HeroPanel` (MeditationView + status pill + toggle), `PresetRegion` (placeholder for Task 16), `ConnectivityPanel`, `LogPanel`, `SettingsPanel`, `AboutButton`.

- [ ] **Step 1: Write `Theme.xaml`**

Dark window style: `Background = Brush.Background`, `Foreground = Brush.Text`, `FontFamily = "Segoe UI Variable, Segoe UI"`, `FontSize 14`. Styles: `PrimaryButton` (accent background `#E01B2E`, white text, corner radius 10, padding 20×10, hover `#FF3B4E`), `SecondaryButton` (surface, border `#2A2A2A`), `ToggleSwitch` (custom `ToggleButton` 44×24 with animated thumb), `Card` (`Border` surface, corner radius 12, padding 16), `StatusPill` (`Border` with dot + text), `LinkButton` (transparent, accent text), `TextBox`/`ComboBox` dark. No external themes.

- [ ] **Step 2: Build `MainWindow.xaml`**

Layout: 1100×760, min 900×620, no default chrome? Keep standard chrome for v1 with dark title bar via `DwmSetWindowAttribute(20, 1)` in code-behind (ignore failure).
Grid rows: hero (auto), preset (auto), connectivity + log (star), footer settings (auto).
- Hero: two columns — `MeditationView` 280×280 left, right: title `ZPRT NEXT GEN`, `StatusPill` bound to `StatusText`, big `ToggleButton`/`Button` bound to `ToggleCommand` with `IsEnabled="{Binding CanStart}"` and text «Включить обход»/«Выключить обход» via trigger on `IsRunning`, and a muted line «Discord · YouTube · без лишних окон».
- Preset region: `ContentControl x:Name="PresetHost"` (Task 16 fills).
- Connectivity: `ItemsControl` over `ConnectivityRows` in a `Card`, each row: dot (green/red via converter), name, `ElapsedMs` ms, error tooltip, button «Проверить связь» bound to `TestConnectionCommand`.
- Log: `ListBox` monospace (`Cascadia Mono, Consolas`) bound to `LogLines`, auto-scroll on collection changed; button «Открыть папку логов».
- Footer/настройки (`Expander` «Настройки»): строка «Папка движка» — `TextBox` (read-only) + `Button` «Обзор…»; `ComboBox` «Game filter» (`Выключен`, `TCP`, `UDP`, `Все`); `ToggleSwitch` «Автозапуск», «Сворачивать в трей», «Уменьшить анимацию»; `Button` «О программе».

- [ ] **Step 3: Wire DataContext**

`MainWindow.xaml.cs` constructor takes `MainViewModel`, sets `DataContext`; `Loaded` → `await vm.InitializeAsync()`; `Closing` → if `MinimizeToTray` and not exiting → cancel + hide; else `await vm.ShutdownAsync(stopEngine: confirm)`. App startup composition is Task 17.

- [ ] **Step 4: Build and launch visual check**

Run: `dotnet build src/ZprtNextGen.App`
Expected: succeeds.
Manual: launch with a temporary `MainWindow` constructed in `App.xaml.cs` using stub data (fake engine validation and an in-memory catalog) — confirm dark theme, RU labels, no binding errors in Output window, resize keeps layout sane.

- [ ] **Step 5: Commit**

```powershell
git add src/ZprtNextGen.App
git commit -m "feat(app): add dark theme and main window layout"
```

---

### Task 16: Сгруппированный селектор пресетов

**Files:**
- Create: `src/ZprtNextGen.App/Controls/PresetSelector.xaml`, `.../PresetSelector.xaml.cs`
- Modify: `MainWindow.xaml` (replace `PresetHost` content)
- Test: `tests/ZprtNextGen.App.Tests/ViewModels/PresetGroupViewModelTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.Groups`, `SelectedPreset`, `SelectPresetCommand`.
- Produces: `PresetSelector` UserControl with DPs `Groups` (`IEnumerable<PresetGroupViewModel>`), `SelectedPreset` (`Preset`), `SelectCommand` (`ICommand`), `CollapsedGroups` (`IList<string>`) or two-way `IsExpanded` on group VMs.

- [ ] **Step 1: Write failing group tests**

- `ExpandedState_FromCollapsedGroups`: group `general` in collapsed list → `IsExpanded false`; others true.
- `ToggleGroup_UpdatesCollapsedList`: toggling updates set and raises `PropertyChanged`.
- `SelectedPreset_ExposesGroupAndName`: selected preset exposes `GroupId` and display name.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.App.Tests --filter "FullyQualifiedName~PresetGroupViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement group VM behavior**

`PresetGroupViewModel` implements `INotifyPropertyChanged`; `IsExpanded` setter raises event; VM subscribes and persists `CollapsedGroups` on change.

- [ ] **Step 4: Build the selector UI**

`PresetSelector.xaml`: `ToggleButton` styled as a field showing `SelectedPreset.Name` + chevron; `Popup` (`Placement=Bottom`, `StaysOpen=false`, `AllowsTransparency=true`) containing `ScrollViewer` max height 420 with `ItemsControl` per group:
- group header: `Button` with chevron ▸/▾ and group name, command toggles `IsExpanded`;
- items: `ListBox`/`ItemsControl` of `Button`s, `IsEnabled`/highlight for selected, each showing `Name` (14 px) and `Id` (11 px, muted);
- click → `SelectCommand.Execute(preset)`, close popup.
Keyboard: `Popup` focus trap; `Up`/`Down` move selection index across visible items, `Enter` selects, `Esc` closes; implement in code-behind with a flat list of visible presets.

- [ ] **Step 5: Verify**

Run: `dotnet build src/ZprtNextGen.App`
Expected: succeeds.
Manual: open popup, groups collapse/expand, 22 presets reachable, selection updates field and persists after restart, `Esc` closes, keyboard navigation works.

- [ ] **Step 6: Commit**

```powershell
git add src/ZprtNextGen.App tests/ZprtNextGen.App.Tests
git commit -m "feat(app): add grouped preset selector"
```

---

### Task 17: Трей, «О программе», запуск приложения

**Files:**
- Create: `src/ZprtNextGen.App/Services/TrayService.cs`, `src/ZprtNextGen.App/Services/SingleInstanceGuard.cs`, `src/ZprtNextGen.App/Services/WpfDispatcher.cs`, `src/ZprtNextGen.App/AboutWindow.xaml`, `.../AboutWindow.xaml.cs`
- Modify: `src/ZprtNextGen.App/App.xaml`, `App.xaml.cs`, `MainWindow.xaml.cs`, `src/ZprtNextGen.App/app.manifest` (verify `requireAdministrator`)
- Test: `tests/ZprtNextGen.App.Tests/Services/SingleInstanceGuardTests.cs`

**Interfaces:**
- Consumes: `MainViewModel`, `TrayService`, `SingleInstanceGuard`.
- Produces:
  - `class SingleInstanceGuard : IDisposable` with `SingleInstanceGuard(string name)`, `bool IsOwner`, `static bool IsElevated()`.
  - `class TrayService : IDisposable` with `TrayService(Icon icon, Action toggle, Action show, Action exit)`, `void UpdateStatus(string tooltip)`.
  - `app.manifest` with `requestedExecutionLevel level="requireAdministrator" uiAccess="false"`.

- [ ] **Step 1: Write failing guard tests**

- `SecondGuard_SameName_IsNotOwner`: create two guards with unique GUID name → first `IsOwner true`, second `false`.
- `AfterDispose_NewGuard_IsOwner`: dispose first, create third → true.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZprtNextGen.App.Tests --filter "FullyQualifiedName~SingleInstanceGuardTests"`
Expected: FAIL.

- [ ] **Step 3: Implement guard and manifest**

`SingleInstanceGuard` wraps `Mutex(initiallyOwned: true, name, out createdNew)` with `Global\ZPRT_NEXT_GEN_<name>`; `IsOwner = createdNew`; dispose releases only if owned. Manifest as above; set `<ApplicationManifest>app.manifest</ApplicationManifest>` (already added in Task 1).

- [ ] **Step 4: Implement tray and About**

`TrayService`: `NotifyIcon` with `Icon` loaded from `Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico")`, `Text = "ZPRT NEXT GEN"`, `ContextMenuStrip` items «Включить/Выключить», «Открыть», «Выход»; double-click → `show`; `UpdateStatus` sets tooltip max 63 chars.
`AboutWindow`: 520×560, dark; version `Assembly.GetExecutingAssembly().GetName().Version`, engine version from catalog, links `https://github.com/Flowseal/zapret-discord-youtube` and `https://github.com/bol-van/zapret` (open via `Process.Start(new ProcessStartInfo(url){UseShellExecute=true})`), MIT text, warning «WinDivert требует прав администратора и может вызывать предупреждения антивируса», disclaimer.

- [ ] **Step 5: Compose startup in `App.xaml.cs`**

Remove `StartupUri`. `OnStartup`:
1. `SingleInstanceGuard("main")`; if not owner → `MessageBox` «ZPRT NEXT GEN уже запущен» → `Shutdown()`.
2. Elevation check: if not admin → `MessageBox` «Запустите приложение от имени администратора» → shutdown.
3. `SettingsStore.CreateDefault()`, load; catalog from `AppContext.BaseDirectory\presets\presets.json` (fallback: embedded copy? — if missing, show RU error with path and exit).
4. `EngineLocator.Locate(settings.EnginePath, AppContext.BaseDirectory)`; invalid → show RU error listing `MissingFiles` and button-less message; still open main window with disabled toggle? **Decision:** open window, toggle disabled, status shows error, settings allow choosing engine folder.
5. Build `HttpClient` + `ConnectivityService.CreateDefault()`, `EngineProcessManager`, `ScheduledTaskAutostart`, `AppLogger(Path.Combine(appData,"logs"))`, `WpfDispatcher`.
6. `MainViewModel`, `MainWindow`, `TrayService`; tray actions call `vm.ToggleAsync()` (fire-and-forget with try/catch), show the window, and initiate exit.
7. Settings UI wiring: «Обзор…» opens `FolderBrowserDialog`; on OK → `await vm.ChangeEnginePathAsync(path)`; `ComboBox` game filter → `vm.GameFilter` (persists); toggles bind to VM properties. «О программе» opens `AboutWindow`.
8. `OnExit`: `vm.ShutdownAsync(stopEngine)` once, dispose tray/guard.

`WpfDispatcher` implements `IUiDispatcher` via `Application.Current.Dispatcher.BeginInvoke`.

- [ ] **Step 6: Build and manual check**

Run: `dotnet build src/ZprtNextGen.App`
Expected: succeeds.
Manual: launching the exe shows UAC; tray icon appears; close button hides to tray; double-click restores; second launch shows message; exit stops engine after confirmation.

- [ ] **Step 7: Commit**

```powershell
git add src/ZprtNextGen.App tests/ZprtNextGen.App.Tests
git commit -m "feat(app): add tray, about window, single instance and startup"
```

---

### Task 18: Упаковка, README, релизный workflow, E2E

**Files:**
- Create: `tools/Pack-Release.ps1`, `.github/workflows/release.yml`
- Modify: `README.md`, `src/ZprtNextGen.App/ZprtNextGen.App.csproj` (publish props)

**Interfaces:**
- Consumes: published app, engine directory, `presets/presets.json`.
- Produces: `publish/ZPRT_NEXT_GEN-<version>-win-x64.zip` with layout `ZprtNextGen.exe`, `presets/`, `engine/`, `assets/`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, `licenses/`.

- [ ] **Step 1: Write `Pack-Release.ps1`**

Params: `-EngineDir` (required), `-PublishDir` (default `publish\app`), `-OutputZip` (default `publish\ZPRT_NEXT_GEN-win-x64.zip`), `-Version` (`0.1.0`).
Steps: validate engine `bin\winws.exe`; clear staging `publish\staging`; copy publish output; copy `engine` (bin, lists, utils if present); ensure `presets\presets.json`; copy assets/licenses; write `VERSION.txt`; `Compress-Archive`. Fail if any required file missing. UTF-8 with BOM.

- [ ] **Step 2: Add publish properties**

App csproj: `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<SelfContained>true</SelfContained>`, `<PublishSingleFile>true</PublishSingleFile>`, `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`, `<PublishReadyToRun>false</PublishReadyToRun>`, `<DebugType>embedded</DebugType>` (inside a `PropertyGroup Condition="'$(Configuration)'=='Release'"`). Keep `presets.json` and `assets` as content.

- [ ] **Step 3: Write release workflow**

`on: workflow_dispatch` with inputs `version` (required) and `engine_url` (required, URL to upstream zip). Add `permissions: contents: write`. Steps: checkout; setup-dotnet 10.0.x; `dotnet publish src/ZprtNextGen.App -c Release`; download `engine_url` with `Invoke-WebRequest` to `engine.zip`; expand to `engine-src`; locate folder containing `bin\winws.exe` (may be nested) and copy to `engine`; run `tools/Pack-Release.ps1`; `actions/upload-artifact`; `softprops/action-gh-release` with `tag_name: v<version>`, files: zip, `generate_release_notes: true`. No push/tag triggers.

- [ ] **Step 4: Write full README (RU)**

Sections: что это и скриншот (placeholder `docs/screenshot.png`), возможности, требования (Windows 10/11 x64, права администратора), быстрый старт (скачать zip → распаковать → запустить), выбор пресета, проверка связи, трей и автозапуск, предупреждение об антивирусе/WinDivert, как собрать (`dotnet build`, `tools\Import-Presets.ps1`, `tools\Pack-Release.ps1`), структура репозитория, лицензия и атрибуция upstream, дисклеймер. Add badge for build workflow.

- [ ] **Step 5: Build release locally and run E2E**

```powershell
dotnet publish src/ZprtNextGen.App -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Pack-Release.ps1 -EngineDir "C:\Users\Machine\Desktop\zapret-discord-youtube-main" -Version "0.1.0"
```

Expected: zip exists, contains `ZprtNextGen.exe`, `presets\presets.json`, `engine\bin\winws.exe`.

E2E checklist (manual, on this machine):
1. Extract zip to a path with spaces/Cyrillic; launch → UAC → window, no binding errors.
2. Select «Основной», toggle on → `winws` starts, animation lifts to «Connected».
3. Open Discord and YouTube → pages load; «Проверить связь» all green.
4. Toggle off → process gone, animation returns to meditation; no orphan `winws`.
5. Close to tray, restore, exit; confirm `winws` stopped.
6. Set engine path to an empty folder → RU error with missing file names; restore valid path.
7. Delete `%APPDATA%\ZPRT_NEXT_GEN\settings.json` while app closed → app starts with defaults.
8. Start `winws` from upstream `.bat`, then app → «запущен извне», no kill without confirmation.
9. Enable «Уменьшить анимацию» → no particles; restart persists.
10. Enable autostart → `schtasks /Query /TN ZPRT_NEXT_GEN` shows task; disable removes it.

- [ ] **Step 6: Commit**

```powershell
git add README.md tools/Pack-Release.ps1 .github/workflows/release.yml src/ZprtNextGen.App/ZprtNextGen.App.csproj
git commit -m "chore: add release packaging, docs and E2E checklist"
```

- [ ] **Step 7: Push and verify CI**

```powershell
git push
gh run list --limit 3
```

Expected: build workflow green. Release workflow is not triggered (manual only); creating a release waits for the user's explicit command.

---

## Self-Review

**Spec coverage**

| Spec section | Task |
|---|---|
| Стек, структура решения | 1 |
| Пресеты, `presets.json`, группы | 2, 3, 4, 5 |
| Плейсхолдеры, game filter | 6 |
| Настройки | 7 |
| Движок и ошибки | 8 |
| Процесс, чужой `winws` | 9 |
| Тест связи | 10 |
| Автозапуск, логи | 11 |
| Анимация и состояния | 12 |
| Иконка, палитра | 13 |
| VM и команды | 14 |
| Тёмная тема, окно | 15 |
| Сгруппированный селектор | 16 |
| Трей, «О программе», старт | 17 |
| Релиз, README, CI | 1, 18 |
| Лицензия/атрибуция | 1, 17, 18 |

**Type consistency:** `Preset`, `PresetGroup`, `PresetCatalog`, `EngineStatus`, `ConnectivityReport`, `MeditationState`, `AppSettings`, `EngineValidation` используются с одинаковыми именами и сигнатурами во всех задачах.

**Review Focus tests:** (1) Task 3/4 — unresolved variable и отсутствующий `winws`; (2) Task 6/8 — пути с пробелами/кириллицей, неполная папка; (3) Task 9/14 — определение чужого процесса и отказ от старта; (4) Task 10/12 — только secondary success → `Error`, таймаут; (5) Task 7 — битый JSON и недоступный файл.
