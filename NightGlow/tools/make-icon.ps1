# Generates the NightGlow icon (original artwork: crescent moon with a cyan-violet glow).
# Usage: powershell -File tools\make-icon.ps1   (writes src\NightGlow\Assets\*)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\src\NightGlow\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$size

    # rounded square background, deep night gradient
    $r = [int]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $size, $size), ([System.Drawing.Color]::FromArgb(255, 18, 20, 38)), ([System.Drawing.Color]::FromArgb(255, 6, 7, 14))
    $g.FillPath($bg, $path)

    # glow halo
    $cx = $s * 0.5; $cy = $s * 0.5; $R = $s * 0.30
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse([float]($cx - $R * 1.45), [float]($cy - $R * 1.45), [float]($R * 2.9), [float]($R * 2.9))
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
    $pgb.CenterColor = [System.Drawing.Color]::FromArgb(150, 53, 214, 232)
    $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 139, 92, 246))
    $g.SetClip($path)
    $g.FillEllipse($pgb, [float]($cx - $R * 1.45), [float]($cy - $R * 1.45), [float]($R * 2.9), [float]($R * 2.9))

    # crescent: full disc minus offset disc
    $moon = New-Object System.Drawing.Region (New-Object System.Drawing.RectangleF ([float]($cx - $R)), ([float]($cy - $R)), ([float]($R * 2)), ([float]($R * 2)))
    $cut = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cut.AddEllipse([float]($cx - $R * 0.55), [float]($cy - $R * 1.25), [float]($R * 2), [float]($R * 2))
    $disc = New-Object System.Drawing.Drawing2D.GraphicsPath
    $disc.AddEllipse([float]($cx - $R), [float]($cy - $R), [float]($R * 2), [float]($R * 2))
    $moon = New-Object System.Drawing.Region $disc
    $moon.Exclude($cut)
    $mb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF ([float]($cx - $R)), ([float]($cy - $R))), (New-Object System.Drawing.PointF ([float]($cx + $R)), ([float]($cy + $R))), ([System.Drawing.Color]::FromArgb(255, 120, 240, 250)), ([System.Drawing.Color]::FromArgb(255, 155, 110, 255))
    $g.FillRegion($mb, $moon)

    # small star
    if ($size -ge 32) {
        $st = $s * 0.06
        $sx = $cx + $R * 0.62; $sy = $cy - $R * 0.55
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))), [float]($sx - $st / 2), [float]($sy - $st / 2), [float]$st, [float]$st)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$sz] = $ms.ToArray()
    if ($sz -in 32, 256) { [IO.File]::WriteAllBytes((Join-Path $assets "NightGlow-$sz.png"), $pngs[$sz]) }
    $bmp.Dispose()
}

# ICO container with PNG-compressed images
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$sz].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$sz].Length
}
foreach ($sz in $sizes) { $w.Write($pngs[$sz]) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'NightGlow.ico'), $out.ToArray())
"icon written to $assets"
