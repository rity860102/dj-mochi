# SDD：記住隱藏的裝置（Persistent Hidden Devices）

| 欄位 | 內容 |
|---|---|
| 狀態 | 已完成（v1.5.0） |
| 日期 | 2026-11-01 |
| 目標版本 | v1.5.0（`feat:`） |
| 分支 | `feat/persistent-hidden`（建議） |

---

## 1. 背景

使用者重新開機後，原本用 DJ Mochi 隱藏的裝置又出現了。實際檢查後發現是兩個原因同時發生：

1. **SteelSeries GG / Sonar 開機時重新啟用自己的虛擬裝置**
   使用者隱藏過的 `SteelSeries Sonar - Chat`、`Sonar - Gaming`、`Sonar - Microphone` 變回「顯示」。Sonar 啟動時會把它正在使用的頻道重新設為可見，覆蓋 Windows 的「不允許」設定。
2. **Realtek 音效驅動被更新 / 安裝，裝置 ID 改變**
   原本由 Windows 通用驅動提供的 `數位音訊 (S/PDIF) (High Definition Audio Device)` 消失，換成一批 `Realtek(R) Audio` 的新裝置（`Realtek Digital Output`、喇叭、麥克風、線路輸入、立體聲混音）。驅動換了，Windows 會建立新的端點 ID，舊的隱藏設定套不上去。

DJ Mochi 目前把設定直接寫進 Windows（`IPolicyConfig::SetEndpointVisibility`），本身不記錄「使用者想要隱藏什麼」，所以被別的程式或驅動改掉之後無法恢復。

## 2. 目標與非目標

### 目標
- DJ Mochi **記住使用者隱藏了哪些裝置**
- 這些裝置被其他程式重新啟用、或因驅動更新 / 換 USB 孔而以新 ID 出現時，**自動再隱藏**
- 開機時（`--tray` 模式，不顯示視窗）也要生效
- 使用者按「顯示」時，該裝置不再被自動隱藏

### 非目標（這次不做）
- 記住並鎖定「預設裝置」或「音量」
- 名稱完全不同的新裝置自動判斷要不要隱藏（例如這次的 `Realtek Digital Output` 是新名稱，需要使用者手動隱藏一次）
- 把 `original-names.json` 搬到新的設定檔（可留待之後統一）

## 3. 使用情境

1. 使用者隱藏「AirPods Pro Hands-Free」→ 重新開機 → DJ Mochi 在右下角啟動時確認它仍是隱藏的
2. SteelSeries GG 啟動後把 `Sonar - Chat` 重新打開 → DJ Mochi 收到裝置變化通知 → 自動再隱藏
3. 音效驅動更新後，`喇叭 (Steam Streaming Speakers)` 以新的 ID 出現，名稱相同 → 依名稱比對成功 → 自動隱藏
4. 使用者在 DJ Mochi 按「顯示」某個裝置 → 從記憶清單移除 → 之後不再自動隱藏它
5. 使用者在 **Windows 設定**裡手動把某個記住的裝置改回「允許」→ DJ Mochi 啟動 3 分鐘後，視為使用者的意思，不再自動隱藏，並記錄時間（見 §8 決定 2）

## 4. 設計

### 4.1 互動方式（建議：全自動）

- 在 DJ Mochi 按「**隱藏**」→ 自動加入記憶清單
- 在 DJ Mochi 按「**顯示**」→ 自動從記憶清單移除
- 不需要額外的按鈕；使用者「隱藏它」就代表「不想再看到它」

> 替代方案：每列多一個「📌 固定隱藏」開關，只有打開的才會自動隱藏。尚未定案，見 §8。

### 4.2 資料模型

設定檔：`%APPDATA%\DJMochi\settings.json`（只在本機，不進 git）

```json
{
  "version": 1,
  "autoHide": true,
  "hiddenDevices": [
    {
      "id": "{0.0.0.00000000}.{2344b43d-aa3a-44ba-8f49-668920e28672}",
      "flow": 0,
      "desc": "耳機",
      "dev": "AirPods Pro Hands-Free",
      "added": "2026-11-01T06:30:00"
    }
  ]
}
```

| 欄位 | 說明 |
|---|---|
| `autoHide` | 總開關，預設 `true`。給右下角選單「自動維持隱藏」使用 |
| `id` | 隱藏當下的端點 ID，優先用它比對 |
| `flow` | 0 = 播放、1 = 錄音 |
| `desc` | **原始名稱**（Windows 給的，不是使用者改過的名稱）。若該裝置改過名，取 `original-names.json` 裡的原始名稱 |
| `dev` | 硬體名稱（`{b3f8fa53-0004-438e-9003-51a46e139bfc},6`） |
| `added` | 加入時間，只供除錯參考 |

讀寫用現有的 `System.Web.Script.Serialization.JavaScriptSerializer`（`original-names.json` 已在用），不加新相依。

### 4.3 比對規則

