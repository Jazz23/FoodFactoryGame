# Draws the DEVELOPMENT 2D item icons (dough, bread, oven, belt, lift, counter, fridge, dock, table) as 128x128 transparent PNGs with GDI+.
# Re-run to regenerate: powershell -ExecutionPolicy Bypass -File AgentScripts/DrawItemIcons.ps1
# Unity imports them as sprites (AgentScripts/BuildDevSite.cs sets the import settings).
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\Assets\Art\Icons'
New-Item -ItemType Directory -Force $out | Out-Null

function Color($hex, $alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
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
    $brush = New-Object System.Drawing.SolidBrush (Color '#000000' 70)
    $g.FillEllipse($brush, $x, $y, $w, $h)
}

function Blob($g, [float[]]$points) {
    # Closed smooth curve through the given x,y pairs.
    $pts = for ($i = 0; $i -lt $points.Length; $i += 2) { New-Object System.Drawing.PointF $points[$i], $points[$i + 1] }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddClosedCurve([System.Drawing.PointF[]]$pts, 0.55)
    return $path
}

$outline = New-Object System.Drawing.Pen (Color '#3a2414'), 4
$outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

# --- Dough: a soft, pale ball of dough with a floury top ---
$bitmap, $g = New-Icon
Shadow $g 18 92 92 22
$dough = Blob $g @(20, 84, 26, 56, 48, 38, 78, 36, 102, 50, 110, 78, 96, 98, 64, 102, 34, 98)
$rect = New-Object System.Drawing.RectangleF 16, 32, 98, 74
$fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Color '#fff4dc'), (Color '#e0bf88'), 90
$g.FillPath($fill, $dough)
$g.DrawPath($outline, $dough)
$flour = New-Object System.Drawing.SolidBrush (Color '#ffffff' 200)
$g.FillEllipse($flour, 44, 44, 30, 12)
$g.FillEllipse($flour, 78, 50, 12, 6)
$fold = New-Object System.Drawing.Pen (Color '#c79a5c'), 3
$fold.StartCap = $fold.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($fold, 36, 66, 36, 22, 20, 120)
$g.DrawArc($fold, 66, 72, 30, 18, 30, 110)
Save-Icon $bitmap $g 'Dough'

# --- Bread: a golden loaf with three scores ---
$bitmap, $g = New-Icon
Shadow $g 12 94 104 20
$loaf = Blob $g @(12, 82, 18, 54, 44, 34, 84, 34, 110, 52, 116, 82, 100, 100, 28, 100)
$rect = New-Object System.Drawing.RectangleF 10, 30, 108, 74
$fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Color '#f2b85a'), (Color '#9c5418'), 90
$g.FillPath($fill, $loaf)
$g.DrawPath($outline, $loaf)
$shine = New-Object System.Drawing.SolidBrush (Color '#ffe2a0' 150)
$g.FillEllipse($shine, 34, 40, 50, 14)
$score = New-Object System.Drawing.Pen (Color '#fbe3ad'), 5
$score.StartCap = $score.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($score, 34, 72, 46, 52)
$g.DrawLine($score, 56, 76, 68, 52)
$g.DrawLine($score, 78, 76, 90, 54)
$crust = New-Object System.Drawing.Pen (Color '#7a3f10'), 2
$g.DrawArc($crust, 20, 70, 88, 30, 20, 140)
Save-Icon $bitmap $g 'Bread'

