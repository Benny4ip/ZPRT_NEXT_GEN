#requires -Version 5.1
<#
.SYNOPSIS
    Полная сборка релизных артефактов ZPRT_NEXT_GEN: Standard + Portable.
.DESCRIPTION
    Скрипт на PowerShell 5.1+. Выполняет шаги из спецификации раздела 12.3:
    1. Поиск и валидация движка zapret (winws.exe, WinDivert, list-general.txt).
    2. Прогон unit-тестов в конфигурации Release (если не указан -SkipTests).
    3. Создание artifacts\portable\engine.zip (bin, lists, utils, presets\presets.json).
    4. Публикация Standard-сборки и упаковка через Pack-Release.ps1.
    5. Публикация Portable-сборки с внедрённым ZprtNextGen.Engine.zip.
    6. Проверка структуры portable-сборки (отсутствие внешней папки engine).
    7. Запуск ZprtNextGen.exe --portable-check при наличии прав администратора.
    8. Сохранение raw portable exe и zip-архива.
    9. Печать путей и размеров артефактов.
#>
param(
    [string] $EnginePath,

    [string] $Version = "1.0.0-beta.1",

    [string] $OutputDir = "artifacts",

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Resolve-FullPath([string] $path)
{
    if ([System.IO.Path]::IsPathRooted($path))
    {
        return $path
    }
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $path))
}

function Require-File([string] $path, [string] $what)
{
    if (-not (Test-Path -Path $path -PathType Leaf))
    {
        throw "Отсутствует обязательный файл ${what}: $path"
    }
}

function Test-EngineRoot([string] $dir)
{
    if ([string]::IsNullOrWhiteSpace($dir) -or -not (Test-Path -Path $dir -PathType Container))
    {
        return $false
    }

    $required = @(
        (Join-Path $dir 'bin\winws.exe'),
        (Join-Path $dir 'bin\WinDivert.dll'),
        (Join-Path $dir 'bin\WinDivert64.sys'),
        (Join-Path $dir 'lists\list-general.txt')
    )

    foreach ($file in $required)
    {
        if (-not (Test-Path -Path $file -PathType Leaf))
        {
            return $false
        }
    }

    return $true
}

# ---------------------------------------------------------------------------
# 1. Поиск и валидация движка
# ---------------------------------------------------------------------------
$engineDir = $null

if (-not [string]::IsNullOrWhiteSpace($EnginePath))
{
    $candidate = Resolve-FullPath $EnginePath
    if (Test-Path -Path $candidate -PathType Leaf)
    {
        # Передан путь к winws.exe -> подняться на два уровня (bin\winws.exe -> engine)
        $parent = Split-Path -Parent $candidate
        if ((Split-Path -Leaf $parent) -ieq 'bin')
        {
            $candidate = Split-Path -Parent $parent
        }
    }
    elseif ((Split-Path -Leaf $candidate) -ieq 'bin')
    {
        $candidate = Split-Path -Parent $candidate
    }

    if (-not (Test-EngineRoot $candidate))
    {
        throw "Указанный в -EnginePath путь не содержит полный валидный движок (bin\winws.exe, WinDivert, lists\list-general.txt): $EnginePath"
    }
    $engineDir = $candidate
}
else
{
    $desktop = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)

    $searchCandidates = @(
        (Join-Path $RepoRoot 'engine'),
        (Join-Path (Split-Path -Parent $RepoRoot) 'engine'),
        (Join-Path $desktop 'ZPRT_NEXT_GEN\engine'),
        (Join-Path $desktop 'NOVII ZAPRET\engine')
    )

    if (Test-Path -Path $desktop -PathType Container)
    {
        try
        {
            $desktopSubs = Get-ChildItem -Path $desktop -Directory -ErrorAction SilentlyContinue
            foreach ($sub in $desktopSubs)
            {
                $searchCandidates += (Join-Path $sub.FullName 'engine')
            }
        }
        catch { }
    }

    $validFound = [System.Collections.Generic.List[string]]::new()
    foreach ($c in $searchCandidates)
    {
        if (Test-EngineRoot $c)
        {
            $full = (Resolve-Path $c).Path
            if (-not $validFound.Contains($full))
            {
                $validFound.Add($full)
            }
        }
    }

    if ($validFound.Count -eq 0)
    {
        throw "Движок zapret не найден автоматически. Укажите путь через параметр -EnginePath."
    }

    $engineDir = $validFound[0]
    Write-Host "Найден движок: $engineDir"
}

