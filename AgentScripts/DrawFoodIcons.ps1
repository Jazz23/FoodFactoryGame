# Draws the DEVELOPMENT 2D icons of decision 0043's food items and kitchen stations as 128x128 transparent PNGs with GDI+,
# shaded like the decision 0040 icons (soft vertical gradients, a thin darker edge, a gloss highlight and a floor shadow).
# Re-run to regenerate: powershell -ExecutionPolicy Bypass -File AgentScripts/DrawFoodIcons.ps1
# AgentScripts/BuildKitchenContent.cs imports them as sprites and names each item's icon after its file.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\Assets\Art\Icons'
New-Item -ItemType Directory -Force $out | Out-Null

function C($hex, $alpha = 255) {
    if ($hex -is [System.Drawing.Color]) { return [System.Drawing.Color]::FromArgb($alpha, $hex.R, $hex.G, $hex.B) }
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

# A colour darkened (factor < 1) for edges and shading.
function Dark($hex, $factor = 0.62) {
    $c = C $hex
    [System.Drawing.Color]::FromArgb(255, [int]($c.R * $factor), [int]($c.G * $factor), [int]($c.B * $factor))
}

function New-Icon {
    $bitmap = New-Object System.Drawing.Bitmap 128, 128, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bitmap, $g)
}

function Save-Icon($bitmap, $g, $name) {
    $g.Dispose()
    $bitmap.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function Shadow($g, $x, $y, $w, $h) {
    $brush = New-Object System.Drawing.SolidBrush (C '#000000' 60)
    $g.FillEllipse($brush, [float]$x, [float]$y, [float]$w, [float]$h)
}

# Fills a path with a top-to-bottom gradient (bottom defaults to a darker top) and draws a thin darker edge.
function Paint($g, $path, $top, $bottom = $null, $edge = $true) {
    $bounds = $path.GetBounds()
    $rect = New-Object System.Drawing.RectangleF ($bounds.X - 1), ($bounds.Y - 1), ($bounds.Width + 2), ($bounds.Height + 2)
    $low = if ($bottom) { C $bottom } else { Dark $top 0.72 }
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C $top), $low, 90
    $g.FillPath($brush, $path)
    if ($edge) {
        $pen = New-Object System.Drawing.Pen (Dark $top 0.5), 2
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($pen, $path)
    }
}

function Ell($g, $x, $y, $w, $h, $top, $bottom = $null, $edge = $true) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse([float]$x, [float]$y, [float]$w, [float]$h)
    Paint $g $path $top $bottom $edge
}

function Rnd($g, $x, $y, $w, $h, $r, $top, $bottom = $null, $edge = $true) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]($r * 2)
    $path.AddArc([float]$x, [float]$y, $d, $d, 180, 90)
    $path.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $path.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    Paint $g $path $top $bottom $edge
}

function Points([float[]]$p) {
    for ($i = 0; $i -lt $p.Length; $i += 2) { New-Object System.Drawing.PointF $p[$i], $p[$i + 1] }
}

function Poly($g, [float[]]$p, $top, $bottom = $null, $edge = $true) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]](Points $p))
    Paint $g $path $top $bottom $edge
}

# A smooth closed shape through the points.
function Blob($g, [float[]]$p, $top, $bottom = $null, $edge = $true) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddClosedCurve([System.Drawing.PointF[]](Points $p), 0.5)
    Paint $g $path $top $bottom $edge
}

function Line($g, $hex, $width, $x1, $y1, $x2, $y2, $alpha = 255) {
    $pen = New-Object System.Drawing.Pen (C $hex $alpha), $width
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
}

function Arc($g, $hex, $width, $x, $y, $w, $h, $start, $sweep, $alpha = 255) {
    $pen = New-Object System.Drawing.Pen (C $hex $alpha), $width
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [float]$x, [float]$y, [float]$w, [float]$h, [float]$start, [float]$sweep)
}

function Spot($g, $hex, $x, $y, $w, $h, $alpha = 255) {
    $brush = New-Object System.Drawing.SolidBrush (C $hex $alpha)
    $g.FillEllipse($brush, [float]$x, [float]$y, [float]$w, [float]$h)
}

