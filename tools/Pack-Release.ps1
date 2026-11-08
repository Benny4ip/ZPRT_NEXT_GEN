#requires -Version 5.1
<#
.SYNOPSIS
    Упаковка релиза ZPRT_NEXT_GEN: вывод dotnet publish + движок winws +
    каталог пресетов, иконка, лицензии -> один zip-архив.
.DESCRIPTION
    Скрипт на PowerShell 5.1. Работает из корня репозитория (или с абсолютными
    путями).Relative пути разрешаются относительно корня репозитория
    (родительская директория tools\).
.PARAMETER EngineDir
    Директория движка zapret (обязательно; должна содержать bin\winws.exe).
.PARAMETER PublishDir
    Директория dotnet publish (по умолчанию publish\app).
.PARAMETER OutputZip
    Итоговый архив (по умолчанию publish\ZPRT_NEXT_GEN-win-x64.zip).
.PARAMETER Version
    Версия релиза (по умолчанию 0.1.0; пишется в staging\VERSION.txt).
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $EngineDir,

    [string] $PublishDir = "publish\app",

    [string] $OutputZip = "publish\ZPRT_NEXT_GEN-win-x64.zip",

    [string] $Version = "0.1.0"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

function Resolve-RepoPath([string] $path)
{
    if ([System.IO.Path]::IsPathRooted($path))
    {
        return $path
    }
    return (Join-Path $RepoRoot $path)
}

function Require-File([string] $path, [string] $what)
{
    if (-not (Test-Path -Path $path -PathType Leaf))
    {
        throw "Отсутствует обязательный файл ${what}: $path"
    }
}

function Require-Dir([string] $path, [string] $what)
{
    if (-not (Test-Path -Path $path -PathType Container))
    {
        throw "Отсутствует обязательная директория ${what}: $path"
    }
}

function Copy-Contents([string] $src, [string] $dest)
{
    if (-not (Test-Path -Path (Join-Path $src '*')))
    {
        return
    }
    New-Item -Path $dest -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $src '*') -Destination $dest -Recurse -Force
}

# ---------------------------------------------------------------------------
# 1. Валидация входных данных
# ---------------------------------------------------------------------------
$engineDir = Resolve-RepoPath $EngineDir
$publishDir = Resolve-RepoPath $PublishDir
$outputZip = Resolve-RepoPath $OutputZip
$staging = Join-Path (Split-Path -Parent $publishDir) 'staging'

$engineExe = Join-Path $engineDir 'bin\winws.exe'
Require-File $engineExe 'bin\winws.exe в -EngineDir'

Require-Dir $publishDir 'директория публикации (-PublishDir)'
Require-File (Join-Path $publishDir 'ZprtNextGen.exe') 'ZprtNextGen.exe в -PublishDir'

$presetsSource = Join-Path $RepoRoot 'presets\presets.json'
Require-File $presetsSource 'presets\presets.json'

$iconSource = Join-Path $RepoRoot 'assets\icon.ico'
Require-File $iconSource 'assets\icon.ico'

$licenseSource = Join-Path $RepoRoot 'LICENSE'
Require-File $licenseSource 'LICENSE'

$noticesSource = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md'
Require-File $noticesSource 'THIRD-PARTY-NOTICES.md'

$licensesDir = Join-Path $RepoRoot 'licenses'

# ---------------------------------------------------------------------------
# 2. Подготовка staging
# ---------------------------------------------------------------------------
if (Test-Path -Path $staging)
{
    Remove-Item -Path $staging -Recurse -Force
}
New-Item -Path $staging -ItemType Directory -Force | Out-Null

# ---------------------------------------------------------------------------
# 3. Копировать вывод publish
# ---------------------------------------------------------------------------
Copy-Item -Path (Join-Path $publishDir '*') -Destination $staging -Recurse -Force

# ---------------------------------------------------------------------------
# 4. Копировать движок (bin, lists, utils, если есть)
# ---------------------------------------------------------------------------
$engineStaging = Join-Path $staging 'engine'
foreach ($sub in @('bin', 'lists', 'utils'))
{
    Copy-Contents -src (Join-Path $engineDir $sub) -dest (Join-Path $engineStaging $sub)
}

# ---------------------------------------------------------------------------
# 5. Каталог пресетов, иконка, лицензии
# ---------------------------------------------------------------------------
$presetsStaging = Join-Path $staging 'presets'
New-Item -Path $presetsStaging -ItemType Directory -Force | Out-Null
Copy-Item -Path $presetsSource -Destination (Join-Path $presetsStaging 'presets.json') -Force

$assetsStaging = Join-Path $staging 'assets'
New-Item -Path $assetsStaging -ItemType Directory -Force | Out-Null
Copy-Item -Path $iconSource -Destination $assetsStaging -Force
Copy-Item -Path $licenseSource -Destination $staging -Force
Copy-Item -Path $noticesSource -Destination $staging -Force

if (Test-Path -Path $licensesDir -PathType Container)
{
    Copy-Contents -src $licensesDir -dest (Join-Path $staging 'licenses')
}

# ---------------------------------------------------------------------------
# 6. Файл версии
# ---------------------------------------------------------------------------
Set-Content -Path (Join-Path $staging 'VERSION.txt') -Value $Version -Encoding UTF8

# ---------------------------------------------------------------------------
# 7. Финальная проверка обязательных файлов в staging
# ---------------------------------------------------------------------------
$requiredInStaging = @(
    (Join-Path $staging 'ZprtNextGen.exe'),
    (Join-Path $staging 'presets\presets.json'),
    (Join-Path $staging 'engine\bin\winws.exe'),
    (Join-Path $staging 'assets\icon.ico'),
    (Join-Path $staging 'LICENSE'),
    (Join-Path $staging 'THIRD-PARTY-NOTICES.md'),
    (Join-Path $staging 'VERSION.txt')
)
foreach ($required in $requiredInStaging)
{
    Require-File $required 'в staging'
}

# ---------------------------------------------------------------------------
# 8. Архивация
# ---------------------------------------------------------------------------
$zipParent = Split-Path -Parent $outputZip
if (-not [string]::IsNullOrEmpty($zipParent))
{
    New-Item -Path $zipParent -ItemType Directory -Force | Out-Null
}
if (Test-Path -Path $outputZip -PathType Leaf)
{
    Remove-Item -Path $outputZip -Force
}

$items = Get-ChildItem -Path $staging -Force
Compress-Archive -Path $items.FullName -DestinationPath $outputZip -Force

Write-Host "Упаковано: $outputZip"
Write-Host "Версия:    $Version"
Write-Host "Движок:    $engineDir"
