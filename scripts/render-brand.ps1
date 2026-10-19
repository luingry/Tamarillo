<#
    Rasterizes the vector brand (src/Tamarillo/Theme/Brand.xaml) into:
      assets/brand/tamarillo.ico         multi-size icon (small sizes use Brand.LogoSmallDrawing)
      assets/brand/png/logo-<size>.png   PNG exports
      assets/brand/png/mark-512.png      mark on transparent background
    Run with Windows PowerShell (STA, WPF available):  powershell -File scripts/render-brand.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml

$root = Split-Path -Parent $PSScriptRoot
$brandDir = Join-Path $root 'assets/brand'
$pngDir = Join-Path $brandDir 'png'
New-Item -ItemType Directory -Force $pngDir | Out-Null

$dict = [Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $root 'src/Tamarillo/Theme/Brand.xaml') -Raw))
$logo = $dict['Brand.LogoDrawing']
$small = $dict['Brand.LogoSmallDrawing']
$mark = $dict['Brand.MarkDrawing']

function Render-Drawing($drawing, [int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 256), ($size / 256)))
    $dc.DrawDrawing($drawing)
    $dc.Pop()
    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    return $bmp
}

function Save-Png($bitmap, [string]$path) {
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$frames = @()
foreach ($size in $sizes) {
    $drawing = if ($size -le 32) { $small } else { $logo }
    $path = Join-Path $pngDir "logo-$size.png"
    Save-Png (Render-Drawing $drawing $size) $path
    $frames += , ([IO.File]::ReadAllBytes($path))
}
Save-Png (Render-Drawing $logo 512) (Join-Path $pngDir 'logo-512.png')
Save-Png (Render-Drawing $mark 512) (Join-Path $pngDir 'mark-512.png')

# ICO container with PNG-compressed frames (supported since Windows Vista).
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $frames[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $w.Write($data) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $brandDir 'tamarillo.ico'), $ms.ToArray())

Write-Host "Brand assets written to $brandDir" -ForegroundColor Green