function Gloss($g, $x, $y, $w, $h, $alpha = 110) { Spot $g '#ffffff' $x $y $w $h $alpha }

# A bowl seen from the front and above, with its contents' surface ($food) at the rim.
function Bowl($g, $food, $foodLow = $null, $bowl = '#f4f1ea') {
    Shadow $g 16 98 96 18
    Ell $g 14 44 100 30 (Dark $bowl 0.8) $null $false
    Ell $g 18 46 92 24 $food $foodLow
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc([float]14, [float]30, [float]100, [float]76, 0, 180)
    $path.CloseFigure()
    Paint $g $path $bowl '#c9c2b4'
    Gloss $g 26 64 22 16 120
}

# A plate seen from above at an angle.
function Plate($g) {
    Shadow $g 10 96 108 20
    Ell $g 8 58 112 48 '#fbfaf6' '#d9d4c8'
    Ell $g 22 64 84 34 '#f1eee6' '#e4dfd3' $false
}

function Draw($name, [scriptblock]$body) {
    $bitmap, $g = New-Icon
    & $body $g
    Save-Icon $bitmap $g $name
}

# ---------- Raw ingredients ----------
Draw 'Flour' {
    param($g)
    Shadow $g 22 100 84 16
    Blob $g @(30, 104, 24, 70, 30, 40, 44, 26, 64, 34, 84, 26, 98, 40, 104, 70, 98, 104, 64, 110) '#f3ead7' '#d8c8a6'
    Line $g '#b49a6c' 3 40 34 88 34
    Rnd $g 40 56 48 30 6 '#3f8fb0' '#2c6d88'
    Spot $g '#ffffff' 52 62 24 18 230
    Gloss $g 32 46 14 30 90
}
Draw 'Potato' {
    param($g)
    Shadow $g 18 96 92 18
    Blob $g @(18, 72, 28, 44, 58, 32, 94, 36, 112, 60, 104, 92, 66, 100, 30, 96) '#d9a95f' '#a8743a'
    foreach ($e in @(@(40, 54), @(70, 46), @(86, 74), @(52, 82))) { Spot $g '#8a5a28' $e[0] $e[1] 6 4 200 }
    Gloss $g 40 42 30 12 100
}
Draw 'Tomato' {
    param($g)
    Shadow $g 20 100 88 16
    Ell $g 18 34 92 72 '#f0472e' '#b51f12'
    Poly $g @(64, 28, 72, 40, 86, 36, 76, 46, 84, 56, 66, 48, 52, 58, 56, 46, 42, 38, 58, 40) '#5fae3b' '#3a7c22'
    Line $g '#3a7c22' 4 64 22 64 32
    Gloss $g 32 46 26 16 130
}
Draw 'Lettuce' {
    param($g)
    Shadow $g 16 98 96 18
    Blob $g @(14, 74, 24, 42, 50, 26, 80, 26, 106, 42, 114, 74, 92, 104, 36, 104) '#8fd65a' '#4a9a2e'
    Arc $g '#3f8a26' 3 34 36 40 60 200 120
    Arc $g '#3f8a26' 3 56 32 40 66 230 100
    Line $g '#e9f7d6' 4 64 98 64 50 200
    Line $g '#e9f7d6' 3 64 70 46 56 180
    Line $g '#e9f7d6' 3 64 66 82 52 180
    Gloss $g 30 40 22 14 90
}
Draw 'Milk' {
    param($g)
    Shadow $g 28 102 72 14
    Poly $g @(36, 44, 92, 44, 92, 108, 36, 108) '#ffffff' '#dfe6ee'
    Poly $g @(36, 44, 50, 22, 78, 22, 92, 44) '#e9eef4' '#c8d2de'
    Rnd $g 58 14 14 10 2 '#3f7fd0' '#2a5ea6'
    Rnd $g 40 62 48 26 4 '#3f7fd0' '#2a5ea6'
    Spot $g '#ffffff' 48 66 18 18 220
    Gloss $g 40 48 8 54 120
}
Draw 'Egg' {
    param($g)
    Shadow $g 26 100 76 16
    Blob $g @(64, 18, 88, 34, 98, 66, 90, 96, 64, 108, 38, 96, 30, 66, 40, 34) '#fbf3e3' '#e2cfa9'
    Gloss $g 44 36 18 28 150
}
Draw 'Beef' {
    param($g)
    Shadow $g 14 96 100 18
    Blob $g @(16, 66, 30, 36, 66, 28, 100, 36, 114, 62, 104, 92, 64, 100, 26, 92) '#d9433f' '#9c2124'
    Blob $g @(26, 64, 36, 44, 64, 38, 94, 44, 104, 62, 96, 84, 64, 90, 34, 84) '#e8605a' '#c23a37' $false
    Arc $g '#ffe0dc' 3 40 50 50 30 200 140 200
    Arc $g '#ffe0dc' 3 58 58 36 22 190 150 200
    Ell $g 84 52 16 14 '#fff2ea' '#e9d5c8'
}
Draw 'Chicken' {
    param($g)
    Shadow $g 18 98 92 16
    Blob $g @(22, 60, 34, 34, 62, 26, 86, 40, 84, 66, 62, 82, 34, 82) '#f5c3b4' '#d68f7e'
    Rnd $g 74 64 34 12 6 '#f6efe6' '#d6cabc'
    Ell $g 98 56 16 14 '#f6efe6' '#d6cabc'
    Ell $g 98 70 16 14 '#f6efe6' '#d6cabc'
    Gloss $g 36 38 22 12 100
}
Draw 'Cheese' {
    param($g)
    Shadow $g 14 96 100 18
    Poly $g @(14, 84, 104, 40, 114, 62, 114, 96, 14, 104) '#f7c948' '#d89e1c'
    Poly $g @(14, 84, 104, 40, 114, 62, 24, 92) '#ffe17a' '#f4c444'
    foreach ($e in @(@(36, 92, 10, 8), @(60, 86, 12, 10), @(88, 80, 10, 10), @(98, 92, 8, 6))) { Spot $g '#c88a14' $e[0] $e[1] $e[2] $e[3] 220 }
}
Draw 'Fish' {
    param($g)
    Shadow $g 14 92 100 18
    Blob $g @(14, 64, 34, 44, 70, 38, 96, 52, 96, 76, 70, 90, 34, 84) '#8fb4c8' '#4f7890'
    Poly $g @(92, 64, 118, 42, 112, 64, 118, 86) '#7aa0b6' '#4f7890'
    Spot $g '#1d2830' 26 56 8 8
    Arc $g '#4f7890' 3 40 46 20 36 300 120
    Gloss $g 40 46 40 10 110
}

