<#
.SYNOPSIS
  Regenerates the application icon files from one shape table.

.DESCRIPTION
  The icon is "Lens + Cube": an amber magnifier with a cube inside. The shapes are listed once,
  in a 256-unit design space, and rendered three ways:

    Assets/icon.svg       the master drawing
    Assets/icon-<n>.png   rasters; 512 and 256 are used by the app, the rest feed the .ico
    Assets/icon.ico       16, 24, 32, 48, 64, 128 and 256 px, PNG-compressed
    Assets/icon.icns      macOS: 16 to 1024 px (including the @2x sizes), PNG-compressed

  Sizes of 32 px and below use a simplified drawing of the same mark. At 16 px a 9-unit stroke
  is half a pixel wide, so the thin cube edges of the master would smear into a blob; the small
  drawing fills three flat faces and thickens the lens instead.

  Windows only: it draws with GDI+ (System.Drawing). The outputs are committed, so nothing else
  needs to run this. Re-run it only when the drawing changes.

    pwsh tools/build-icons.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\QemuXplorer.App\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ink = '#14171c'; $edge = '#2e3540'; $amber = '#f5a524'
$light = '#dfe4ec'; $side = '#9aa4b3'; $shade = '#6e7a8b'

# Largest size that still uses the simplified drawing.
$smallMax = 32

function Get-Shapes([bool]$small) {
    $tile = @(
        @{ Kind = 'rrect'; X = 16; Y = 16; W = 224; H = 224; R = 52; Fill = $ink }
    )
    if (-not $small) {
        $tile += @{ Kind = 'rrect'; X = 16.75; Y = 16.75; W = 222.5; H = 222.5; R = 51.25; Stroke = $edge; Sw = 1.5 }
    }

    if ($small) {
        return $tile + @(
            @{ Kind = 'circle'; Cx = 112; Cy = 112; R = 66; Stroke = $amber; Sw = 22 }
            @{ Kind = 'line'; X1 = 162; Y1 = 162; X2 = 208; Y2 = 208; Stroke = $amber; Sw = 34; Cap = 'butt' }
            @{ Kind = 'circle'; Cx = 208; Cy = 208; R = 17; Fill = $amber }
            # Three flat faces: top, left, right.
            @{ Kind = 'poly'; Pts = '112,70 148.4,91 112,112 75.6,91'; Fill = $light }
            @{ Kind = 'poly'; Pts = '75.6,91 112,112 112,154 75.6,133'; Fill = $side }
            @{ Kind = 'poly'; Pts = '112,112 148.4,91 148.4,133 112,154'; Fill = $shade }
        )
    }

    return $tile + @(
        @{ Kind = 'circle'; Cx = 112; Cy = 112; R = 68; Stroke = $amber; Sw = 16 }
        @{ Kind = 'line'; X1 = 162; Y1 = 162; X2 = 208; Y2 = 208; Stroke = $amber; Sw = 26; Cap = 'butt' }
        @{ Kind = 'circle'; Cx = 208; Cy = 208; R = 13; Fill = $amber }
        @{ Kind = 'poly'; Pts = '112,68 150.1,90 112,112 73.9,90'; Fill = $light }
        @{ Kind = 'poly'; Pts = '112,68 150.1,90 150.1,134 112,156 73.9,134 73.9,90'; Stroke = $light; Sw = 9 }
        @{ Kind = 'poly'; Pts = '73.9,90 112,112 150.1,90'; Stroke = $light; Sw = 9; Open = $true }
        @{ Kind = 'line'; X1 = 112; Y1 = 112; X2 = 112; Y2 = 156; Stroke = $light; Sw = 9 }
    )
}

function ConvertTo-Points([string]$text) {
    $n = $text -split '[ ,]+' | Where-Object { $_ } | ForEach-Object { [single]::Parse($_, [cultureinfo]::InvariantCulture) }
    for ($i = 0; $i -lt $n.Count; $i += 2) { [System.Drawing.PointF]::new($n[$i], $n[$i + 1]) }
}