# --- Oven: cream cabinet, teal trim, glowing glass door with bread on racks ---
$bitmap, $g = New-Icon
Shadow $g 14 110 100 14
$dark = New-Object System.Drawing.Pen (Color '#1f2a2e'), 4
$dark.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$teal = New-Object System.Drawing.SolidBrush (Color '#2f7f86')
$g.FillRectangle($teal, 30, 106, 8, 8)
$g.FillRectangle($teal, 90, 106, 8, 8)
$body = New-Object System.Drawing.RectangleF 18, 22, 92, 86
$fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $body, (Color '#f4efe0'), (Color '#c9c1a8'), 0
$g.FillRectangle($fill, $body)
$g.FillRectangle($teal, 18, 96, 92, 12)
$g.FillRectangle($teal, 14, 16, 100, 10)
$g.DrawRectangle($dark, 18, 22, 92, 86)
$g.DrawRectangle($dark, 14, 16, 100, 10)
$g.FillRectangle($teal, 58, 6, 12, 10)
$g.DrawRectangle($dark, 58, 6, 12, 10)
$glassRect = New-Object System.Drawing.RectangleF 30, 32, 58, 58
$glass = New-Object System.Drawing.Drawing2D.LinearGradientBrush $glassRect, (Color '#3a1c14'), (Color '#d8491c'), 90
$g.FillRectangle($glass, $glassRect)
$rack = New-Object System.Drawing.Pen (Color '#9aa3a6'), 2
$loafBrush = New-Object System.Drawing.SolidBrush (Color '#d98f3a')
foreach ($y in 46, 64, 82) {
    $g.FillEllipse($loafBrush, 36, $y - 9, 20, 9)
    $g.FillEllipse($loafBrush, 60, $y - 9, 20, 9)
    $g.DrawLine($rack, 32, $y, 86, $y)
}
$g.DrawRectangle($dark, 30, 32, 58, 58)
$glare = New-Object System.Drawing.SolidBrush (Color '#ffffff' 60)
$g.FillPolygon($glare, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 34, 88), (New-Object System.Drawing.PointF 60, 36),
    (New-Object System.Drawing.PointF 70, 36), (New-Object System.Drawing.PointF 44, 88)))
$handle = New-Object System.Drawing.SolidBrush (Color '#1f2a2e')
$g.FillRectangle($handle, 94, 44, 6, 32)
$lamp = New-Object System.Drawing.SolidBrush (Color '#ffcc33')
$g.FillEllipse($lamp, 94, 84, 7, 7)
Save-Icon $bitmap $g 'Oven'

# --- Belt: a straight conveyor seen from above, dark tread between amber rails, chevrons pointing along travel ---
$bitmap, $g = New-Icon
Shadow $g 20 108 88 14
$frame = New-Object System.Drawing.SolidBrush (Color '#3b3f44')
$g.FillRectangle($frame, 26, 10, 76, 104)
$treadRect = New-Object System.Drawing.RectangleF 36, 10, 56, 104
$tread = New-Object System.Drawing.Drawing2D.LinearGradientBrush $treadRect, (Color '#2a2b2e'), (Color '#4a4c50'), 0
$g.FillRectangle($tread, $treadRect)
$amber = New-Object System.Drawing.SolidBrush (Color '#f0a818')
$g.FillRectangle($amber, 26, 10, 10, 104)
$g.FillRectangle($amber, 92, 10, 10, 104)
$chevron = New-Object System.Drawing.Pen (Color '#f6d25a'), 7
$chevron.StartCap = $chevron.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$chevron.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
foreach ($y in 34, 64, 94) {
    $g.DrawLines($chevron, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 48, $y), (New-Object System.Drawing.PointF 64, ($y - 14)),
        (New-Object System.Drawing.PointF 80, $y)))
}
$g.DrawRectangle($dark, 26, 10, 76, 104)
Save-Icon $bitmap $g 'Belt'

# --- Lift: a conveyor tower rising through two floor slabs, with a double arrow for carrying goods up or down (decision 0021) ---
$bitmap, $g = New-Icon
Shadow $g 20 108 88 14
$slab = New-Object System.Drawing.SolidBrush (Color '#9aa3ab')
$g.FillRectangle($slab, 6, 30, 116, 9)
$g.FillRectangle($slab, 6, 94, 116, 9)
$frame = New-Object System.Drawing.SolidBrush (Color '#3b3f44')
$g.FillRectangle($frame, 38, 8, 52, 106)
$treadRect = New-Object System.Drawing.RectangleF 46, 8, 36, 106
$tread = New-Object System.Drawing.Drawing2D.LinearGradientBrush $treadRect, (Color '#2a2b2e'), (Color '#4a4c50'), 0
$g.FillRectangle($tread, $treadRect)
$amber = New-Object System.Drawing.SolidBrush (Color '#f0a818')
$g.FillRectangle($amber, 38, 8, 8, 106)
$g.FillRectangle($amber, 82, 8, 8, 106)
$arrow = New-Object System.Drawing.Pen (Color '#f6d25a'), 7
$arrow.StartCap = $arrow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$arrow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawLine($arrow, 64, 24, 64, 98)
$g.DrawLines($arrow, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 52, 36), (New-Object System.Drawing.PointF 64, 22), (New-Object System.Drawing.PointF 76, 36)))
$g.DrawLines($arrow, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 52, 86), (New-Object System.Drawing.PointF 64, 100), (New-Object System.Drawing.PointF 76, 86)))
$outline = New-Object System.Drawing.Pen (Color '#1f2a2e'), 4
$g.DrawRectangle($outline, 38, 8, 52, 106)
Save-Icon $bitmap $g 'Lift'

