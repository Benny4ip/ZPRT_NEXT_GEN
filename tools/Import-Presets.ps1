param(
    [Parameter(Mandatory = $true)]
    [string] $Engine,

    [Parameter(Mandatory = $true)]
    [string] $Out,

    [Parameter(Mandatory = $false)]
    [string] $EngineVersion = "unknown"
)

<#
.SYNOPSIS
    Обёртка PowerShell 5.1 над CLI-импортером пресетов ZPRT_NEXT_GEN.
.DESCRIPTION
    Запускает dotnet run для инструмента tools/ZprtNextGen.PresetTool из корня решения.
.PARAMETER Engine
    Путь к папке движка zapret (обязательно).
.PARAMETER Out
    Путь к выходному файлу presets.json (обязательно).
.PARAMETER EngineVersion
    Версия движка для записи в каталог (по умолчанию "unknown").
#>

Push-Location $PSScriptRoot\..
try
{
    dotnet run --project tools/ZprtNextGen.PresetTool -- --engine $Engine --out $Out --engine-version $EngineVersion
}
finally
{
    Pop-Location
}

exit $LASTEXITCODE