一個目前**可見**的裝置 `d` 符合記憶清單中的項目 `r`，當：

1. `d.Id == r.id`（不分大小寫），或
2. `d.Flow == r.flow` **且** `d.Dev == r.dev` **且** `OriginalDesc(d) == r.desc`

`OriginalDesc(d)`：若 `orig`（`original-names.json`）有這個 ID，用原始名稱，否則用 `d.Desc`。這樣使用者改過名的裝置，在驅動更新後以預設名稱重新出現時也能比對成功。

依名稱比對成功、但 ID 不同時，把 `r.id` **更新成新的 ID** 並存檔，之後就用新 ID 直接比對。

> 名稱比對的副作用：兩個名稱完全相同的裝置（例如同一款耳機插在兩個 USB 孔）會一起被隱藏。這通常就是使用者想要的，可接受。

### 4.4 執行時機

實作放在 `src/Features/HiddenDevices/`：`HiddenDevices.cs`（設定讀寫、比對、`Enforce`）與 `MainWindow.HiddenDevices.cs`（`EnforceHidden()` 呼叫時機與提示）：

| 時機 | 做法 |
|---|---|
| **程式啟動**（包含 `--tray`） | 在 `MainWindow` 建構子結束前、或 `Program.Main` 呼叫 `app.Run()` 之前，先載入裝置清單並執行一次 |
| **裝置變化通知** | `ScheduleDeviceRefresh` 的計時器觸發時，`RefreshAll()` 之前先執行一次（已有 0.4 秒合併連發通知的機制） |
| **從右下角叫回視窗** | `ShowFromTray()` 會呼叫 `RefreshAll()`，順便執行 |

⚠️ **重要**：目前第一次載入裝置清單是在 `ContentRendered`（`src/UI/MainWindow.cs`），而開機以 `--tray` 啟動時視窗不顯示、`ContentRendered` 不會觸發。所以**不能**依賴它，啟動時要另外主動執行一次。

`EnforceHiddenDevices()` 流程：
1. `autoHide == false` → 直接結束
2. 用 `Audio.Load(0)`、`Audio.Load(1)` 取得目前清單
3. 對每個 `Enabled && !Unplugged` 的裝置，若符合記憶清單 → `Audio.SetVisibility(id, false)`
4. 若有任何裝置被隱藏，**不要**在這裡重畫；呼叫端之後的 `RefreshAll()` 會反映最新狀態
5. 有隱藏動作且視窗可見時，用 `SetStatus` 提示「已自動隱藏：Sonar - Chat 等 2 個裝置」

### 4.5 防止拉鋸

自動隱藏本身會觸發裝置變化通知；若另一個程式（例如 Sonar）也持續把裝置打開，可能形成無限循環。

- 每個裝置 ID 記錄最近的自動隱藏時間
- **同一個裝置 60 秒內最多自動隱藏 3 次**，超過就暫停對它的自動隱藏，直到下次程式啟動
- 被暫停時寫一次狀態提示（視窗可見時）：「Sonar - Chat 一直被其他程式重新開啟，已暫停自動隱藏」

### 4.6 使用者操作的對應

修改 `MainWindow.Toggle()`（`src/UI/MainWindow.cs`）：

- 隱藏成功後 → `HiddenDevices.Remember(d, originalDesc)`
- 顯示成功後 → `HiddenDevices.Forget(d)`：移除 ID 相同、或 flow + dev + 原始名稱相同的項目

右下角選單（`src/Features/Tray/MainWindow.Tray.cs`）新增勾選項目「**自動維持隱藏**」，對應 `autoHide`。

### 4.7 首次啟用（遷移）

升級到這個版本、第一次執行時 `settings.json` 不存在：

- 把**目前所有隱藏中的裝置**寫進記憶清單，作為初始值
- 好處：使用者不用重新隱藏一次
- 已知限制：這次已被 Sonar 重新打開的裝置（`Sonar - Chat` 等）不在初始清單裡，需要使用者再隱藏一次

### 4.8 介面文字

在 `src/Core/Lang.cs` 新增（中 / 英）：

| key | 繁體中文 | English |
|---|---|---|
| `tray.autoHide` | 自動維持隱藏 | Keep hidden devices hidden |
| `st.autoHidden` | 已自動隱藏：{0} | Hidden again automatically: {0} |
| `st.autoHiddenMany` | 已自動隱藏：{0} 等 {1} 個裝置 | Hidden again automatically: {0} and {1} more |
| `st.autoHidePaused` | 「{0}」一直被其他程式重新開啟，已暫停自動隱藏 | "{0}" keeps being re-enabled by another app. Auto-hide paused for it |

## 5. 要修改的檔案

