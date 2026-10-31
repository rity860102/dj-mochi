# 開發說明

## 檔案結構

```
dj-mochi\
├─ DJMochi.exe                主程式（編譯產生，不進 git）
├─ original-names.json        原始名稱備份（第一次改名時自動建立，不進 git）
├─ README.md                  英文說明（GitHub 首頁）
├─ README.zh-TW.md            繁體中文說明
├─ LICENSE
├─ .github\workflows\
│  └─ build.yml               CI / CD
├─ docs\
│  ├─ DEVELOPMENT.md          本文件
│  ├─ screenshot.png          中文介面截圖（README.zh-TW.md）
│  └─ screenshot-en.png       英文介面截圖（README.md）
├─ src\                      C# 5 / WPF，build.ps1 會編譯這裡所有的 .cs
│  ├─ Program.cs              進入點：防多開、開機縮在右下角
│  ├─ MainWindow.cs           主視窗：版面、裝置列表、顯示 / 隱藏、設為預設、重新命名
│  ├─ MainWindow.Tray.cs      右下角通知區域、縮小 / 叫回、開機啟動按鈕
│  ├─ MainWindow.AirPods.cs   AirPods 電量的監聽時機、篩選與顯示
│  ├─ MainWindow.Styles.cs    按鈕、滑桿、捲軸等 WPF 樣式
│  ├─ Audio.cs                音訊裝置清單與控制（顯示 / 隱藏、預設、改名、音量）
│  ├─ Startup.cs              開機自動啟動（HKCU Run）
│  ├─ Lang.cs                 介面文字（繁體中文 / English）與語言設定
│  ├─ Interop.cs              Windows COM 介面與 Win32 API 宣告
│  ├─ AirPods.cs              AirPods BLE 廣播監聽與電量解析
│  ├─ build.ps1               編譯腳本
│  └─ icon\
│     ├─ make-icon.ps1        圖示繪製腳本（WPF 向量繪圖）
│     ├─ mochi.ico            exe 與右下角圖示（16～256 px）
│     ├─ mochi.png            程式內使用的圖示（256 px）
│     └─ preview.png          各尺寸預覽圖（產生後不進 git）
└─ tools\
   ├─ airpods-scan.ps1        AirPods 廣播測試工具（編譯並執行）
   └─ AirPodsScan.cs
```

---

## 建置

使用 Windows 內建的 C# 編譯器（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），不需要 Visual Studio 或 .NET SDK。

```powershell
# 編譯（輸出到專案根目錄的 DJMochi.exe，版本號預設 0.0.0-dev）
pwsh -File src\build.ps1

# 指定版本號（寫進 exe 的「內容 → 詳細資料」）
pwsh -File src\build.ps1 -Version 1.2.0

# 修改圖示後，重新產生 ico / png 再編譯
pwsh -File src\icon\make-icon.ps1
pwsh -File src\build.ps1

# 測試 AirPods 廣播解析（印出附近 AirPods 的電量，藍牙位址只顯示後 4 碼）
pwsh -File tools\airpods-scan.ps1 -Seconds 30
```

> 編譯前要先關閉正在執行的 `DJMochi.exe`，否則檔案被鎖住無法覆寫。

### 注意事項
- 內建的 `csc.exe` **只支援 C# 5**：不能用 `$"..."`、`?.`、`=>` 運算式成員、`nameof`、`out var` 等語法
- 原始碼是 UTF-8，`build.ps1` 已加上 `/codepage:65001`
- 圖示和中文要放在**不同的 TextBlock**。共用 `Segoe Fluent Icons, Microsoft JhengHei UI` 這類混合字型時，中文會變成方框
- 呼叫 WinRT（藍牙 BLE）需要參考 `C:\Windows\System32\WinMetadata\*.winmd` 和 `System.Runtime.InteropServices.WindowsRuntime.dll` 等 facade，C# 5 才能用 `+=` 訂閱 WinRT 事件
- `IBuffer` 不要用 `ToArray()`（會需要完整 Windows SDK 的 `Windows.winmd`），改用 `DataReader` 讀取
- `make-icon.ps1` 不能用 `[ordered]@{}` 搭配整數鍵，PowerShell 會把整數當成索引

### 介面文字（多語言）
- 所有介面文字都放在 `src/Lang.cs` 的 `Table`，每筆是 `{ "key", new[] { "繁體中文", "English" } }`
- 程式裡用 `Lang.Tr("key")` 取得目前語言的文字，有參數時用 `Lang.Tr("key", arg0, arg1)`（內部是 `string.Format`）
- 找不到的 key 會直接顯示 key 本身，畫面上看到 `xxx.yyy` 這種文字就代表漏加翻譯
- 語言設定存在 `HKCU\Software\DJMochi` 的 `Language`（`zh` / `en`；沒有這個值 = 自動跟隨 Windows）
- 裝置名稱（例如「耳機」「Headphones」）是 Windows 依系統語言提供的，不經過 `Lang`
- `MainWindow.IconFor` 裡的「麥克風」「耳機」等字是用來比對 Windows 裝置名稱、決定圖示的關鍵字，不是介面文字，不要翻譯

