# airpods-scan.ps1 — 編譯並執行 AirPods 廣播測試工具（開發 / 除錯用）
#   pwsh -File tools\airpods-scan.ps1 [-Seconds 30]
param([int]$Seconds = 30)
$ErrorActionPreference = 'Stop'

$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wm  = 'C:\Windows\System32\WinMetadata'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path ([IO.Path]::GetTempPath()) 'DJMochi.AirPodsScan.exe'

& "$fw\csc.exe" /nologo /target:exe /platform:x64 /codepage:65001 "/out:$out" `
    "/r:$wm\Windows.Devices.winmd" "/r:$wm\Windows.Foundation.winmd" "/r:$wm\Windows.Storage.winmd" `
    "/r:$fw\System.Runtime.WindowsRuntime.dll" "/r:$fw\System.Runtime.dll" "/r:$fw\System.Runtime.InteropServices.WindowsRuntime.dll" `
    (Join-Path $root 'src\AirPods.cs') (Join-Path $PSScriptRoot 'AirPodsScan.cs')
if ($LASTEXITCODE -ne 0) { throw "編譯失敗 (exit $LASTEXITCODE)" }

& $out $Seconds