# --- Counter: a wooden shop counter with a teal cash register and a gold coin (decision 0013 sell counter) ---
$bitmap, $g = New-Icon
Shadow $g 8 108 112 14
$dark = New-Object System.Drawing.Pen (Color '#1f2a2e'), 4
$dark.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$front = New-Object System.Drawing.RectangleF 12, 62, 104, 50
$wood = New-Object System.Drawing.Drawing2D.LinearGradientBrush $front, (Color '#b8743a'), (Color '#7a4a22'), 90
$g.FillRectangle($wood, $front)
$plank = New-Object System.Drawing.Pen (Color '#5e3818'), 2
foreach ($x in 38, 64, 90) { $g.DrawLine($plank, $x, 64, $x, 110) }
$g.DrawRectangle($dark, 12, 62, 104, 50)
$top = New-Object System.Drawing.SolidBrush (Color '#f4efe0')
$g.FillRectangle($top, 6, 54, 116, 10)
$g.DrawRectangle($dark, 6, 54, 116, 10)
$teal = New-Object System.Drawing.SolidBrush (Color '#2f7f86')
$g.FillRectangle($teal, 58, 26, 46, 28)
$g.DrawRectangle($dark, 58, 26, 46, 28)
$screen = New-Object System.Drawing.SolidBrush (Color '#9be7c4')
$g.FillRectangle($screen, 66, 14, 30, 12)
$g.DrawRectangle($dark, 66, 14, 30, 12)
$key = New-Object System.Drawing.SolidBrush (Color '#e8e2cf')
foreach ($row in 0, 1) { foreach ($col in 0, 1, 2) { $g.FillRectangle($key, 64 + $col * 12, 32 + $row * 10, 8, 6) } }
$gold = New-Object System.Drawing.SolidBrush (Color '#f2c230')
$g.FillEllipse($gold, 18, 26, 28, 28)
$g.DrawEllipse($dark, 18, 26, 28, 28)
$mark = New-Object System.Drawing.Pen (Color '#8a6a10'), 3
$g.DrawLine($mark, 32, 32, 32, 48)
$g.DrawArc($mark, 26, 32, 12, 8, 90, 180)
$g.DrawArc($mark, 26, 40, 12, 8, 270, 180)
Save-Icon $bitmap $g 'Counter'

# --- Fridge: a tall white two-door fridge with steel handles and an ice-blue snowflake badge (decision 0018) ---
$bitmap, $g = New-Icon
Shadow $g 24 110 80 14
$dark = New-Object System.Drawing.Pen (Color '#1f2a2e'), 4
$dark.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$body = New-Object System.Drawing.RectangleF 30, 8, 68, 106
$fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $body, (Color '#ffffff'), (Color '#c7d6de'), 0
$g.FillRectangle($fill, $body)
$g.DrawRectangle($dark, 30, 8, 68, 106)
$g.DrawLine($dark, 30, 44, 98, 44)
$steel = New-Object System.Drawing.SolidBrush (Color '#7d8b92')
$g.FillRectangle($steel, 86, 18, 6, 18)
$g.FillRectangle($steel, 86, 54, 6, 30)
$feet = New-Object System.Drawing.SolidBrush (Color '#1f2a2e')
$g.FillRectangle($feet, 36, 114, 8, 4)
$g.FillRectangle($feet, 84, 114, 8, 4)
$ice = New-Object System.Drawing.SolidBrush (Color '#5bc0eb')
$g.FillEllipse($ice, 38, 62, 36, 36)
$g.DrawEllipse($dark, 38, 62, 36, 36)
$flake = New-Object System.Drawing.Pen (Color '#ffffff'), 3
$flake.StartCap = $flake.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
foreach ($angle in 0, 60, 120) {
    $rad = $angle * [Math]::PI / 180
    $dx = [Math]::Cos($rad) * 12
    $dy = [Math]::Sin($rad) * 12
    $g.DrawLine($flake, 56 - $dx, 80 - $dy, 56 + $dx, 80 + $dy)
}
Save-Icon $bitmap $g 'Fridge'