# ---------- Intermediates ----------
Draw 'CutPotatoes' {
    param($g)
    Shadow $g 18 98 92 16
    $i = 0
    foreach ($s in @(@(22, 40), @(38, 30), @(54, 44), @(70, 32), @(86, 42))) {
        Rnd $g $s[0] $s[1] 14 58 3 '#f3dca0' '#d8b56a'
        $i++
    }
    Rnd $g 30 74 70 26 4 '#f3dca0' '#d8b56a'
}
Draw 'Fries' {
    param($g)
    Shadow $g 26 102 76 14
    foreach ($s in @(@(38, 18, -8), @(50, 12, -3), @(62, 10, 0), @(74, 14, 4), @(84, 20, 8))) {
        Rnd $g $s[0] $s[1] 11 60 3 '#ffd25a' '#e3a12c'
    }
    Poly $g @(28, 46, 100, 46, 92, 108, 36, 108) '#e8352a' '#b11f17'
    Spot $g '#ffd25a' 54 66 22 22 255
    Gloss $g 34 52 8 46 90
}
Draw 'TomatoSauce' {
    param($g)
    Shadow $g 26 102 76 14
    Rnd $g 30 40 68 66 10 '#d8392a' '#9c1f15'
    Rnd $g 34 26 60 18 5 '#cfd6dc' '#9aa4ad'
    Rnd $g 40 58 48 30 4 '#fff5dc' '#e9dcb8'
    Ell $g 54 62 20 18 '#e8402e' '#b5241a'
    Gloss $g 36 46 8 50 110
}
Draw 'TomatoSoup' { param($g); Bowl $g '#e2552f' '#b9381a'; Arc $g '#fff3e0' 3 46 52 30 10 200 140 200; Spot $g '#5fae3b' 70 52 6 4 }
Draw 'Bun' {
    param($g)
    Shadow $g 14 98 100 18
    Ell $g 14 72 100 28 '#d89a52' '#a86c2c'
    Blob $g @(14, 82, 22, 50, 64, 34, 106, 50, 114, 82) '#e9ad5e' '#bb7a34'
    foreach ($e in @(@(40, 52), @(58, 46), @(76, 50), @(64, 60), @(46, 64), @(84, 64))) { Spot $g '#fff6dc' $e[0] $e[1] 6 3 230 }
    Gloss $g 36 46 34 10 90
}
Draw 'Patty' {
    param($g)
    Shadow $g 14 98 100 18
    Ell $g 14 56 100 44 '#c44a44' '#8e2a27'
    Ell $g 18 52 92 36 '#dc6660' '#b8403b' $false
    foreach ($e in @(@(36, 60), @(56, 56), @(76, 62), @(48, 70), @(70, 72), @(88, 66))) { Spot $g '#f2b1aa' $e[0] $e[1] 7 4 220 }
}
Draw 'CookedPatty' {
    param($g)
    Shadow $g 14 98 100 18
    Ell $g 14 56 100 44 '#7a4426' '#4e2814'
    Ell $g 18 52 92 36 '#94562f' '#6e3c1e' $false
    foreach ($y in @(58, 68, 78)) { Line $g '#3a1d0c' 5 ($y - 30) $y ($y + 46) ($y - 8) 220 }
    Gloss $g 30 56 20 8 70
}
Draw 'ChoppedLettuce' { param($g); Bowl $g '#8fd65a' '#4a9a2e'; foreach ($e in @(@(30, 48), @(52, 50), @(74, 48), @(42, 56), @(66, 56), @(86, 54))) { Spot $g '#c8f09e' $e[0] $e[1] 12 6 230 } }
Draw 'PizzaBase' {
    param($g)
    Shadow $g 10 96 108 20
    Ell $g 8 50 112 52 '#f2d9a6' '#d5b47a'
    Ell $g 18 56 92 40 '#f8e7c3' '#ead2a2' $false
    foreach ($e in @(@(40, 66), @(70, 62), @(56, 78), @(84, 76))) { Spot $g '#d9b880' $e[0] $e[1] 5 3 220 }
}
Draw 'Pasta' {
    param($g)
    Shadow $g 18 98 92 16
    Ell $g 18 44 92 58 '#f4d067' '#d6a832'
    foreach ($r in @(@(26, 50, 76, 46), @(34, 56, 60, 36), @(42, 62, 44, 26), @(50, 68, 28, 16))) { Arc $g '#c99520' 3 $r[0] $r[1] $r[2] $r[3] 0 300 }
    Gloss $g 32 50 24 10 90
}
Draw 'GroundBeef' {
    param($g)
    Shadow $g 16 98 96 16
    Blob $g @(16, 92, 26, 60, 50, 40, 78, 38, 104, 58, 112, 92) '#d4504b' '#9c2a28'
    foreach ($e in @(@(34, 66), @(50, 54), @(66, 48), @(82, 58), @(44, 78), @(62, 70), @(80, 76), @(96, 80), @(28, 84))) { Spot $g '#f39c94' $e[0] $e[1] 8 5 230 }
}
Draw 'Bolognese' { param($g); Bowl $g '#a8432a' '#7a2a17'; foreach ($e in @(@(34, 50), @(54, 54), @(74, 50), @(46, 58), @(66, 60), @(86, 54))) { Spot $g '#5e2412' $e[0] $e[1] 7 5 230 } }
Draw 'Breading' { param($g); Bowl $g '#e3b45e' '#c08a32'; foreach ($e in @(@(30, 50), @(44, 54), @(58, 49), @(72, 55), @(86, 50), @(52, 58), @(80, 58), @(38, 58))) { Spot $g '#a8711f' $e[0] $e[1] 4 3 230 } }
Draw 'Batter' {
    param($g)
    Bowl $g '#f6e3a0' '#e3c76c'
    Arc $g '#e2c45e' 3 40 50 46 12 190 160
    Line $g '#9aa4ad' 4 82 48 108 14
    Ell $g 70 36 22 26 '#cfd6dc' '#9aa4ad'
}
Draw 'FriedChicken' {
    param($g)
    Shadow $g 18 98 92 16
    Blob $g @(20, 60, 30, 32, 60, 22, 88, 36, 86, 64, 62, 82, 32, 82) '#e3a046' '#a8661c'
    foreach ($e in @(@(34, 40), @(52, 34), @(70, 40), @(40, 58), @(60, 52), @(76, 60), @(50, 70))) { Spot $g '#f6c470' $e[0] $e[1] 8 6 230 }
    Rnd $g 76 64 30 12 6 '#f6efe6' '#d6cabc'
    Ell $g 98 56 16 14 '#f6efe6' '#d6cabc'
    Ell $g 98 70 16 14 '#f6efe6' '#d6cabc'
}
Draw 'FriedFish' {
    param($g)
    Shadow $g 14 96 100 18
    Blob $g @(14, 70, 28, 46, 64, 38, 100, 44, 114, 66, 100, 90, 60, 96, 26, 92) '#e9a948' '#b06e1e'
    foreach ($e in @(@(30, 58), @(50, 50), @(72, 48), @(92, 56), @(40, 74), @(64, 70), @(86, 74))) { Spot $g '#f8cf7a' $e[0] $e[1] 9 6 230 }
    Ell $g 84 82 18 12 '#ffe066' '#e8b828'
}

