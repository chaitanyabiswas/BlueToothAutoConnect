param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$svgPath = Join-Path $Root "BlueToothAutoConnect.UserAgent\Assets\BlueToothAutoConnect-logo.svg"
[xml]$svg = Get-Content $svgPath -Raw
$namespace = [System.Xml.XmlNamespaceManager]::new($svg.NameTable)
$namespace.AddNamespace("svg", "http://www.w3.org/2000/svg")
$namespace.AddNamespace("c2pa", "http://c2pa.org/manifest")

function ConvertTo-Brush {
    param([string]$Value)

    if ($Value -eq "none") {
        return $null
    }
    if ($Value -match '^url\(#(?<id>[^)]+)\)$') {
        $gradient = $svg.SelectSingleNode("//svg:linearGradient[@id='$($Matches.id)']", $namespace)
        $stops = @($gradient.SelectNodes("svg:stop", $namespace))
        $start = [Windows.Media.ColorConverter]::ConvertFromString($stops[0].GetAttribute("stop-color"))
        $end = [Windows.Media.ColorConverter]::ConvertFromString($stops[-1].GetAttribute("stop-color"))
        return [Windows.Media.LinearGradientBrush]::new($start, $end, [Windows.Point]::new(0, 0), [Windows.Point]::new(1, 1))
    }
    return [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($Value))
}

function Get-InheritedAttribute {
    param(
        [System.Xml.XmlElement]$Element,
        [string]$Name,
        [string]$Default
    )

    for ($current = $Element; $current -and $current.LocalName -ne "svg"; $current = $current.ParentNode) {
        if ($current.HasAttribute($Name)) {
            return $current.GetAttribute($Name)
        }
    }
    return $Default
}

function Render-Logo {
    param([int]$Size)

    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($Size / 512.0, $Size / 512.0))

    foreach ($element in $svg.SelectNodes("//svg:rect | //svg:path | //svg:polygon", $namespace)) {
        $fill = ConvertTo-Brush (Get-InheritedAttribute $element "fill" "black")
        $stroke = ConvertTo-Brush (Get-InheritedAttribute $element "stroke" "none")
        $pen = $null
        if ($stroke) {
            $pen = [Windows.Media.Pen]::new($stroke, [double](Get-InheritedAttribute $element "stroke-width" "1"))
            if ((Get-InheritedAttribute $element "stroke-linecap" "") -eq "round") {
                $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
                $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
            }
            if ((Get-InheritedAttribute $element "stroke-linejoin" "") -eq "round") {
                $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
            }
        }

        switch ($element.LocalName) {
            "rect" {
                $rect = [Windows.Rect]::new(
                    [double]$element.GetAttribute("x"),
                    [double]$element.GetAttribute("y"),
                    [double]$element.GetAttribute("width"),
                    [double]$element.GetAttribute("height"))
                $radius = [double]$element.GetAttribute("rx")
                $geometry = [Windows.Media.RectangleGeometry]::new($rect, $radius, $radius)
                $context.DrawGeometry($fill, $pen, $geometry)
            }
            "path" {
                $geometry = [Windows.Media.Geometry]::Parse($element.GetAttribute("d"))
                $context.DrawGeometry($fill, $pen, $geometry)
            }
            "polygon" {
                $geometry = [Windows.Media.StreamGeometry]::new()
                $points = @($element.GetAttribute("points") -split '[,\s]+' | Where-Object { $_ })
                $transform = $element.GetAttribute("transform")
                $translateX = 0.0
                $translateY = 0.0
                $rotation = 0.0
                if ($transform -match 'translate\((?<x>-?[\d.]+)[,\s]+(?<y>-?[\d.]+)\)') {
                    $translateX = [double]$Matches.x
                    $translateY = [double]$Matches.y
                }
                if ($transform -match 'rotate\((?<angle>-?[\d.]+)\)') {
                    $rotation = [double]$Matches.angle * [Math]::PI / 180.0
                }
                $stream = $geometry.Open()
                for ($index = 0; $index -lt $points.Count; $index += 2) {
                    $x = [double]$points[$index]
                    $y = [double]$points[$index + 1]
                    $rotatedX = $x * [Math]::Cos($rotation) - $y * [Math]::Sin($rotation)
                    $rotatedY = $x * [Math]::Sin($rotation) + $y * [Math]::Cos($rotation)
                    $point = [Windows.Point]::new($rotatedX + $translateX, $rotatedY + $translateY)
                    if ($index -eq 0) {
                        $stream.BeginFigure($point, $true, $true)
                    }
                    else {
                        $stream.LineTo($point, $true, $false)
                    }
                }
                $stream.Close()
                $context.DrawGeometry($fill, $pen, $geometry)
            }
        }
    }

    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return $bitmap
}

function Write-Png {
    param(
        [Windows.Media.Imaging.BitmapSource]$Bitmap,
        [string]$Path
    )

    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($Bitmap))
    $stream = [System.IO.File]::Create($Path)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
}

$userAssets = Join-Path $Root "BlueToothAutoConnect.UserAgent\Assets"
$cliAssets = Join-Path $Root "BlueToothAutoConnect.UserAgent.Cli\Assets"
$installerAssets = Join-Path $Root "BlueToothAutoConnect.Installer\Assets"
New-Item -ItemType Directory -Force -Path $cliAssets | Out-Null
New-Item -ItemType Directory -Force -Path $installerAssets | Out-Null

Write-Png (Render-Logo 512) (Join-Path $userAssets "BlueToothAutoConnect-logo.png")

$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $iconSizes) {
    $bitmap = Render-Logo $size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $encoder.Save($stream)
        ,@{ Size = $size; Bytes = $stream.ToArray() }
    }
    finally {
        $stream.Dispose()
    }
}

$iconStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($iconStream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -eq 256) { [byte]0 } else { [byte]$image.Size }
    $writer.Write($dimension)
    $writer.Write($dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) {
    $writer.Write([byte[]]$image.Bytes)
}
$writer.Flush()
$iconBytes = $iconStream.ToArray()
$writer.Dispose()
$iconStream.Dispose()

foreach ($destination in @(
    (Join-Path $userAssets "AppIcon.ico"),
    (Join-Path $cliAssets "AppIcon.ico"),
    (Join-Path $installerAssets "AppIcon.ico")
)) {
    [System.IO.File]::WriteAllBytes($destination, $iconBytes)
}

Write-Host "Generated PNG and Windows icons from $svgPath"
