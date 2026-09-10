# Reproduce the reference's black relay symbols on a true transparent PNG.
# Run from any directory; System.Drawing is available on Windows.
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '../Assets/ElectricalSim/Resources/RelaySchematic.png'
$bitmap = [Drawing.Bitmap]::new(1080, 834, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$pen = [Drawing.Pen]::new([Drawing.Color]::Black, 3.3)
$numbers = [Drawing.Font]::new('Arial', 24, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$name = [Drawing.Font]::new('Arial', 46, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$format = [Drawing.StringFormat]::new()
$format.Alignment = [Drawing.StringAlignment]::Center
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform(2, 2)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round

    # Four isolated changeover contacts: common 9..12, NC 1..4, NO 5..8.
    for ($group = 0; $group -lt 4; $group++) {
        [single]$x = 42 + 84 * $group
        $graphics.DrawLine($pen, $x + 6, 112, $x + 45, 112)
        $graphics.DrawLine($pen, $x + 45, 112, $x + 45, 215)
        $graphics.DrawLine($pen, $x + 45, 215, $x + 17, 215)
        $graphics.DrawLine($pen, $x, 181, $x, 215)
        $graphics.DrawLine($pen, $x, 293, $x, 272)
        $graphics.DrawLine($pen, $x, 272, $x + 23, 211)
        foreach ($y in @(112, 175, 299)) {
            $graphics.DrawEllipse($pen, $x - 6, $y - 6, 12, 12)
        }
        $graphics.DrawString([string]($group + 1), $numbers, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($x - 20, 79, 40, 26), $format)
        $graphics.DrawString([string]($group + 5), $numbers, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($x - 20, 142, 40, 26), $format)
        $graphics.DrawString([string]($group + 9), $numbers, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($x - 20, 304, 40, 26), $format)
    }

    # Separate coil, terminals 13 and 14. The rectangle has no fill.
    $graphics.DrawString('KA', $name, [Drawing.Brushes]::Black, [single]389, [single]92)
    $graphics.DrawRectangle($pen, 430, 168, 34, 67)
    $graphics.DrawLine($pen, 397, 293, 397, 201)
    $graphics.DrawLine($pen, 397, 201, 430, 201)
    $graphics.DrawLine($pen, 464, 201, 497, 201)
    $graphics.DrawLine($pen, 497, 201, 497, 293)
    foreach ($terminal in @(@(397, 13), @(497, 14))) {
        [single]$x = $terminal[0]
        $graphics.DrawEllipse($pen, $x - 6, 293, 12, 12)
        $graphics.DrawString([string]$terminal[1], $numbers, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($x - 20, 304, 40, 26), $format)
    }
    $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $format.Dispose()
    $name.Dispose()
    $numbers.Dispose()
    $pen.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
Write-Output "Generated: $destination"
