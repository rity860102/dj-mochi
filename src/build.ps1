# 用 Windows 內建的 .NET Framework 4.8 C# 編譯器建置 DJMochi.exe（不需要安裝任何東西）
$ErrorActionPreference = 'Stop'
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$src  = Join-Path $PSScriptRoot 'DJMochi.cs'
$icon = Join-Path $PSScriptRoot 'icon'
$out  = Join-Path (Split-Path $PSScriptRoot -Parent) 'DJMochi.exe'

& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$out" `
    "/win32icon:$icon\mochi.ico" `
    "/resource:$icon\mochi.png,DJMochi.mochi.png" `
    "/r:$fw\WPF\PresentationFramework.dll" `
    "/r:$fw\WPF\PresentationCore.dll" `
    "/r:$fw\WPF\WindowsBase.dll" `
    "/r:$fw\System.Xaml.dll" `
    "/r:$fw\System.Web.Extensions.dll" `
    $src
if ($LASTEXITCODE -ne 0) { throw "編譯失敗 (exit $LASTEXITCODE)" }
"已建置：$out"
