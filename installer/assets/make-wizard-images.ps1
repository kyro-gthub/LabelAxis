<#
.SYNOPSIS
    Draws Setup's wizard images (installer\assets\wizard-*) for Inno Setup 6.7 or later.
.DESCRIPTION
    The side picture of the Welcome and Finished pages, in a light and a dark version (Setup
    follows Windows' light or dark mode, WizardStyle=modern dynamic), and the LabelAxis mark for
    the top corner of the other pages. Everything is drawn as shapes at the size Setup shows it
    at each display scale, so nothing is stretched. Run it with Windows PowerShell (WPF):
        powershell -NoProfile -ExecutionPolicy Bypass -File installer\assets\make-wizard-images.ps1
    The only words in the pictures are the name LabelAxis: Setup runs in six languages.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$here = $PSScriptRoot
$app = Join-Path $here '..\..\src\LabelMaker.App\Assets'
$fonts = ([Uri](Join-Path (Resolve-Path (Join-Path $app 'Fonts')) '\')).AbsoluteUri
$logo = ([Uri](Resolve-Path (Join-Path $app 'logo.png')).Path).AbsoluteUri
$inv = [Globalization.CultureInfo]::InvariantCulture

# Side picture, drawn on 202 x 386 (its size at 100%); Setup keeps the 164:314 shape.
$W = 202; $H = 386

$themes = @{
    light = @{
        Top = '#FF3F8AF4'; Bottom = '#FF1543B5'; Glow = '#55FFFFFF'; Tick = '#38FFFFFF'
        Sheet = '#FFFFFFFF'; Label = '#FFFFFFFF'; Edge = '#FFD3DCEA'; Ink = '#FF1B2433'; Soft = '#FFB9C4D6'
        Accent = '#FF2563EB'; Shadow = '#FF0A2A6B'; Wordmark = '#FFFFFFFF'; Tile = '#30FFFFFF'; TileEdge = '#66FFFFFF'
    }
    dark = @{
        Top = '#FF16264A'; Bottom = '#FF0A1022'; Glow = '#663D7BFF'; Tick = '#26FFFFFF'
        Sheet = '#FFE8EDF5'; Label = '#FFF6F8FC'; Edge = '#FFC9D3E3'; Ink = '#FF1B2433'; Soft = '#FFAFBACC'
        Accent = '#FF3B82F6'; Shadow = '#FF000000'; Wordmark = '#FFF2F6FF'; Tile = '#14FFFFFF'; TileEdge = '#29FFFFFF'
    }
}

function N([double]$v) { $v.ToString('0.###', $inv) }

# A fixed QR-like pattern (finder squares in three corners), the same every run.
function Qr([double]$x, [double]$y, [double]$cell, [string]$ink, [int]$seed) {
    $rnd = New-Object System.Random $seed
    $sb = New-Object System.Text.StringBuilder
    $n = 21
    for ($r = 0; $r -lt $n; $r++) {
        for ($c = 0; $c -lt $n; $c++) {
            $finder = ($r -lt 7 -and $c -lt 7) -or ($r -lt 7 -and $c -ge $n - 7) -or ($r -ge $n - 7 -and $c -lt 7)
            if ($finder) { continue }
            if ($rnd.NextDouble() -lt 0.48) {
                [void]$sb.Append("M$(N ($x + $c * $cell)),$(N ($y + $r * $cell))h$(N $cell)v$(N $cell)h-$(N $cell)z ")
            }
        }
    }
    foreach ($corner in @(@(0, 0), @(0, ($n - 7)), @(($n - 7), 0))) {
        $fx = $x + $corner[1] * $cell; $fy = $y + $corner[0] * $cell
        [void]$sb.Append("M$(N $fx),$(N $fy)h$(N (7*$cell))v$(N (7*$cell))h-$(N (7*$cell))z M$(N ($fx+$cell)),$(N ($fy+$cell))v$(N (5*$cell))h$(N (5*$cell))v-$(N (5*$cell))z ")
        [void]$sb.Append("M$(N ($fx+2*$cell)),$(N ($fy+2*$cell))h$(N (3*$cell))v$(N (3*$cell))h-$(N (3*$cell))z ")
    }
    "<Path Fill='$ink' Data='F0 $($sb.ToString())'/>"
}

# Bars of a barcode: widths from a fixed sequence, filling the given width.
function Bars([double]$x, [double]$y, [double]$w, [double]$h, [string]$ink, [int]$seed) {
    $rnd = New-Object System.Random $seed
    $runs = @(); $total = 0
    while ($total -lt 90) { $k = $rnd.Next(1, 4); $runs += $k; $total += $k }
    $unit = $w / $total
    $sb = New-Object System.Text.StringBuilder
    $at = 0; $bar = $true
    foreach ($k in $runs) {
        if ($bar) { [void]$sb.Append("M$(N ($x + $at * $unit)),$(N $y)h$(N ($k * $unit))v$(N $h)h-$(N ($k * $unit))z ") }
        $at += $k; $bar = -not $bar
    }
    "<Path Fill='$ink' Data='F0 $($sb.ToString())'/>"
}

function Line([double]$x, [double]$y, [double]$w, [double]$h, [string]$fill) {
    "<Rectangle Canvas.Left='$(N $x)' Canvas.Top='$(N $y)' Width='$(N $w)' Height='$(N $h)' RadiusX='$(N ($h/2))' RadiusY='$(N ($h/2))' Fill='$fill'/>"
}

function SidePicture($t) {
    $x = New-Object System.Text.StringBuilder
    [void]$x.Append("<Canvas xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='$W' Height='$H' ClipToBounds='True'>")
    [void]$x.Append("<Rectangle Width='$W' Height='$H'><Rectangle.Fill><LinearGradientBrush StartPoint='0.2,0' EndPoint='0.8,1'><GradientStop Color='$($t.Top)' Offset='0'/><GradientStop Color='$($t.Bottom)' Offset='1'/></LinearGradientBrush></Rectangle.Fill></Rectangle>")
    [void]$x.Append("<Ellipse Canvas.Left='-39' Canvas.Top='-30' Width='280' Height='260'><Ellipse.Fill><RadialGradientBrush><GradientStop Color='$($t.Glow)' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/></RadialGradientBrush></Ellipse.Fill></Ellipse>")

    # Rulers along the left and top edges: the "axis" of LabelAxis.
    $ticks = New-Object System.Text.StringBuilder
    for ($i = 1; $i * 6 -lt $H; $i++) { $len = if ($i % 5 -eq 0) { 9 } else { 4 }; [void]$ticks.Append("M0,$(N ($i*6))h$len ") }
    for ($i = 1; $i * 6 -lt $W; $i++) { $len = if ($i % 5 -eq 0) { 9 } else { 4 }; [void]$ticks.Append("M$(N ($i*6)),0v$len ") }
    [void]$x.Append("<Path Stroke='$($t.Tick)' StrokeThickness='0.7' Data='$($ticks.ToString())'/>")

    # The mark on a frosted tile, and the name.
    [void]$x.Append("<Rectangle Canvas.Left='49' Canvas.Top='34' Width='104' Height='104' RadiusX='28' RadiusY='28' Fill='$($t.Tile)' Stroke='$($t.TileEdge)' StrokeThickness='0.8'/>")
    [void]$x.Append("<Image Source='$logo' Canvas.Left='59' Canvas.Top='44' Width='84' Height='84' RenderOptions.BitmapScalingMode='HighQuality'><Image.Effect><DropShadowEffect Color='$($t.Shadow)' BlurRadius='22' ShadowDepth='7' Direction='270' Opacity='0.35'/></Image.Effect></Image>")
    [void]$x.Append("<TextBlock Canvas.Left='0' Canvas.Top='145' Width='$W' TextAlignment='Center' Text='LabelAxis' FontFamily='${fonts}#Plus Jakarta Sans' FontWeight='ExtraBold' FontSize='28' Foreground='$($t.Wordmark)'/>")
    [void]$x.Append("<Rectangle Canvas.Left='87' Canvas.Top='190' Width='28' Height='2.4' RadiusX='1.2' RadiusY='1.2' Fill='$($t.Wordmark)' Opacity='0.55'/>")

    # A sheet of labels being designed, tilted and running off the bottom.
    $sw = 156; $sh = 220
    [void]$x.Append("<Canvas Canvas.Left='23' Canvas.Top='214' Width='$sw' Height='$sh'><Canvas.RenderTransform><RotateTransform Angle='-7' CenterX='78' CenterY='110'/></Canvas.RenderTransform>")
    [void]$x.Append("<Rectangle Width='$sw' Height='$sh' RadiusX='6' RadiusY='6' Fill='$($t.Sheet)'><Rectangle.Effect><DropShadowEffect Color='$($t.Shadow)' BlurRadius='26' ShadowDepth='8' Direction='270' Opacity='0.4'/></Rectangle.Effect></Rectangle>")
    $lw = 66; $lh = 36; $mx = 9; $my = 11; $gap = 6; $pitch = $lh + 6
    $selected = $null
    for ($r = 0; $r -lt 5; $r++) {
        for ($c = 0; $c -lt 2; $c++) {
            $lx = $mx + $c * ($lw + $gap); $ly = $my + $r * $pitch
            [void]$x.Append("<Rectangle Canvas.Left='$lx' Canvas.Top='$ly' Width='$lw' Height='$lh' RadiusX='3.5' RadiusY='3.5' Fill='$($t.Label)' Stroke='$($t.Edge)' StrokeThickness='0.7'/>")
            $kind = ($r * 2 + $c) % 4
            switch ($kind) {
                0 { [void]$x.Append((Line ($lx+7) ($ly+6) 30 3.4 $t.Ink)); [void]$x.Append((Bars ($lx+7) ($ly+12) 52 15 $t.Ink (11 + $r))); [void]$x.Append((Line ($lx+17) ($ly+29.5) 32 2.2 $t.Soft)) }
                1 { [void]$x.Append((Qr ($lx+6) ($ly+6) 1.15 $t.Ink (5 + $r))); [void]$x.Append((Line ($lx+35) ($ly+8) 24 3.4 $t.Ink)); [void]$x.Append((Line ($lx+35) ($ly+15) 20 2.2 $t.Soft)); [void]$x.Append((Line ($lx+35) ($ly+20.5) 23 2.2 $t.Soft)); [void]$x.Append((Line ($lx+35) ($ly+26) 14 2.2 $t.Soft)) }
                2 { [void]$x.Append((Line ($lx+7) ($ly+7) 40 4.2 $t.Ink)); [void]$x.Append((Line ($lx+7) ($ly+15) 50 2.2 $t.Soft)); [void]$x.Append((Line ($lx+7) ($ly+20.5) 44 2.2 $t.Soft)); [void]$x.Append((Line ($lx+7) ($ly+27) 22 4 $t.Accent)) }
                3 { [void]$x.Append((Line ($lx+7) ($ly+6) 22 3.4 $t.Ink)); [void]$x.Append((Line ($lx+40) ($ly+5) 19 6 $t.Accent)); [void]$x.Append((Bars ($lx+7) ($ly+14) 52 12 $t.Ink (23 + $r))); [void]$x.Append((Line ($lx+19) ($ly+29) 28 2.2 $t.Soft)) }
            }
            if ($r -eq 1 -and $c -eq 1) { $selected = @($lx, $ly) }
        }
    }

    # One label selected, as on the canvas: guides through its corner, the axis mark, handles.
    $sx = $selected[0]; $sy = $selected[1]
    [void]$x.Append("<Path Stroke='$($t.Accent)' StrokeThickness='0.7' Opacity='0.55' StrokeDashArray='3 2.5' Data='M$(N ($sx-0.5)),0V$sh M0,$(N ($sy-0.5))H$sw'/>")
    [void]$x.Append("<Rectangle Canvas.Left='$(N ($sx-0.5))' Canvas.Top='$(N ($sy-0.5))' Width='$($lw+1)' Height='$($lh+1)' Stroke='$($t.Accent)' StrokeThickness='1.2'/>")
    foreach ($hx in @(0, 0.5, 1)) { foreach ($hy in @(0, 0.5, 1)) {
        if ($hx -eq 0.5 -and $hy -eq 0.5) { continue }
        if ($hx -eq 0 -and $hy -eq 0) { continue }
        [void]$x.Append("<Rectangle Canvas.Left='$(N ($sx - 2.2 + $hx * $lw))' Canvas.Top='$(N ($sy - 2.2 + $hy * $lh))' Width='4.4' Height='4.4' Fill='White' Stroke='$($t.Accent)' StrokeThickness='1'/>")
    } }
    $cx = $sx - 0.5; $cy = $sy - 0.5
    [void]$x.Append("<Ellipse Canvas.Left='$(N ($cx-6.5))' Canvas.Top='$(N ($cy-6.5))' Width='13' Height='13' Fill='White' Stroke='$($t.Accent)' StrokeThickness='1.8'/>")
    [void]$x.Append("<Path Stroke='$($t.Accent)' StrokeThickness='1.8' Data='M$(N ($cx-3.4)),$(N $cy)h6.8 M$(N $cx),$(N ($cy-3.4))v6.8'/>")
    [void]$x.Append("</Canvas>")

    [void]$x.Append("</Canvas>")
    $x.ToString()
}

# Opaque pictures are saved as 24-bit BMP, which Setup's compression packs far smaller than
# PNG; the mark keeps its see-through corners as PNG.
function Save([System.Windows.UIElement]$element, [int]$pw, [int]$ph, [string]$path) {
    $box = New-Object System.Windows.Controls.Viewbox
    $box.Stretch = 'Fill'
    $box.Width = $pw; $box.Height = $ph
    $box.Child = $element
    $box.Measure((New-Object System.Windows.Size $pw, $ph))
    $box.Arrange((New-Object System.Windows.Rect 0, 0, $pw, $ph))
    $box.UpdateLayout()
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, 'ApplicationIdle')
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $pw, $ph, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($box)
    if ($path.EndsWith('.bmp')) {
        $enc = New-Object System.Windows.Media.Imaging.BmpBitmapEncoder
        $opaque = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bmp, ([System.Windows.Media.PixelFormats]::Bgr24), $null, 0
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($opaque))
    } else {
        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    }
    $fs = [System.IO.File]::Create($path)
    try { $enc.Save($fs) } finally { $fs.Close() }
    $box.Child = $null
    "  $([System.IO.Path]::GetFileName($path))  ${pw}x${ph}"
}

# Sizes Setup shows them at 100%, 150% and 200% display scale (from Inno Setup's help); Setup
# picks the nearest and scales it for the settings in between.
$large = @(@(202, 386), @(336, 643), @(430, 824))
$small = @(58, 97, 124)

foreach ($name in 'light', 'dark') {
    $xaml = SidePicture $themes[$name]
    foreach ($s in $large) {
        $element = [System.Windows.Markup.XamlReader]::Parse($xaml)
        Save $element $s[0] $s[1] (Join-Path $here "wizard-$name-$($s[0]).bmp")
    }
}

foreach ($px in $small) {
    $mark = [System.Windows.Markup.XamlReader]::Parse("<Image xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Source='$logo' Width='256' Height='256' RenderOptions.BitmapScalingMode='HighQuality'/>")
    Save $mark $px $px (Join-Path $here "wizard-mark-$px.png")
}
