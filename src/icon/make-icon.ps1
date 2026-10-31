# make-icon.ps1 — 用 WPF 向量繪製 DJ Mochi 圖示，輸出 PNG（多尺寸）與 mochi.ico
# 執行：pwsh -File make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$dir = $PSScriptRoot
$bc = New-Object System.Windows.Media.BrushConverter
function Br([string]$hex) { $b = $bc.ConvertFromString($hex); $b.Freeze(); $b }
function Pen([string]$hex, [double]$w) {
    $p = New-Object System.Windows.Media.Pen (Br $hex), $w
    $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'; $p.LineJoin = 'Round'; $p.Freeze(); $p
}
function Geo([string]$s) { [System.Windows.Media.Geometry]::Parse($s) }
function Pt($x, $y) { New-Object System.Windows.Point $x, $y }

# 調色盤（日系粉嫩）
$C = @{
    Line      = '#5B4646'   # 柔和深棕描邊
    MochiTop  = '#FFFFFF'
    MochiBot  = '#FFE6DC'
    Cheek     = '#FFAFC0'
    Eye       = '#3D2F2F'
    Mint      = '#7FDDB8'   # 耳機
    MintDark  = '#4FBF94'
    Band      = '#2E3038'
    Vinyl     = '#1E2028'
    Groove    = '#30333D'
    Label     = '#FF8FAB'
    Note      = '#FF8FAB'
}

function Draw-Mochi($dc) {
    # ---- 黑膠唱片（背景）----
    # 外框用淺灰粉，深色工作列上也看得到唱片輪廓
    $dc.DrawEllipse((Br $C.Vinyl), (Pen '#6E6470' 5), (Pt 128 132), 116, 116)
    foreach ($r in 104, 92, 80) { $dc.DrawEllipse($null, (Pen $C.Groove 2.5), (Pt 128 132), $r, $r) }
    # 光澤
    $dc.DrawGeometry($null, (Pen '#4A4E5A' 5), (Geo 'M 52,70 A 104,104 0 0 1 104,32'))

    # ---- 耳機頭帶 ----
    $dc.DrawGeometry($null, (Pen $C.Line 20), (Geo 'M 46,150 C 46,52 210,52 210,150'))
    $dc.DrawGeometry($null, (Pen $C.Band 13), (Geo 'M 46,150 C 46,52 210,52 210,150'))
    $dc.DrawGeometry($null, (Pen '#4A4E5A' 3), (Geo 'M 62,118 C 70,78 100,66 128,66'))

    # ---- 麻糬本體 ----
    $grad = New-Object System.Windows.Media.LinearGradientBrush
    $grad.StartPoint = Pt 0.5 0; $grad.EndPoint = Pt 0.5 1
    $grad.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString($C.MochiTop)), 0.0))
    $grad.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString($C.MochiBot)), 1.0))
    $grad.Freeze()
    $body = Geo 'M 38,172 C 38,116 80,92 128,92 C 176,92 218,116 218,172 C 218,212 180,230 128,230 C 76,230 38,212 38,172 Z'
    $dc.DrawGeometry($grad, (Pen $C.Line 7), $body)
    # 頂部高光
    $dc.DrawGeometry($null, (Pen '#FFFFFF' 6), (Geo 'M 92,108 C 104,102 118,100 130,100'))

    # ---- 臉 ----
    # 眼睛（閃亮）
    foreach ($x in 100, 156) {
        $dc.DrawEllipse((Br $C.Eye), $null, (Pt $x 164), 10, 12)
        $dc.DrawEllipse((Br '#FFFFFF'), $null, (Pt ($x + 3.5) 159), 4, 4)
        $dc.DrawEllipse((Br '#FFFFFF'), $null, (Pt ($x - 3) 169), 1.8, 1.8)
    }
    # 腮紅
    foreach ($x in 78, 178) { $dc.DrawEllipse((Br $C.Cheek), $null, (Pt $x 186), 15, 9) }
    # ω 嘴
    $dc.DrawGeometry($null, (Pen $C.Line 5), (Geo 'M 116,183 Q 122,192 128,184 Q 134,192 140,183'))

    # ---- 耳罩 ----
    foreach ($x in 22, 196) {
        $rect = New-Object System.Windows.Rect $x, 124, 38, 64
        $dc.DrawRoundedRectangle((Br $C.Mint), (Pen $C.Line 6), $rect, 17, 17)
        $inner = New-Object System.Windows.Rect ($x + 9), 136, 20, 40
        $dc.DrawRoundedRectangle((Br $C.MintDark), $null, $inner, 9, 9)
    }

    # ---- 音符 ♪ ----
    $dc.DrawGeometry((Br $C.Note), (Pen $C.Line 5), (Geo 'M 214,26 L 214,62 M 214,26 C 226,30 238,34 240,48'))
    $dc.DrawGeometry($null, (Pen $C.Line 5), (Geo 'M 214,26 L 214,64'))
    $head = New-Object System.Windows.Media.EllipseGeometry (Pt 205 66), 11, 8.5
    $head.Transform = New-Object System.Windows.Media.RotateTransform -20, 205, 66
    $dc.DrawGeometry((Br $C.Note), (Pen $C.Line 4.5), $head)
    $dc.DrawGeometry($null, (Pen $C.Note 3), (Geo 'M 216,30 C 225,33 233,37 236,46'))
}

function Render([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
    Draw-Mochi $dc
    $dc.Pop(); $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

# ---- 輸出 ----
$sizes = 256, 128, 64, 48, 32, 24, 16
$pngs = @{}   # 不能用 [ordered]：整數鍵會被當成索引
foreach ($s in $sizes) { $pngs[$s] = Render $s }
[IO.File]::WriteAllBytes((Join-Path $dir 'mochi.png'), $pngs[256])

# ICO（PNG 壓縮項目，Vista 以後支援）
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $len = $pngs[$s].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $dir 'mochi.ico'), $ms.ToArray())

# 預覽圖：各尺寸並排，淺色 / 深色背景各一排
$pw = 256 + 128 + 64 + 48 + 32 + 24 + 16 + 8 * 20
$dv = New-Object System.Windows.Media.DrawingVisual
$dc = $dv.RenderOpen()
foreach ($row in @(@('#F3F3F3', 0), @('#202020', 276))) {
    $dc.DrawRectangle((Br $row[0]), $null, (New-Object System.Windows.Rect 0, $row[1], $pw, 276))
    $x = 10
    foreach ($s in $sizes) {
        $bmp = New-Object System.Windows.Media.Imaging.BitmapImage
        $bmp.BeginInit(); $bmp.StreamSource = New-Object System.IO.MemoryStream (, $pngs[$s]); $bmp.EndInit()
        $dc.DrawImage($bmp, (New-Object System.Windows.Rect $x, ($row[1] + 10 + (256 - $s)), $s, $s))
        $x += $s + 20
    }
}
$dc.Close()
$rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap ([int]$pw), 552, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($dv)
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$fs = [IO.File]::Create((Join-Path $dir 'preview.png')); $enc.Save($fs); $fs.Close()
"已輸出：mochi.png、mochi.ico、preview.png"