---

## CI / CD

設定檔：`.github/workflows/build.yml`，在 GitHub 的 Windows 主機上用同一個 `build.ps1` 編譯。

| 觸發 | 動作 |
|---|---|
| 推送到 `main` | 編譯 → 依 commit 類型自動決定版本號 → 建立 Release，附上 `DJMochi.exe` 與 `DJMochi.exe.sha256` |
| 推送 `v*` tag | 用該 tag 的版本號建立 Release |
| 發 PR、手動執行 | 只編譯，exe 存成 Artifact（版本 `0.0.0-dev+<commit>`） |

### 版本號規則
**發布新版本 = 正常 commit 並 push 到 `main`**。版本號依上次發布之後的 commit 訊息（[Conventional Commits](https://www.conventionalcommits.org/zh-hant/)）自動遞增：

| 上次發布後的 commit | 版本變化 | 例子 |
|---|---|---|
| 有 `feat:` | 第二碼 +1 | `1.0.0` → `1.1.0` |
| 只有 `fix:`、`refactor:`、`perf:` 等 | 第三碼 +1 | `1.1.0` → `1.1.1` |
| 有 `feat!:` 或內文有 `BREAKING CHANGE` | 第一碼 +1 | `1.1.0` → `2.0.0` |
| 全部都是 `docs:`、`ci:`、`chore:`、`style:`、`test:` | 不發布，只跑 CI | |
| 最新 commit 訊息含 `[skip release]` | 不發布 | |

Release 說明依 commit 紀錄自動產生，發布後可以在 GitHub 上手動編輯。

---

## 技術原理

### 音訊裝置

| 功能 | Windows API |
|---|---|
| 顯示 / 隱藏 | `IPolicyConfig::SetEndpointVisibility` |
| 設為預設 | `IPolicyConfig::SetDefaultEndpoint`（Console / Multimedia / Communications 三種角色） |
| 重新命名 | `IPolicyConfig::SetPropertyValue` 寫入 `PKEY_Device_DeviceDesc` |
| 音量 / 靜音 | `IAudioEndpointVolume` |
| 讀取預設裝置 | `IMMDeviceEnumerator::GetDefaultAudioEndpoint` |
| 裝置清單 | 登錄檔 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\{Render,Capture}` |

`IPolicyConfig` 是 Windows「聲音」控制台自己使用的 COM 介面（`CPolicyConfigClient`，CLSID `870AF99C-171D-4f9e-AF0D-E63DF40C2BC9`，位於 `AudioSes.dll`）。微軟沒有公開文件，但從 Windows 7 以來一直保持穩定。

登錄檔 `DeviceState` 的值：

| 值 | 意義 |
|---|---|
| `0x1` | 啟用 |
| `0x2` | 停用 |
| `0x4` | 不存在（已移除的裝置，不會列出） |
| `0x8` | 未接上 |
| `0x10000000` 旗標 | 被「不允許」隱藏，例如 `0x10000001` |

**為什麼不用裝置管理員的「停用」？**
`Disable-PnpDevice` 需要系統管理員權限，而且停用後音訊服務不一定會同步，Windows 的音效選單仍可能看得到。`SetEndpointVisibility` 才跟 Windows 設定的「不允許」完全一致。

### AirPods 電量

AirPods 會持續發送 Apple 的 Proximity Pairing BLE 廣播，DJ Mochi 用 WinRT `BluetoothLEAdvertisementWatcher` 監聽並解析。格式依公開的逆向工程文件自行實作：

| 位元組 | 內容 |
|---|---|
| 廠商 ID | `0x004C`（Apple） |
| `[0]` | `0x07` = Proximity Pairing |
| `[1]` | 後續資料長度 |
| `[3-4]` | 型號（大端序），例如 `0x2720` = AirPods Pro 3 |
| `[5]` | 狀態；高 4 bit 的 `0x2` 是主耳旗標，決定 `[6]` 的左右順序 |
| `[6]` | 兩耳電量，各 4 bit |
| `[7]` | 高 4 bit：充電旗標（兩耳、`0x4` = 充電盒）；低 4 bit：充電盒電量 |

電量值 `0`～`10` 乘以 10 即為百分比，`15` 代表沒有連線。

**辨識是不是自己的 AirPods**：已配對藍牙裝置的識別碼形如 `BTHENUM\{...}_VID&0001004c_PID&2027`，PID 兩個位元組對調就是廣播裡的型號，只採用這些型號的廣播。型號相同時，採用訊號最強（最近）的那一副。

**隱私**：藍牙位址只保存在記憶體中，用來分辨不同的 AirPods，不會寫入檔案或傳送到網路。

參考資料：
- [furiousMAC/continuity：Proximity Pairing](https://github.com/furiousMAC/continuity/blob/master/messages/proximity_pairing.md)
- [apple-ble（Python）](https://pypi.org/project/apple-ble/)

### 為什麼用 C# 而不是 PowerShell？
最早的版本是 PowerShell + WPF，但 PowerShell 本身啟動就要約 0.5 秒。改用 C# 編譯成 exe 後，開啟時間從約 0.75 秒降到約 0.3 秒。