function Get-RoundedRect($s) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = [single](2 * $s.R)
    $path.AddArc($s.X, $s.Y, $d, $d, 180, 90)
    $path.AddArc($s.X + $s.W - $d, $s.Y, $d, $d, 270, 90)
    $path.AddArc($s.X + $s.W - $d, $s.Y + $s.H - $d, $d, $d, 0, 90)
    $path.AddArc($s.X, $s.Y + $s.H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Pen($hex, $width, $cap) {
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml($hex), [single]$width)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $style = if ($cap -eq 'butt') { [System.Drawing.Drawing2D.LineCap]::Flat } else { [System.Drawing.Drawing2D.LineCap]::Round }
    $pen.StartCap = $style
    $pen.EndCap = $style
    return $pen
}

function Render-Png([int]$size, [bool]$small) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    foreach ($s in (Get-Shapes $small)) {
        $fill = if ($s.Fill) { [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($s.Fill)) }
        $pen = if ($s.Stroke) { New-Pen $s.Stroke $s.Sw $s.Cap }

        switch ($s.Kind) {
            'rrect' {
                $path = Get-RoundedRect $s
                if ($fill) { $g.FillPath($fill, $path) }
                if ($pen) { $g.DrawPath($pen, $path) }
            }
            'circle' {
                $x = $s.Cx - $s.R; $y = $s.Cy - $s.R; $d = 2 * $s.R
                if ($fill) { $g.FillEllipse($fill, [single]$x, [single]$y, [single]$d, [single]$d) }
                if ($pen) { $g.DrawEllipse($pen, [single]$x, [single]$y, [single]$d, [single]$d) }
            }
            'line' {
                $g.DrawLine($pen, [single]$s.X1, [single]$s.Y1, [single]$s.X2, [single]$s.Y2)
            }
            'poly' {
                $pts = [System.Drawing.PointF[]]@(ConvertTo-Points $s.Pts)
                if ($fill) { $g.FillPolygon($fill, $pts) }
                if ($pen) {
                    if ($s.Open) { $g.DrawLines($pen, $pts) } else { $g.DrawPolygon($pen, $pts) }
                }
            }
        }
    }

    $g.Dispose()
    return $bmp
}