Write-Host "Используется движок: $engineDir"
Write-Host "Версия релиза:       $Version"

# ---------------------------------------------------------------------------
# 2. Прогон тестов (если не -SkipTests)
# ---------------------------------------------------------------------------
if (-not $SkipTests)
{
    Write-Host "Запуск dotnet test (Release)..."
    & dotnet test (Join-Path $RepoRoot 'ZprtNextGen.sln') -c Release --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Тесты завершились с ошибкой (код $LASTEXITCODE)."
    }
    Write-Host "Все тесты успешно пройдены."
}

# ---------------------------------------------------------------------------
# 3. Подготовка выходных директорий
# ---------------------------------------------------------------------------
$outputDirFull = Resolve-FullPath $OutputDir
if (-not (Test-Path -Path $outputDirFull -PathType Container))
{
    New-Item -Path $outputDirFull -ItemType Directory -Force | Out-Null
}

$portableArtifactsDir = Join-Path $outputDirFull 'portable'
if (-not (Test-Path -Path $portableArtifactsDir -PathType Container))
{
    New-Item -Path $portableArtifactsDir -ItemType Directory -Force | Out-Null
}

# ---------------------------------------------------------------------------
# 4. Создание artifacts\portable\engine.zip
# ---------------------------------------------------------------------------
$portableEngineZip = Join-Path $portableArtifactsDir 'engine.zip'
if (Test-Path -Path $portableEngineZip -PathType Leaf)
{
    Remove-Item -Path $portableEngineZip -Force
}

$engineStaging = Join-Path ([System.IO.Path]::GetTempPath()) ("zprt_engine_zip_" + [Guid]::NewGuid().ToString("N"))
New-Item -Path $engineStaging -ItemType Directory -Force | Out-Null

try
{
    # Копируем bin, lists, utils (если есть)
    foreach ($sub in @('bin', 'lists', 'utils'))
    {
        $srcSub = Join-Path $engineDir $sub
        if (Test-Path -Path $srcSub -PathType Container)
        {
            $destSub = Join-Path $engineStaging $sub
            New-Item -Path $destSub -ItemType Directory -Force | Out-Null
            Copy-Item -Path (Join-Path $srcSub '*') -Destination $destSub -Recurse -Force
        }
    }

    # Копируем пресеты внутрь архива: presets\presets.json
    $presetsStaging = Join-Path $engineStaging 'presets'
    New-Item -Path $presetsStaging -ItemType Directory -Force | Out-Null
    $presetsFile = Join-Path $RepoRoot 'presets\presets.json'
    Require-File $presetsFile 'presets\presets.json'
    Copy-Item -Path $presetsFile -Destination (Join-Path $presetsStaging 'presets.json') -Force

    $itemsToZip = Get-ChildItem -Path $engineStaging -Force
    Compress-Archive -Path $itemsToZip.FullName -DestinationPath $portableEngineZip -Force
    Write-Host "Создан embedded archive: $portableEngineZip"
}
finally
{
    Remove-Item -Path $engineStaging -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# 5. Сборка Standard (dotnet publish) + Pack-Release.ps1
# ---------------------------------------------------------------------------
Write-Host "Публикация Standard-сборки..."
$standardPublishDir = Join-Path $outputDirFull 'standard-publish'
if (Test-Path -Path $standardPublishDir)
{
    Remove-Item -Path $standardPublishDir -Recurse -Force
}

$appProject = Join-Path $RepoRoot 'src\ZprtNextGen.App\ZprtNextGen.App.csproj'

& dotnet publish $appProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=embedded `
    -p:Version=$Version `
    -o $standardPublishDir

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish для Standard завершился с ошибкой (код $LASTEXITCODE)."
}

$standardZip = Join-Path $outputDirFull "ZPRT_NEXT_GEN-v$Version-standard-win-x64.zip"
Write-Host "Упаковка Standard-релиза через Pack-Release.ps1..."
& (Join-Path $PSScriptRoot 'Pack-Release.ps1') `
    -EngineDir $engineDir `
    -PublishDir $standardPublishDir `
    -OutputZip $standardZip `
    -Version $Version

if ($LASTEXITCODE -ne 0)
{
    throw "Pack-Release.ps1 завершился с ошибкой (код $LASTEXITCODE)."
}

