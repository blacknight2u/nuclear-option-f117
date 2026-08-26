param(
    [string]$UiRoot
)

if ([string]::IsNullOrWhiteSpace($UiRoot)) {
    $repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $UiRoot = Join-Path $repositoryRoot 'UnityAuthoring\Assets\F117\UI'
}

Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $UiRoot 'F117_Damage.png'
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
try {
    # The source silhouette points down. Nuclear Option aircraft/status icons use
    # nose-up orientation, so rotate the exact authored silhouette once.
    $source.RotateFlip([System.Drawing.RotateFlipType]::Rotate180FlipNone)

    $minX = $source.Width
    $minY = $source.Height
    $maxX = -1
    $maxY = -1
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt $minX -or $maxY -lt $minY) {
        throw 'F117_Damage.png has no visible silhouette pixels.'
    }

    $crop = [System.Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)

    function Write-F117Sprite([string]$Path, [int]$CanvasSize, [double]$HeightFraction) {
        $output = New-Object System.Drawing.Bitmap $CanvasSize, $CanvasSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($output)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $height = [int][Math]::Round($CanvasSize * $HeightFraction)
                $width = [int][Math]::Round($height * $crop.Width / $crop.Height)
                $left = [int][Math]::Round(($CanvasSize - $width) / 2.0)
                $top = [int][Math]::Round(($CanvasSize - $height) / 2.0)
                $destination = New-Object System.Drawing.Rectangle $left, $top, $width, $height
                $graphics.DrawImage($source, $destination, $crop, [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $graphics.Dispose()
            }
            $output.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $output.Dispose()
        }
    }

    # Health display gets extra clearance because its parent HUD rect is clipped.
    Write-F117Sprite (Join-Path $UiRoot 'F117_Damage.png') 1024 0.72
    # Selection, map, friendly, and hostile icon: same canonical top view.
    Write-F117Sprite (Join-Path $UiRoot 'F117_Icon.png') 512 0.78
}
finally {
    $source.Dispose()
}