# ---------- Dishes ----------
Draw 'Pancakes' {
    param($g)
    Plate $g
    foreach ($y in @(76, 66, 56, 46)) {
        Ell $g 24 $y 80 24 '#e7a85a' '#b97a32'
        Ell $g 26 ($y - 2) 76 18 '#f4c37a' '#e0a256' $false
    }
    Blob $g @(40, 46, 64, 40, 90, 46, 86, 60, 72, 56, 60, 66, 46, 56) '#9a4f12' '#6a3308'
    Rnd $g 54 32 18 12 2 '#fff2b8' '#f2d27a'
}
Draw 'GrilledCheese' {
    param($g)
    Plate $g
    Poly $g @(22, 84, 92, 34, 104, 88) '#e7b066' '#b77a30'
    Poly $g @(28, 82, 90, 40, 98, 84) '#f3c97e' '#d79b48' $false
    foreach ($x in @(48, 64, 80)) { Line $g '#8a4f1a' 4 $x 74 ($x + 12) 56 200 }
    Poly $g @(26, 86, 102, 90, 100, 96, 30, 94) '#ffc634' '#e8a010'
    Blob $g @(44, 92, 54, 100, 60, 94) '#ffc634' '#e8a010' $false
}
Draw 'SoupAndBread' {
    param($g)
    Shadow $g 10 100 108 16
    Ell $g 8 48 76 24 (Dark '#f4f1ea' 0.8) $null $false
    Ell $g 12 50 68 18 '#e2552f' '#b9381a'
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc([float]8, [float]36, [float]76, [float]62, 0, 180)
    $path.CloseFigure()
    Paint $g $path '#f4f1ea' '#c9c2b4'
    Blob $g @(70, 92, 76, 66, 98, 56, 118, 66, 118, 92) '#d6914c' '#a3602a'
    Arc $g '#f6d39a' 3 82 62 18 14 200 120
    Arc $g '#f6d39a' 3 96 62 18 14 200 120
}
Draw 'Cheeseburger' {
    param($g)
    Shadow $g 14 102 100 16
    Ell $g 18 84 92 20 '#d89a52' '#a86c2c'
    Ell $g 14 72 100 20 '#5d3018' '#3a1c0c'
    Poly $g @(18, 72, 110, 72, 102, 84, 64, 78, 26, 84) '#ffc634' '#e8a010'
    Blob $g @(14, 66, 40, 60, 64, 66, 90, 60, 114, 66, 90, 74, 64, 70, 38, 74) '#7ccf4a' '#4a9a2e'
    Blob $g @(14, 64, 22, 36, 64, 20, 106, 36, 114, 64) '#e9ad5e' '#bb7a34'
    foreach ($e in @(@(40, 36), @(58, 30), @(76, 34), @(64, 44), @(46, 48), @(84, 46))) { Spot $g '#fff6dc' $e[0] $e[1] 6 3 230 }
    Gloss $g 36 30 30 8 90
}
Draw 'Pizza' {
    param($g)
    Shadow $g 8 98 112 18
    Ell $g 6 44 116 58 '#e5b46a' '#b98238'
    Ell $g 14 50 100 46 '#e04a2e' '#b1311a' $false
    foreach ($e in @(@(26, 60, 22, 12), @(54, 54, 24, 12), @(84, 62, 20, 12), @(40, 74, 22, 12), @(70, 76, 24, 12))) { Ell $g $e[0] $e[1] $e[2] $e[3] '#fff8e6' '#ead8b0' $false }
    foreach ($e in @(@(46, 64), @(78, 56), @(60, 84), @(30, 76))) { Spot $g '#3f9a2a' $e[0] $e[1] 10 5 }
}
Draw 'Spaghetti' {
    param($g)
    Plate $g
    Ell $g 24 52 80 40 '#f4d067' '#d6a832'
    foreach ($r in @(@(28, 56, 72, 32), @(34, 60, 60, 24), @(40, 64, 48, 18))) { Arc $g '#c99520' 3 $r[0] $r[1] $r[2] $r[3] 10 320 }
    Blob $g @(38, 60, 52, 44, 78, 44, 92, 58, 74, 70, 50, 70) '#b2442b' '#7a2a17'
    foreach ($e in @(@(50, 52), @(66, 50), @(76, 60), @(58, 60))) { Spot $g '#5e2412' $e[0] $e[1] 6 4 230 }
    Spot $g '#5fae3b' 62 44 8 5
}
Draw 'ChickenAndFries' {
    param($g)
    Plate $g
    foreach ($s in @(@(70, 40), @(80, 36), @(90, 42), @(98, 48))) { Rnd $g $s[0] $s[1] 9 44 3 '#ffd25a' '#e3a12c' }
    Blob $g @(18, 72, 26, 50, 52, 42, 74, 54, 72, 76, 52, 88, 28, 88) '#e3a046' '#a8661c'
    foreach ($e in @(@(30, 56), @(46, 50), @(60, 58), @(38, 70), @(56, 72))) { Spot $g '#f6c470' $e[0] $e[1] 8 6 230 }
    Rnd $g 62 74 22 9 4 '#f6efe6' '#d6cabc'
    Ell $g 80 70 11 10 '#f6efe6' '#d6cabc'
    Ell $g 80 79 11 10 '#f6efe6' '#d6cabc'
}
Draw 'FishAndChips' {
    param($g)
    Plate $g
    foreach ($s in @(@(16, 54), @(26, 50), @(36, 56), @(22, 62), @(32, 66))) { Rnd $g $s[0] $s[1] 9 38 3 '#ffd25a' '#e3a12c' }
    Blob $g @(44, 70, 54, 52, 80, 46, 104, 54, 112, 70, 100, 86, 70, 90, 50, 86) '#e9a948' '#b06e1e'
    foreach ($e in @(@(58, 60), @(76, 54), @(94, 60), @(66, 74), @(88, 76))) { Spot $g '#f8cf7a' $e[0] $e[1] 9 6 230 }
    Blob $g @(96, 82, 104, 74, 114, 80, 108, 90) '#ffe066' '#e8b828'
}
Draw 'Lasagna' {
    param($g)
    Plate $g
    Poly $g @(26, 60, 70, 44, 104, 56, 60, 74) '#f3c45a' '#d89a30'
    Poly $g @(26, 60, 60, 74, 60, 98, 26, 84) '#d27a3a' '#a2521e'
    Poly $g @(60, 74, 104, 56, 104, 80, 60, 98) '#c96a30' '#954318'
    foreach ($y in @(68, 76, 84)) {
        Line $g '#fff1c4' 3 26 $y 60 ($y + 14) 230
        Line $g '#fff1c4' 3 60 ($y + 14) 104 ($y - 4) 230
    }
    foreach ($e in @(@(48, 54), @(70, 50), @(84, 58), @(60, 62))) { Spot $g '#b2442b' $e[0] $e[1] 9 5 }
    Spot $g '#3f9a2a' 64 52 8 4
}

