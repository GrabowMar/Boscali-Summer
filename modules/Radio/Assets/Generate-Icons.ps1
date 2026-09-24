Add-Type -AssemblyName System.Drawing
$assets = $PSScriptRoot
$stations = @(
    @{ File = 'agrapol-fm.png'; Code = 'BR'; Label = 'REPUBLIC RADIO'; Top = '#143A4B'; Bottom = '#0B202D'; Accent = '#F2C46D' },
    @{ File = 'maris-network.png'; Code = 'PS'; Label = 'PALA STATE RADIO'; Top = '#591F2B'; Bottom = '#200E18'; Accent = '#E36868' },
    @{ File = 'base-broadcast.png'; Code = 'BB'; Label = 'BASE BROADCAST'; Top = '#344354'; Bottom = '#121C29'; Accent = '#9ECCE0' }
)

foreach ($station in $stations) {
    $image = [System.Drawing.Bitmap]::new(256, 256)
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $bounds = [System.Drawing.Rectangle]::new(0, 0, 256, 256)
    $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bounds, [System.Drawing.ColorTranslator]::FromHtml($station.Top),
        [System.Drawing.ColorTranslator]::FromHtml($station.Bottom), 90)
    $accent = [System.Drawing.ColorTranslator]::FromHtml($station.Accent)
    $pen = [System.Drawing.Pen]::new($accent, 3)
    $line = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, $accent), 1)
    $brush = [System.Drawing.SolidBrush]::new($accent)
    $fontCode = [System.Drawing.Font]::new('Segoe UI', 62, [System.Drawing.FontStyle]::Bold)
    $fontLabel = [System.Drawing.Font]::new('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
    $center = [System.Drawing.StringFormat]::new()
    $center.Alignment = [System.Drawing.StringAlignment]::Center
    $center.LineAlignment = [System.Drawing.StringAlignment]::Center
    try {
        $graphics.FillRectangle($gradient, $bounds)
        $graphics.DrawEllipse($pen, 34, 25, 188, 188)
        $graphics.DrawEllipse($line, 47, 38, 162, 162)
        $graphics.DrawLine($line, 12, 127, 244, 127)
        $graphics.DrawString($station.Code, $fontCode, $brush,
            [System.Drawing.RectangleF]::new(20, 60, 216, 118), $center)
        $graphics.FillRectangle($brush, 66, 209, 124, 3)
        $graphics.DrawString($station.Label, $fontLabel, $brush,
            [System.Drawing.RectangleF]::new(5, 215, 246, 25), $center)
        $image.Save((Join-Path $assets $station.File), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $center.Dispose(); $fontLabel.Dispose(); $fontCode.Dispose(); $brush.Dispose()
        $line.Dispose(); $pen.Dispose(); $gradient.Dispose(); $graphics.Dispose(); $image.Dispose()
    }
}