function ConvertTo-PngBytes($bmp) {
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

function Format-Number([double]$v) { $v.ToString('0.##', [cultureinfo]::InvariantCulture) }

function Write-Svg([string]$path) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">')
    foreach ($s in (Get-Shapes $false)) {
        $paint = ''
        if ($s.Fill) { $paint += " fill=`"$($s.Fill)`"" } else { $paint += ' fill="none"' }
        $linecap = if ($s.Cap) { $s.Cap } else { 'round' }
        if ($s.Stroke) { $paint += " stroke=`"$($s.Stroke)`" stroke-width=`"$(Format-Number $s.Sw)`" stroke-linejoin=`"round`" stroke-linecap=`"$linecap`"" }
        switch ($s.Kind) {
            'rrect'  { [void]$sb.AppendLine("  <rect x=`"$(Format-Number $s.X)`" y=`"$(Format-Number $s.Y)`" width=`"$(Format-Number $s.W)`" height=`"$(Format-Number $s.H)`" rx=`"$(Format-Number $s.R)`"$paint/>") }
            'circle' { [void]$sb.AppendLine("  <circle cx=`"$($s.Cx)`" cy=`"$($s.Cy)`" r=`"$($s.R)`"$paint/>") }
            'line'   { [void]$sb.AppendLine("  <line x1=`"$($s.X1)`" y1=`"$($s.Y1)`" x2=`"$($s.X2)`" y2=`"$($s.Y2)`"$paint/>") }
            'poly'   {
                $tag = if ($s.Open) { 'polyline' } else { 'polygon' }
                [void]$sb.AppendLine("  <$tag points=`"$($s.Pts)`"$paint/>")
            }
        }
    }
    [void]$sb.AppendLine('</svg>')
    [System.IO.File]::WriteAllText($path, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
}

function Write-Ico([string]$path, [int[]]$sizes) {
    $images = foreach ($size in $sizes) {
        $bmp = Render-Png $size ($size -le $smallMax)
        [pscustomobject]@{ Size = $size; Bytes = (ConvertTo-PngBytes $bmp) }
        $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($path)
    $w = [System.IO.BinaryWriter]::new($fs)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]@($images).Count)

    $offset = 6 + 16 * @($images).Count
    foreach ($img in $images) {
        # A width or height of 256 is stored as 0.
        $dim = if ($img.Size -ge 256) { [byte]0 } else { [byte]$img.Size }
        $w.Write($dim); $w.Write($dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$img.Bytes.Length); $w.Write([uint32]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) { $w.Write($img.Bytes) }
    $w.Dispose(); $fs.Dispose()
}

function ConvertTo-BigEndianBytes([uint32]$value) {
    $bytes = [BitConverter]::GetBytes($value)
    [array]::Reverse($bytes)
    return , $bytes
}

# An .icns file is the ASCII tag 'icns', a big-endian total length, then one block per image:
# a four-letter type, a big-endian length that includes these 8 header bytes, and the PNG data.
# The type-to-size table below is the one `iconutil` produces from an .iconset folder, so an
# @2x entry is simply the next size up (16@2x is 32 px, 512@2x is 1024 px).
function Write-Icns([string]$path) {
    $entries = @(
        @{ Type = 'icp4'; Size = 16 }   # 16
        @{ Type = 'ic11'; Size = 32 }   # 16@2x
        @{ Type = 'icp5'; Size = 32 }   # 32
        @{ Type = 'ic12'; Size = 64 }   # 32@2x
        @{ Type = 'ic07'; Size = 128 }  # 128
        @{ Type = 'ic13'; Size = 256 }  # 128@2x
        @{ Type = 'ic08'; Size = 256 }  # 256
        @{ Type = 'ic14'; Size = 512 }  # 256@2x
        @{ Type = 'ic09'; Size = 512 }  # 512
        @{ Type = 'ic10'; Size = 1024 } # 512@2x
    )

    $png = @{}
    foreach ($size in ($entries | ForEach-Object { $_.Size } | Select-Object -Unique)) {
        $bmp = Render-Png $size ($size -le $smallMax)
        $png[$size] = ConvertTo-PngBytes $bmp
        $bmp.Dispose()
    }

    $body = [System.IO.MemoryStream]::new()
    foreach ($entry in $entries) {
        $data = $png[$entry.Size]
        $type = [System.Text.Encoding]::ASCII.GetBytes($entry.Type)
        $length = ConvertTo-BigEndianBytes ([uint32](8 + $data.Length))
        $body.Write($type, 0, 4)
        $body.Write($length, 0, 4)
        $body.Write($data, 0, $data.Length)
    }

    $file = [System.IO.File]::Create($path)
    $magic = [System.Text.Encoding]::ASCII.GetBytes('icns')
    $total = ConvertTo-BigEndianBytes ([uint32](8 + $body.Length))
    $file.Write($magic, 0, 4)
    $file.Write($total, 0, 4)
    $body.WriteTo($file)
    $file.Dispose()
    $body.Dispose()
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Write-Svg (Join-Path $OutDir 'icon.svg')

foreach ($size in 512, 256) {
    $bmp = Render-Png $size $false
    $bmp.Save((Join-Path $OutDir "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Write-Ico (Join-Path $OutDir 'icon.ico') @(16, 24, 32, 48, 64, 128, 256)
Write-Icns (Join-Path $OutDir 'icon.icns')

Get-ChildItem $OutDir -Filter 'icon*' | Select-Object Name, Length | Format-Table -AutoSize
