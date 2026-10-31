# 用 Windows 內建的 .NET Framework 4.8 C# 編譯器建置 DJMochi.exe（不需要安裝任何東西）
#   pwsh -File src\build.ps1                    本機開發（版本 0.0.0-dev）
#   pwsh -File src\build.ps1 -Version 1.2.0     指定版本（CI 依 tag 自動帶入）
param(
    [string]$Version = '0.0.0-dev'
)
$ErrorActionPreference = 'Stop'
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$src  = Join-Path $PSScriptRoot 'DJMochi.cs'
$icon = Join-Path $PSScriptRoot 'icon'
$out  = Join-Path (Split-Path $PSScriptRoot -Parent) 'DJMochi.exe'

# 版本資訊（顯示在 exe 的「內容 → 詳細資料」）
# AssemblyVersion 只能是數字，例如 1.2.0；完整字串（含 -dev、+commit）放 InformationalVersion
$numeric = ($Version -split '[-+]')[0]
if ($numeric -notmatch '^\d+(\.\d+){1,3}$') { throw "版本號格式錯誤：$Version（應為 1.2.3 或 1.2.3-xxx）" }
$info = Join-Path ([IO.Path]::GetTempPath()) ("DJMochi.AssemblyInfo.{0}.cs" -f [guid]::NewGuid())
@"
using System.Reflection;
[assembly: AssemblyTitle("DJ Mochi")]
[assembly: AssemblyProduct("DJ Mochi")]
[assembly: AssemblyDescription("DJ Mochi - Windows 音訊裝置管理")]
[assembly: AssemblyCopyright("Copyright (c) 2026 rity860102 - MIT License")]
[assembly: AssemblyVersion("$numeric")]
[assembly: AssemblyFileVersion("$numeric")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content $info -Encoding utf8

try {
    & "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        "/out:$out" `
        "/win32icon:$icon\mochi.ico" `
        "/resource:$icon\mochi.png,DJMochi.mochi.png" `
        "/r:$fw\WPF\PresentationFramework.dll" `
        "/r:$fw\WPF\PresentationCore.dll" `
        "/r:$fw\WPF\WindowsBase.dll" `
        "/r:$fw\System.Xaml.dll" `
        "/r:$fw\System.Web.Extensions.dll" `
        $src $info
    if ($LASTEXITCODE -ne 0) { throw "編譯失敗 (exit $LASTEXITCODE)" }
}
finally { Remove-Item $info -Force -EA SilentlyContinue }
"已建置：$out（版本 $Version）"