# ---------- Kitchen stations ----------
Draw 'Mixer' {
    param($g)
    Shadow $g 20 104 88 14
    Rnd $g 30 88 68 16 4 '#2a8d90' '#1b6467'
    Rnd $g 72 26 20 66 6 '#2a8d90' '#1b6467'
    Rnd $g 30 18 66 26 10 '#33a3a6' '#1f7275'
    Line $g '#cfd6dc' 4 46 44 46 62
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc([float]18, [float]40, [float]58, [float]60, 0, 180)
    $path.CloseFigure()
    Paint $g $path '#e6eaee' '#9aa4ad'
    Ell $g 18 62 58 14 '#f4ead2' '#e3d2ac'
    Gloss $g 24 74 10 14 140
    Gloss $g 38 22 30 6 120
}
Draw 'Stove' {
    param($g)
    Shadow $g 12 104 104 14
    Rnd $g 12 54 104 52 6 '#c9d0d6' '#8b959e'
    Rnd $g 20 70 88 30 4 '#3a4148' '#22272c'
    Rnd $g 30 76 68 16 3 '#5a6570' '#3a4148'
    Ell $g 10 40 108 24 '#3a4148' '#22272c'
    Spot $g '#ff8a2a' 26 44 30 10 230
    Spot $g '#ff8a2a' 72 44 30 10 230
    Rnd $g 26 18 32 30 4 '#e04a2e' '#a8301a'
    Rnd $g 22 16 40 8 3 '#cfd6dc' '#9aa4ad'
    foreach ($x in @(24, 44, 64, 84, 100)) { Spot $g '#f07a2a' $x 58 8 8 }
}
Draw 'Fryer' {
    param($g)
    Shadow $g 18 104 92 14
    Rnd $g 18 40 92 66 6 '#c9d0d6' '#8b959e'
    Rnd $g 24 44 38 20 3 '#f2b62e' '#c98a10'
    Rnd $g 66 44 38 20 3 '#f2b62e' '#c98a10'
    Rnd $g 70 18 30 30 3 '#3a4148' '#22272c'
    Rnd $g 74 24 22 18 2 '#f2c160' '#c98a10' $false
    Line $g '#22272c' 4 85 48 85 60
    Rnd $g 26 72 76 26 3 '#3a4148' '#22272c'
    foreach ($x in @(34, 54, 74)) { Spot $g '#f07a2a' $x 80 8 8 }
}
Draw 'Grill' {
    param($g)
    Shadow $g 10 104 108 14
    Rnd $g 10 64 108 42 5 '#3a4148' '#22272c'
    Rnd $g 14 26 100 18 4 '#5a6570' '#3a4148'
    Poly $g @(10, 66, 22, 44, 106, 44, 118, 66) '#26292d' '#141618'
    foreach ($e in @(@(28, 50), @(54, 48), @(80, 50))) { Ell $g $e[0] $e[1] 22 10 '#7a4426' '#4e2814' }
    Spot $g '#ff8a2a' 14 62 100 4 200
    foreach ($x in @(24, 46, 68, 90)) { Spot $g '#f07a2a' $x 80 10 10 }
}
Draw 'KitchenCounter' {
    param($g)
    Shadow $g 8 104 112 14
    Rnd $g 10 62 108 44 4 '#1f8a8c' '#136163'
    Line $g '#0f4d4f' 3 46 64 46 104
    Line $g '#0f4d4f' 3 82 64 82 104
    Poly $g @(6, 64, 18, 44, 110, 44, 122, 64) '#b27a48' '#7a4c26'
    Poly $g @(24, 58, 32, 48, 66, 48, 60, 58) '#e8c890' '#c7a46a'
    Line $g '#cfd6dc' 3 38 54 54 52
    Ell $g 70 44 18 12 '#e04a2e' '#a8301a'
    Ell $g 88 46 20 12 '#7ccf4a' '#4a9a2e'
}
Write-Output "Drew food and kitchen icons in $out"
