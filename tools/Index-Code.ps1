param(
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $Root).Path
if (-not $OutDir) { $OutDir = Join-Path $root '.ai' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$sourceRoots = @(
    (Join-Path $root 'src'),
    (Join-Path $root 'tests')
) | Where-Object { Test-Path $_ }

$files = Get-ChildItem -Path $sourceRoots -Recurse -File |
    Where-Object { $_.Extension -in '.cs', '.xaml' } |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object FullName

$records = New-Object System.Collections.Generic.List[object]
$markdown = New-Object System.Collections.Generic.List[string]

function Add-Record([string]$kind, [string]$name, [string]$file, [int]$line) {
    $records.Add([pscustomobject]@{
        kind = $kind
        name = $name
        file = $file
        line = $line
    }) | Out-Null
}

$fileCount = 0
$typeCount = 0

foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
    $lines = Get-Content -Path $file.FullName -Encoding UTF8
    $entries = New-Object System.Collections.Generic.List[string]
    $lineNo = 0
    $isXaml = $file.Extension -eq '.xaml'

    foreach ($line in $lines) {
        $lineNo++
        $trimmed = $line.Trim()

        if ($isXaml) {
            if ($trimmed -match 'x:Class="([^"]+)"') {
                Add-Record 'xaml-class' $Matches[1] $relative $lineNo
                $entries.Add("- L${lineNo} x:Class **$($Matches[1])**") | Out-Null
                continue
            }
            if ($trimmed -match 'x:Key="([^"]+)"') {
                Add-Record 'xaml-key' $Matches[1] $relative $lineNo
                $entries.Add("- L${lineNo} key ``$($Matches[1])``") | Out-Null
                continue
            }
            if ($trimmed -match 'x:Name="([^"]+)"') {
                Add-Record 'xaml-name' $Matches[1] $relative $lineNo
                $entries.Add("- L${lineNo} name ``$($Matches[1])``") | Out-Null
                continue
            }
            if ($trimmed -match '\bClick="([^"]+)"') {
                Add-Record 'xaml-handler' $Matches[1] $relative $lineNo
                $entries.Add("- L${lineNo} Click ``$($Matches[1])``") | Out-Null
                continue
            }
            continue
        }

        if ($trimmed.Length -eq 0) { continue }
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('/*')) { continue }

        if ($trimmed -match '^namespace\s+([\w\.]+)') {
            Add-Record 'namespace' $Matches[1] $relative $lineNo
            $entries.Add("- L${lineNo} namespace **$($Matches[1])**") | Out-Null
            continue
        }

        if ($trimmed -match '\b(class|interface|record|struct|enum)\s+([A-Za-z_]\w*)') {
            $kind = $Matches[1]
            $name = $Matches[2]
            Add-Record $kind $name $relative $lineNo
            $entries.Add("- L${lineNo} **$kind $name**") | Out-Null
            $typeCount++
            continue
        }

        if ($trimmed -match '^(public|internal|protected)\s') {
            if ($trimmed -match '([A-Za-z_]\w*)\s*(?:<[^>]+>)?\s*\(') {
                $name = $Matches[1]
                Add-Record 'method' $name $relative $lineNo
                $entries.Add("- L${lineNo} method ``$name``") | Out-Null
                continue
            }
            if ($trimmed -match '([A-Za-z_]\w*)\s*(\{|=>)') {
                $name = $Matches[1]
                Add-Record 'property' $name $relative $lineNo
                $entries.Add("- L${lineNo} property ``$name``") | Out-Null
                continue
            }
        }
    }

    if ($entries.Count -gt 0) {
        $fileCount++
        $markdown.Add("### $relative") | Out-Null
        foreach ($entry in $entries) { $markdown.Add($entry) | Out-Null }
        $markdown.Add('') | Out-Null
    }
}

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm'
$header = @(
    '# Code index (generated)',
    '',
    "Generated: $stamp",
    "Root: $root",
    "Files with entries: $fileCount",
    "Types: $typeCount",
    '',
    '> Regenerate: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\Index-Code.ps1`',
    ''
)

$indexPath = Join-Path $OutDir 'code-index.md'
$jsonPath = Join-Path $OutDir 'code-index.json'

Set-Content -Path $indexPath -Value (($header + $markdown) -join [Environment]::NewLine) -Encoding UTF8
$records | ConvertTo-Json -Depth 3 | Set-Content -Path $jsonPath -Encoding UTF8

Write-Output "index: $indexPath"
Write-Output "json:  $jsonPath"
Write-Output "files: $fileCount, records: $($records.Count), types: $typeCount"