# ---------------------------------------------------------------------------
# 6. Сборка Portable (dotnet publish с embedded engine)
# ---------------------------------------------------------------------------
Write-Host "Публикация Portable-сборки с внедрённым движком..."
$portablePublishDir = Join-Path $outputDirFull 'portable-publish'
if (Test-Path -Path $portablePublishDir)
{
    Remove-Item -Path $portablePublishDir -Recurse -Force
}

$portableEngineZipAbs = (Resolve-Path $portableEngineZip).Path

& dotnet publish $appProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=embedded `
    -p:Version=$Version `
    -p:Portable=true `
    "-p:PortableEngineZip=$portableEngineZipAbs" `
    -o $portablePublishDir

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish для Portable завершился с ошибкой (код $LASTEXITCODE)."
}

$portableExeSource = Join-Path $portablePublishDir 'ZprtNextGen.exe'
Require-File $portableExeSource 'ZprtNextGen.exe в portable-publish'

# Проверяем, что в portable-папке нет внешней папки engine
if (Test-Path -Path (Join-Path $portablePublishDir 'engine') -PathType Container)
{
    throw "Ошибка: в portable-директории публикации обнаружена папка engine. Portable должен содержать только один файл exe."
}

# ---------------------------------------------------------------------------
# 7. Проверка --portable-check при наличии прав администратора
# ---------------------------------------------------------------------------
$isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isElevated)
{
    Write-Host "Запуск проверки распаковки --portable-check..."
    $checkCache = Join-Path ([System.IO.Path]::GetTempPath()) ("zprt_cache_test_" + [Guid]::NewGuid().ToString("N"))
    try
    {
        $oldEnv = $env:ZPRT_ENGINE_CACHE_ROOT
        $env:ZPRT_ENGINE_CACHE_ROOT = $checkCache

        $proc = Start-Process -FilePath $portableExeSource -ArgumentList "--portable-check" -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -ne 0)
        {
            throw "--portable-check завершился с ошибкой (код $($proc.ExitCode)). Встроенный движок повреждён или не распаковывается."
        }
        Write-Host "Проверка --portable-check успешно пройдена (код 0)."
    }
    finally
    {
        $env:ZPRT_ENGINE_CACHE_ROOT = $oldEnv
        Remove-Item -Path $checkCache -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else
{
    Write-Warning "PowerShell запущен без прав администратора. Проверка --portable-check пропущена (требуются права для WinDivert/манифеста)."
}

# ---------------------------------------------------------------------------
# 8. Создание финальных артефактов Portable
# ---------------------------------------------------------------------------
$finalPortableExe = Join-Path $outputDirFull "ZPRT_NEXT_GEN-v$Version-portable-win-x64.exe"
Copy-Item -Path $portableExeSource -Destination $finalPortableExe -Force

$finalPortableZip = Join-Path $outputDirFull "ZPRT_NEXT_GEN-v$Version-portable-win-x64.zip"
if (Test-Path -Path $finalPortableZip -PathType Leaf)
{
    Remove-Item -Path $finalPortableZip -Force
}

$portableZipStaging = Join-Path ([System.IO.Path]::GetTempPath()) ("zprt_port_zip_" + [Guid]::NewGuid().ToString("N"))
New-Item -Path $portableZipStaging -ItemType Directory -Force | Out-Null
try
{
    Copy-Item -Path $portableExeSource -Destination (Join-Path $portableZipStaging 'ZprtNextGen.exe') -Force
    Copy-Item -Path (Join-Path $RepoRoot 'LICENSE') -Destination $portableZipStaging -Force
    Copy-Item -Path (Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md') -Destination $portableZipStaging -Force
    Set-Content -Path (Join-Path $portableZipStaging 'VERSION.txt') -Value $Version -Encoding UTF8

    $portItems = Get-ChildItem -Path $portableZipStaging -Force
    Compress-Archive -Path $portItems.FullName -DestinationPath $finalPortableZip -Force
}
finally
{
    Remove-Item -Path $portableZipStaging -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# 9. Итог
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================"
Write-Host "Сборка ZPRT_NEXT_GEN v$Version завершена успешно!"
Write-Host "============================================================"

$artifacts = @(
    $standardZip,
    $finalPortableZip,
    $finalPortableExe
)

foreach ($art in $artifacts)
{
    if (Test-Path -Path $art -PathType Leaf)
    {
        $item = Get-Item $art
        $sizeMb = [math]::Round($item.Length / 1MB, 2)
        Write-Host ("  {0,-50} : {1,6} MB" -f $item.Name, $sizeMb)
    }
}
Write-Host "============================================================"
Write-Host "Директория артефактов: $outputDirFull"
