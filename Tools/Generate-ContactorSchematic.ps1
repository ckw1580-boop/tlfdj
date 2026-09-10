# Reference schematic redrawn on transparent pixels; no photographic background.
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(1064, 808, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bitmap)
$pen = [Drawing.Pen]::new([Drawing.Color]::Black, 3)
$font = [Drawing.Font]::new('Arial', 24, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$name = [Drawing.Font]::new('Arial', 32, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$format = [Drawing.StringFormat]::new()
$format.Alignment = [Drawing.StringAlignment]::Center
try {
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform(2, 2)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    # Independent auxiliary contacts and three mechanically linked main contacts.
    $contacts = @(@(36,'53','54',$false), @(75,'61','62',$true), @(113,'71','72',$true), @(152,'83','84',$false),
                  @(222,'1L1','2T1',$false), @(275,'3L2','4T2',$false), @(328,'5L3','6T3',$false), @(380,'13','14',$false))
    foreach ($contact in $contacts) {
        [single]$x = $contact[0]
        $g.DrawString($contact[1], $font, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($x - 27, 145, 54, 26), $format)
        $g.DrawString($contact[2], $font, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new($x - 27, 252, 54, 26), $format)
        $g.DrawLine($pen, $x, 172, $x, 199)
        $g.DrawLine($pen, $x, 250, $x, 225)
        if ($contact[3]) {
            $g.DrawLine($pen, $x, 199, $x + 12, 199)
            $g.DrawLine($pen, $x, 225, $x + 9, 196)
        } else {
            $g.DrawLine($pen, $x, 225, $x - 16, 196)
        }
    }
    # Mechanical linkage only, matching the horizontal dashed line in the reference.
    $link = [Drawing.Pen]::new([Drawing.Color]::Black, 2)
    $link.DashPattern = [single[]]@(8, 4)
    $g.DrawLine($link, 216, 210, 324, 210)
    $link.Dispose()
    # KM coil and its two separate leads.
    $g.DrawString('KM', $name, [Drawing.Brushes]::Black, [single]428, [single]130)
    $g.DrawLine($pen, 421, 250, 421, 172)
    $g.DrawLine($pen, 421, 172, 489, 172)
    $g.DrawLine($pen, 489, 172, 489, 197)
    $g.DrawRectangle($pen, 463, 197, 53, 28)
    $g.DrawLine($pen, 489, 225, 489, 250)
    $g.DrawString('A1', $font, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new(399, 252, 44, 26), $format)
    $g.DrawString('A2', $font, [Drawing.Brushes]::Black, [Drawing.RectangleF]::new(469, 252, 44, 26), $format)
    $bitmap.Save((Join-Path $PSScriptRoot '../Assets/ElectricalSim/Resources/ContactorSchematic.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally { $format.Dispose(); $name.Dispose(); $font.Dispose(); $pen.Dispose(); $g.Dispose(); $bitmap.Dispose() }
