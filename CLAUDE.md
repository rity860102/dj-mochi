# CLAUDE.md

給 AI 助理（Claude Code 等）的專案規則。詳細的檔案結構、技術原理請見 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)，本文件只列「必須遵守」的事項與常用指令。

## 專案簡介

DJ Mochi：Windows 音訊裝置管理工具（顯示 / 隱藏、設為預設、音量、重新命名、AirPods 電量）。C# 5 + WPF，單一 exe，MIT 授權的公開專案。

## 常用指令

```powershell
pwsh -File build.ps1                        # 編譯 → 根目錄的 DJMochi.exe（版本 0.0.0-dev）
pwsh -File build.ps1 -Version 1.2.0         # 指定版本號
pwsh -File src\Assets\make-icon.ps1         # 重新產生圖示（改圖示後才需要）
pwsh -File tools\airpods-scan.ps1 -Seconds 30   # AirPods 廣播測試工具
```

- 編譯前要先關閉執行中的 `DJMochi.exe`，否則檔案被鎖住無法覆寫
- 沒有自動化測試。改完請實際編譯並執行，確認視窗、右下角圖示、相關功能正常

## 硬性限制

- **只能用 C# 5 語法**：編譯器是 Windows 內建的 .NET Framework 4.8 `csc.exe`。不能用 `$"..."`、`?.`、`??=`、`=>` 運算式成員、`nameof`、`out var`、tuple、`async` 的新寫法等。事件處理用 `delegate(...) { }` 或 lambda
- **不新增外部相依**：不加 NuGet 套件、不需要另外安裝 SDK。只能參考 Windows 內建的 .NET Framework 組件與 `C:\Windows\System32\WinMetadata` 的 WinRT 中繼資料
- **不複製 GPL 等不相容授權的程式碼**：本專案是 MIT。可以參考公開的格式文件自行實作，但不能貼上 GPL 專案的程式碼
- **不需要系統管理員權限**：功能都必須在一般使用者權限下運作
- **隱私**：不得把藍牙位址、裝置 ID、使用者路徑寫進檔案、日誌，或傳送到網路。測試工具輸出藍牙位址時只顯示後 4 碼

## 程式慣例

- **按功能分資料夾**：新功能放 `src/Features/<功能名>/`。`MainWindow` 是 `partial class`，功能的介面程式碼寫成 `MainWindow.<功能>.cs` 放在同一個資料夾。`build.ps1` 會自動編譯 `src` 底下所有 `.cs`
- **介面文字一律用 `Lang.Tr("key")`**：在 `src/Core/Lang.cs` 的表格同時補上繁體中文與英文，不要直接在程式裡寫死中文或英文字串
  - 例外：`MainWindow.IconFor` 裡的「麥克風」「耳機」等字是用來比對 Windows 裝置名稱的關鍵字，不是介面文字，不要翻譯
- **圖示與文字分開**：圖示字型（Segoe Fluent Icons）和中文要放在不同的 `TextBlock`，否則中文會變成方框。可用 `IconLabel()`
- **背景執行緒**：WinRT / 藍牙事件在背景執行緒觸發，更新介面前要用 `Dispatcher.BeginInvoke` 切回 UI 執行緒，並包 `try/catch`，避免例外讓整個程式結束
- **AirPods 只在使用中監聽**：視窗開著且 AirPods 是預設播放裝置時才監聽，不要改成背景常駐掃描
- 原始碼為 UTF-8；註解用繁體中文，並說明「為什麼」而不只是「做什麼」

## Git 與發版

**push 到 `main` 就會自動發布新版本**，版本號由 commit 訊息決定（Conventional Commits）：

| commit 類型 | 結果 |
|---|---|
| `feat:` | 第二碼 +1，發布新版 |
| `fix:`、`refactor:`、`perf:` 等 | 第三碼 +1，發布新版 |
| `feat!:` 或內文有 `BREAKING CHANGE` | 第一碼 +1，發布新版 |
| `docs:`、`ci:`、`chore:`、`style:`、`test:` | 不發布 |

- 選對 commit 類型：只改文件就用 `docs:`，不要用 `fix:`，否則會發出沒有實質變更的版本
- **commit 訊息裡不要出現 `[skip release]` 字樣**（包括在說明文字中提到它），否則該次 push 不會發布
- 較大的變更先開分支開發，確認後再合併回 `main`
- 不要提交 `DJMochi.exe`、`original-names.json`（含使用者的裝置 ID）、`src/Assets/preview.png`，已在 `.gitignore` 排除
- 修改檔案結構、建置方式或使用方式時，同步更新 `docs/DEVELOPMENT.md` 與兩份 README（`README.md` 英文、`README.zh-TW.md` 繁體中文）

## 設計文件（SDD）

- **每個功能（feat）的 SDD 各自一個資料夾**：`docs/sdd/<功能名稱>/SDD.md`，功能名稱用小寫英文加連字號，例如 `docs/sdd/persistent-hidden-devices/`
- 同一個功能的測試腳本、截圖、補充筆記也放在該資料夾
- 開始實作某個功能前，先看 `TODO.md` 與對應的 SDD；SDD 裡「待決定事項」沒定案前，先跟使用者確認
- 實作完成後，在 SDD 開頭的「狀態」改成「已完成（vX.Y.Z）」，並在 `TODO.md` 打勾
