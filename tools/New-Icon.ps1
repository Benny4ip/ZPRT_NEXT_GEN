param(
    [Parameter(Mandatory = $false)]
    [string] $Out = 'assets\icon.ico'
)

<#
.SYNOPSIS
    Собирает ICO-файл бренда ZPRT_NEXT_GEN из растрового образа.
.DESCRIPTION
    Для каждого размера 16,24,32,48,64,128,256 создаёт Bitmap(size,size) с
    сглаживанием (SmoothingMode.AntiAlias), чёрным фоном (скруглённый квадрат
    rx=48 для размеров >= 48), рисует оба алмаза и центральную точку, масштабированные
    из viewBox 256x256, сохраняет кадр через [System.Drawing.Icon] в промежуточный
    одностраничный ICO (MemoryStream), копирует 16-байтную запись каталога и DIB-
    полезную нагрузку, затем DestroyIcon(hIcon) через P/Invoke user32.dll.
    Итоговый ICO: 6-байтный заголовок (0,0,1,0,count), все 16-байтные записи
    (для 256 ширина/высота = 0), затем склеенные DIB-полезные нагрузки с
    поправленными смещениями. PNG-сжатые записи не используются.
.PARAMETER Out
    Путь к выходному файлу .ico (по умолчанию assets\icon.ico, относительно корня решения).
#>

Push-Location $PSScriptRoot\..
try
{
    Add-Type -AssemblyName System.Drawing

    Add-Type @"
        using System;
        using System.Runtime.InteropServices;
        public static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool DestroyIcon(IntPtr hIcon);
        }
"@

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $baseSize = 256.0

    $colorOuter = [System.Drawing.Color]::FromArgb(0xE0, 0x1B, 0x2E)
    $colorInner = [System.Drawing.Color]::FromArgb(0xFF, 0x3B, 0x4E)
    $colorDot   = [System.Drawing.Color]::FromArgb(0xFF, 0xC6, 0x5C)
    $brushBlack = [System.Drawing.Brushes]::Black

    $entries  = New-Object System.Collections.Generic.List[byte[]]
    $payloads = New-Object System.Collections.Generic.List[byte[]]

    foreach ($size in $sizes)
    {
        $scale = $size / $baseSize

        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

        # Чёрный фон: скруглённый квадрат (rx=48 в 256) для >= 48, иначе сплошной.
        if ($size -ge 48)
        {
            $rx = 48.0 * $scale
            $radius = 2.0 * $rx
            $last = [single]($size - $radius)
            $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $path.AddArc(0.0, 0.0, $radius, $radius, 180.0, 90.0)
            $path.AddArc($last, 0.0, $radius, $radius, 270.0, 90.0)
            $path.AddArc($last, $last, $radius, $radius, 0.0, 90.0)
            $path.AddArc(0.0, $last, $radius, $radius, 90.0, 90.0)
            $path.CloseFigure()
            $graphics.FillPath($brushBlack, $path)
            $path.Dispose()
        }
        else
        {
            $graphics.Clear([System.Drawing.Color]::Black)
        }

        $outerPen = [System.Drawing.Pen]::new($colorOuter, [single](10.0 * $scale))
        $outerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $innerPen = [System.Drawing.Pen]::new($colorInner, [single](4.0 * $scale))
        $innerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        $outer = @(
            [System.Drawing.PointF]::new([single](128.0 * $scale), [single](28.0 * $scale)),
            [System.Drawing.PointF]::new([single](228.0 * $scale), [single](128.0 * $scale)),
            [System.Drawing.PointF]::new([single](128.0 * $scale), [single](228.0 * $scale)),
            [System.Drawing.PointF]::new([single](28.0 * $scale),  [single](128.0 * $scale))
        )
        $inner = @(
            [System.Drawing.PointF]::new([single](128.0 * $scale), [single](68.0 * $scale)),
            [System.Drawing.PointF]::new([single](188.0 * $scale), [single](128.0 * $scale)),
            [System.Drawing.PointF]::new([single](128.0 * $scale), [single](188.0 * $scale)),
            [System.Drawing.PointF]::new([single](68.0 * $scale),  [single](128.0 * $scale))
        )

        $outerPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $outerPath.AddLines($outer)
        $outerPath.CloseFigure()
        $graphics.DrawPath($outerPen, $outerPath)
        $outerPath.Dispose()

        $innerPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $innerPath.AddLines($inner)
        $innerPath.CloseFigure()
        $graphics.DrawPath($innerPen, $innerPath)
        $innerPath.Dispose()

        $r = 6.0 * $scale
        $brushDot = [System.Drawing.SolidBrush]::new($colorDot)
        $graphics.FillEllipse($brushDot, [single](128.0 * $scale - $r), [single](128.0 * $scale - $r), [single](2.0 * $r), [single](2.0 * $r))
        $brushDot.Dispose()

        $outerPen.Dispose()
        $innerPen.Dispose()
        $graphics.Dispose()

        $hIcon = $bitmap.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $ms = [System.IO.MemoryStream]::new()
        try
        {
            $icon.Save($ms)
            $raw = $ms.ToArray()
        }
        finally
        {
            $ms.Dispose()
            $icon.Dispose()
        }

        # Данные кадра скопированы в поток — HICON можно уничтожать.
        [void][NativeMethods]::DestroyIcon($hIcon)
        $bitmap.Dispose()

        # Промежуточный ICO: [6-byte header][16-byte directory entry][DIB payload]
        $entrySize  = [BitConverter]::ToUInt32($raw, 14)   # bytes 8-11 of the entry
        $entryStart = [BitConverter]::ToUInt32($raw, 18)   # bytes 12-15 of the entry

        $entry = New-Object byte[] 16
        [System.Array]::Copy($raw, 6, $entry, 0, 16)
        $payload = New-Object byte[] $entrySize
        [System.Array]::Copy($raw, [long]$entryStart, $payload, 0, [long]$entrySize)

        # Размер/высота: 0 означает 256; смещение поправляется при упаковке.
        $entry[0] = if ($size -eq 256) { 0 } else { [byte]$size }
        $entry[1] = if ($size -eq 256) { 0 } else { [byte]$size }

        $entries.Add($entry)
        $payloads.Add($payload)
    }

    $header = New-Object byte[] 6
    $header[2] = 1                                               # type = 1 (icon)
    [System.Array]::Copy([BitConverter]::GetBytes([uint16]$sizes.Count), 0, $header, 4, 2)

    $nextOffset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++)
    {
        $entry = $entries[$i]
        [System.Array]::Copy([BitConverter]::GetBytes([uint32]$nextOffset), 0, $entry, 12, 4)
        $nextOffset += $payloads[$i].Length
    }

    $result = New-Object System.Collections.Generic.List[byte]
    $result.AddRange($header)
    foreach ($entry in $entries)  { $result.AddRange($entry) }
    foreach ($payload in $payloads) { $result.AddRange($payload) }

    $outDir = [System.IO.Path]::GetDirectoryName($Out)
    if ($outDir -and -not [System.IO.Directory]::Exists($outDir))
    {
        [System.IO.Directory]::CreateDirectory($outDir) | Out-Null
    }
    [System.IO.File]::WriteAllBytes($Out, $result.ToArray())

    Write-Host ("Wrote {0} with {1} icon entries." -f $Out, $sizes.Count)
}
finally
{
    Pop-Location
}
