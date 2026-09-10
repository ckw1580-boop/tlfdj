# User-authorized vector redraw of the reference on a transparent canvas.
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(1094, 800, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bitmap)
$pen = [Drawing.Pen]::new([Drawing.Color]::Black, 4)
$font = [Drawing.Font]::new('Arial', 30, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$name = [Drawing.Font]::new('Arial', 44, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$format = [Drawing.StringFormat]::new()
$format.Alignment = [Drawing.StringAlignment]::Center
try {
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform(2, 2)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    foreach ($label in @(@('95',44,76),@('97',122,76),@('96',44,288),@('98',122,288),
        @('1L1',281,76),@('3L2',383,76),@('5L3',485,76),@('2T1',281,288),@('4T2',383,288),@('6T3',485,288))) {
        $g.DrawString($label[0], $font, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($label[1]-40,$label[2],80,38),$format)
    }
    $g.DrawString('FR',$name,[Drawing.Brushes]::Black,[single]185,[single]108)
    $g.DrawLine($pen,44,122,44,176); $g.DrawLine($pen,44,176,74,176)
    $g.DrawLine($pen,44,282,44,228); $g.DrawLine($pen,44,228,70,169)
    $g.DrawLine($pen,122,122,122,176)
    $g.DrawLine($pen,122,282,122,228); $g.DrawLine($pen,122,228,89,169)
    $g.DrawRectangle($pen,222,181,306,51)
    $g.DrawLine($pen,324,181,324,232); $g.DrawLine($pen,426,181,426,232)
    foreach ($x in @(273,375,477)) {
        $g.DrawLine($pen,$x,130,$x,194); $g.DrawLine($pen,$x,194,$x+26,194)
        $g.DrawLine($pen,$x+26,194,$x+26,219); $g.DrawLine($pen,$x+26,219,$x,219)
        $g.DrawLine($pen,$x,219,$x,282)
    }
    $bitmap.Save((Join-Path $PSScriptRoot '../Assets/ElectricalSim/Resources/ThermalRelaySchematic.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally { $format.Dispose(); $name.Dispose(); $font.Dispose(); $pen.Dispose(); $g.Dispose(); $bitmap.Dispose() }
