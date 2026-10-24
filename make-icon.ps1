# Рисует иконку Universum (звезда на фоне ночного неба) и собирает многоразмерный .ico из PNG-кадров.
param([string]$Out = "$PSScriptRoot\Assets\universum.ico")
Add-Type -AssemblyName System.Drawing

function RoundRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

# Четырёхлучевая звезда с вогнутыми гранями: центр (cx, cy), радиус r, «талия» k.
function StarPath([float]$cx, [float]$cy, [float]$r, [float]$k) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $r * $k
    $pt = { param($x, $y) New-Object System.Drawing.PointF ([float]$x), ([float]$y) }
    $p.AddBezier((& $pt $cx ($cy - $r)), (& $pt ($cx + $w * 0.25) ($cy - $w)), (& $pt ($cx + $w) ($cy - $w * 0.25)), (& $pt ($cx + $r) $cy))
    $p.AddBezier((& $pt ($cx + $r) $cy), (& $pt ($cx + $w) ($cy + $w * 0.25)), (& $pt ($cx + $w * 0.25) ($cy + $w)), (& $pt $cx ($cy + $r)))
    $p.AddBezier((& $pt $cx ($cy + $r)), (& $pt ($cx - $w * 0.25) ($cy + $w)), (& $pt ($cx - $w) ($cy + $w * 0.25)), (& $pt ($cx - $r) $cy))
    $p.AddBezier((& $pt ($cx - $r) $cy), (& $pt ($cx - $w) ($cy - $w * 0.25)), (& $pt ($cx - $w * 0.25) ($cy - $w)), (& $pt $cx ($cy - $r)))
    $p.CloseFigure(); $p
}

$frames = foreach ($s in 256, 64, 48, 32, 16) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 256.0

    # Фон — глубокий космос с фиолетовым отливом
    $bg = RoundRectPath 0 0 $s $s (56 * $k)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s),
        ([System.Drawing.Color]::FromArgb(255, 34, 22, 74)), ([System.Drawing.Color]::FromArgb(255, 8, 10, 22))
    $g.FillPath($bgBrush, $bg)

    # Свечение вокруг звезды
    $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gr = 110 * $k
    $glow.AddEllipse([float](128 * $k - $gr), [float](132 * $k - $gr), [float](2 * $gr), [float](2 * $gr))
    $glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
    $glowBrush.CenterColor = [System.Drawing.Color]::FromArgb(150, 140, 120, 255)
    $glowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 140, 120, 255))
    $g.SetClip($bg); $g.FillPath($glowBrush, $glow)

    # Мелкие звёзды на фоне (на маленьких размерах только мешают)
    if ($s -ge 48) {
        foreach ($d in @(@(52, 60, 5), @(204, 196, 4), @(70, 200, 3), @(196, 70, 3.5))) {
            $dr = $d[2] * $k
            $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 255, 255, 255))), [float]($d[0] * $k - $dr), [float]($d[1] * $k - $dr), [float](2 * $dr), [float](2 * $dr))
        }
    }

    # Главная звезда: от белого к голубому
    $star = StarPath (128 * $k) (132 * $k) (92 * $k) 0.32
    $starBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF (60 * $k), (40 * $k)), (New-Object System.Drawing.PointF (200 * $k), (224 * $k)),
        ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(255, 125, 211, 252))
    $g.FillPath($starBrush, $star)

    # Маленькая звезда-спутник
    if ($s -ge 32) {
        $mini = StarPath (196 * $k) (62 * $k) (24 * $k) 0.3
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 196, 181, 253))), $mini)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save("$PSScriptRoot\Assets\universum.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}

$fs = [System.IO.File]::Create($Out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Dispose()