# --- Dock: a loading dock with a roll-up door, a yellow-and-black bumper edge and a crate waiting to ship (decision 0022) ---
$bitmap, $g = New-Icon
Shadow $g 8 108 112 14
$dark = New-Object System.Drawing.Pen (Color '#1f2a2e'), 4
$dark.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$wall = New-Object System.Drawing.RectangleF 10, 12, 108, 70
$concrete = New-Object System.Drawing.Drawing2D.LinearGradientBrush $wall, (Color '#b9bec2'), (Color '#8c9296'), 90
$g.FillRectangle($concrete, $wall)
$g.DrawRectangle($dark, 10, 12, 108, 70)
$door = New-Object System.Drawing.SolidBrush (Color '#5d7f99')
$g.FillRectangle($door, 26, 24, 76, 58)
$slat = New-Object System.Drawing.Pen (Color '#3f5a70'), 2
foreach ($y in 32, 40, 48, 56, 64, 72) { $g.DrawLine($slat, 26, $y, 102, $y) }
$g.DrawRectangle($dark, 26, 24, 76, 58)
$deck = New-Object System.Drawing.SolidBrush (Color '#6f7478')
$g.FillRectangle($deck, 4, 82, 120, 26)
$g.DrawRectangle($dark, 4, 82, 120, 26)
$yellow = New-Object System.Drawing.SolidBrush (Color '#f2c230')
$black = New-Object System.Drawing.SolidBrush (Color '#1f2a2e')
for ($x = 6; $x -lt 122; $x += 16) {
    $g.FillRectangle($yellow, $x, 100, 8, 7)
    $g.FillRectangle($black, $x + 8, 100, 8, 7)
}
$crate = New-Object System.Drawing.SolidBrush (Color '#c28a4a')
$g.FillRectangle($crate, 72, 58, 28, 26)
$g.DrawRectangle($dark, 72, 58, 28, 26)
$brace = New-Object System.Drawing.Pen (Color '#7a4a22'), 3
$g.DrawLine($brace, 72, 58, 100, 84)
Save-Icon $bitmap $g 'Dock'

# --- Table: a square wooden dining table seen at an angle, with a chair each side and a plate (decision 0024) ---
$bitmap, $g = New-Icon
Shadow $g 10 106 108 16
$dark = New-Object System.Drawing.Pen (Color '#1f2a2e'), 4
$dark.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$chair = New-Object System.Drawing.SolidBrush (Color '#8a5a2b')
foreach ($x in 6, 98) {
    $g.FillRectangle($chair, $x, 40, 24, 10)
    $g.DrawRectangle($dark, $x, 40, 24, 10)
    $g.FillRectangle($chair, $x, 70, 24, 8)
    $g.DrawRectangle($dark, $x, 70, 24, 8)
    $g.FillRectangle($chair, $x + 2, 78, 5, 30)
    $g.FillRectangle($chair, $x + 17, 78, 5, 30)
}
$legs = New-Object System.Drawing.SolidBrush (Color '#6b4220')
$g.FillRectangle($legs, 34, 64, 8, 46)
$g.FillRectangle($legs, 86, 64, 8, 46)
$g.DrawRectangle($dark, 34, 64, 8, 46)
$g.DrawRectangle($dark, 86, 64, 8, 46)
$top = New-Object System.Drawing.RectangleF 22, 50, 84, 16
$wood = New-Object System.Drawing.Drawing2D.LinearGradientBrush $top, (Color '#d99a55'), (Color '#a8672d'), 90
$g.FillRectangle($wood, $top)
$g.DrawRectangle($dark, 22, 50, 84, 16)
$plate = New-Object System.Drawing.SolidBrush (Color '#f4f1ea')
$g.FillEllipse($plate, 48, 40, 32, 12)
$g.DrawEllipse($dark, 48, 40, 32, 12)
Save-Icon $bitmap $g 'Table'
"Icons written to $out"
