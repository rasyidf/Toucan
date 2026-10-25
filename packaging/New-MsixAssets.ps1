# Generate Windows packaging assets from the maintained app logo.
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source = [Drawing.Image]::FromFile("$PSScriptRoot\..\Toucan.Avalonia\Assets\logo.png")
try {
    $sizes = @{
        'StoreLogo.png' = @(50, 50)
        'Square44x44Logo.png' = @(44, 44)
        'Square150x150Logo.png' = @(150, 150)
        'Wide310x150Logo.png' = @(310, 150)
        'SmallTile.png' = @(71, 71)
        'LargeTile.png' = @(310, 310)
        'document.png' = @(44, 44)
    }
    foreach ($name in $sizes.Keys) {
        $width, $height = $sizes[$name]
        $bitmap = [Drawing.Bitmap]::new($width, $height)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scale = [Math]::Min($width / $source.Width, $height / $source.Height)
            $drawWidth = [int]($source.Width * $scale)
            $drawHeight = [int]($source.Height * $scale)
            $graphics.DrawImage($source, [int](($width - $drawWidth) / 2), [int](($height - $drawHeight) / 2), $drawWidth, $drawHeight)
            $bitmap.Save((Join-Path $OutputDirectory $name), [Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
} finally {
    $source.Dispose()
}
