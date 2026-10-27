# Draws the app icon - a white rocket on an orange disc - and writes it as a multi-size .ico. The
# shapes are on a 32x32 grid and scaled to each size, so every size is drawn rather than resampled.
#
#   pwsh -File tools\New-AppIcon.ps1
param(
    [string]$Path = (Join-Path $PSScriptRoot '..\src\DevWorkspaceLauncher.WinUI\Assets\AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

$orange = [System.Drawing.Color]::FromArgb(0xF2, 0x71, 0x1C)
$amber = [System.Drawing.Color]::FromArgb(0xFF, 0xC2, 0x3D)
$white = [System.Drawing.Color]::White
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-IconPng([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size / 32, $size / 32)

    $g.FillEllipse([System.Drawing.SolidBrush]::new($orange), 0.5, 0.5, 31, 31)

    $body = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $body.AddBezier(16, 5, 20.5, 8.5, 21.5, 14, 19.8, 20.5)
    $body.AddLine(19.8, 20.5, 12.2, 20.5)
    $body.AddBezier(12.2, 20.5, 10.5, 14, 11.5, 8.5, 16, 5)
    $g.FillPath([System.Drawing.SolidBrush]::new($white), $body)

    $fins = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(12.3, 16), [System.Drawing.PointF]::new(8.8, 22.5), [System.Drawing.PointF]::new(12.9, 21.2))
    $g.FillPolygon([System.Drawing.SolidBrush]::new($white), $fins)
    $fins = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(19.7, 16), [System.Drawing.PointF]::new(23.2, 22.5), [System.Drawing.PointF]::new(19.1, 21.2))
    $g.FillPolygon([System.Drawing.SolidBrush]::new($white), $fins)

    $flame = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(13.8, 21.8), [System.Drawing.PointF]::new(16, 27), [System.Drawing.PointF]::new(18.2, 21.8))
    $g.FillPolygon([System.Drawing.SolidBrush]::new($amber), $flame)

    # The porthole is left out at the smallest sizes, where it would only blur the body.
    if ($size -ge 24) {
        $g.FillEllipse([System.Drawing.SolidBrush]::new($orange), 14, 10.5, 4, 4)
    }

    $g.Dispose()
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

$images = foreach ($size in $sizes) { , (New-IconPng $size) }

New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
$out = [System.IO.BinaryWriter]::new([System.IO.File]::Create($Path))
try {
    $out.Write([uint16]0)              # reserved
    $out.Write([uint16]1)              # type: icon
    $out.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
        $out.Write([byte]$side)
        $out.Write([byte]$side)
        $out.Write([byte]0)            # no palette
        $out.Write([byte]0)
        $out.Write([uint16]1)          # colour planes
        $out.Write([uint16]32)         # bits per pixel
        $out.Write([uint32]$images[$i].Length)
        $out.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($image in $images) { $out.Write($image) }
}
finally {
    $out.Dispose()
}

Write-Host "Wrote $Path"