| 檔案 | 變更 |
|---|---|
| `src/Features/Devices/HiddenDevices.cs`（新） | 設定讀寫、比對、`Remember` / `Forget` / `Enforce`、拉鋸保護 |
| `src/UI/MainWindow.cs` | 建構子在啟動時執行一次；`Toggle()` 呼叫 Remember / Forget；`ScheduleDeviceRefresh` 計時器裡先 Enforce 再 `RefreshAll()` |
| `src/Features/Tray/MainWindow.Tray.cs` | 右下角選單「自動維持隱藏」 |
| `src/Core/Lang.cs` | 新增 §4.8 的文字 |
| `README.md`、`README.zh-TW.md` | 功能說明、常見問題「設定會保留嗎？」補充 |
| `docs/DEVELOPMENT.md` | 檔案結構、技術原理（設定檔位置與比對規則） |
| `TODO.md` | 完成後打勾 |

## 6. 測試計畫

沒有自動化測試，以實機驗證（參考過去用的「外部程式修改設定」腳本：另開一個 PowerShell，用 `IPolicyConfig::SetEndpointVisibility` 改裝置狀態，再用 `PrintWindow` 截圖確認）：

| # | 情境 | 預期 |
|---|---|---|
| 1 | 在 DJ Mochi 隱藏 A → 檢查 `settings.json` | A 被記錄，`desc` 為原始名稱 |
| 2 | 用外部程式把 A 改回顯示 | 約 0.4 秒內 A 被自動隱藏，視窗顯示提示 |
| 3 | 在 DJ Mochi 按「顯示」A → 再用外部程式隱藏、顯示 | A 不會被自動隱藏 |
| 4 | 以 `--tray` 啟動前，先用外部程式把記住的裝置打開 | 啟動後（不開視窗）該裝置已被隱藏 |
| 5 | 外部程式每秒把 A 打開一次，持續 30 秒 | 最多自動隱藏 3 次後暫停，程式不會卡住或閃退 |
| 6 | 改過名的裝置被隱藏 → 手動把 `settings.json` 的 `id` 改成不存在的值 → 外部程式把它打開 | 依名稱比對成功並隱藏，`id` 被更新 |
| 7 | 右下角選單關閉「自動維持隱藏」→ 重複情境 2 | 不會自動隱藏 |
| 8 | 刪除 `settings.json` 後啟動 | 以目前隱藏中的裝置建立初始清單 |

測試結束後，記得把測試時動過的裝置恢復原狀。

## 7. 風險

| 風險 | 對策 |
|---|---|
| 與 Sonar 等程式拉鋸 | §4.5 次數上限 |
| 名稱比對誤傷（同名不同裝置） | 加上 `flow` 與 `dev` 一起比對；誤傷時使用者按「顯示」即可移除 |
| 隱藏了正在使用的預設裝置 | 使用者當初隱藏時已經二次確認過；自動隱藏時若該裝置剛好是預設，Windows 會自動改用其他裝置。可考慮在這種情況下**跳過**並提示（見 §8 待決定事項 3） |
| `settings.json` 損毀 | 讀取失敗時當作空清單，不覆寫原檔（改名為 `.bak` 後再建新檔） |

## 8. 待決定事項

1. **互動方式**：全自動（建議）或每列加「📌 固定隱藏」開關？
2. **使用者在 Windows 設定裡手動改回「允許」時**：DJ Mochi 無法分辨是使用者還是其他程式改的，目前設計會再隱藏回去。若使用者覺得困擾，可以用 §4.6 的右下角開關整個關掉。是否需要更細的處理？
3. **自動隱藏的對象剛好是目前的預設裝置時**：照樣隱藏，還是跳過並提示？建議**跳過並提示**，避免突然沒聲音。

### 決定（2026-11-01）

1. **互動方式**：全自動。按「隱藏」記住、按「顯示」不再處理（記錄標為 `releasedBy: "user"` 與時間 `released`，保留紀錄）
2. **在 DJ Mochi 以外被打開**：視為使用者的意思，**不再自動隱藏**，記錄標為 `releasedBy: "other"` 與時間
   - 為了不讓開機時的 Sonar 被誤當成使用者：**啟動後 3 分鐘內**（`StartupGrace`）被打開的一律再隱藏，過了才套用此規則
   - 判斷「被打開」= 這次執行期間曾看到它是隱藏的、現在變成可見。以新 ID 出現（驅動更新）的裝置不算，照樣隱藏
3. **對象是預設裝置**：不自動隱藏，跳**右下角通知**，點通知才隱藏；沒點就這次執行期間維持顯示

## 9. 實作順序建議

1. `HiddenDevices.cs`：資料模型、讀寫、比對（先不接介面）
2. `Toggle()` 接上 Remember / Forget，確認 `settings.json` 內容正確（測試 #1）
3. 裝置變化時執行 Enforce（測試 #2、#3、#6）
4. 啟動時執行（測試 #4、#8）
5. 拉鋸保護（測試 #5）
6. 右下角開關與介面文字（測試 #7）
7. 文件更新、commit（`feat: 記住隱藏的裝置，被重新啟用時自動再隱藏`）、合併發布 v1.5.0
