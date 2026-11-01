# TODO

DJ Mochi 的待辦與想法清單。做完的項目打勾，並在後面註明版本。

## 🔜 下一步

### 記住隱藏的裝置
重新開機後，SteelSeries Sonar 會重新啟用自己的虛擬裝置、驅動更新會讓裝置以新 ID 出現，原本的隱藏設定因此失效。DJ Mochi 要記住使用者隱藏了哪些裝置，被重新啟用時自動再隱藏。

- [ ] 依設計文件實作：[docs/sdd/persistent-hidden-devices/SDD.md](docs/sdd/persistent-hidden-devices/SDD.md)
- [ ] 先決定 SDD §8 的待決定事項（互動方式、預設裝置被自動隱藏時的處理）

### Arctis Nova 7P 連線狀態與電量
Arctis 透過 2.4 GHz 接收器連接，Windows 只看得到接收器，耳機關機時音訊裝置仍顯示「啟用中」，所以要直接問接收器。

- [ ] 寫測試工具：透過接收器的廠商自訂 HID 介面（`VID_1038&PID_22A7`，`MI_03` / `MI_05`）送出查詢指令並讀取回應
- [ ] 實測：開 / 關耳機、充電中，確認回應中的連線狀態與電量欄位
- [ ] 整合：Arctis 那一列顯示「已連線 80%」或「耳機未開機」；未開機時在「設為預設」旁提示
- 注意事項
  - 通訊格式參考 [HeadsetControl](https://github.com/Sapd/HeadsetControl) 整理的 Arctis Nova 7 系列格式，但它是 GPL-3.0，**只參考格式、不複製程式碼**
  - Nova 7P（PlayStation 版）的格式可能與 Nova 7 不同，以實測為準
  - 確認與 SteelSeries GG 同時執行時不會互相干擾
  - 只用 Windows 內建的 HID API（`hid.dll`、`CreateFile` / `WriteFile` / `ReadFile`），不加外部套件

## 🎛️ 功能

- [ ] **右下角快速面板**：左鍵點圖示，在圖示上方跳出小卡片，列出顯示中的裝置與音量，點一下就切換；完整視窗留給設定
- [ ] **全域快捷鍵**：例如 `Ctrl + Alt + M` 在常用裝置間輪流切換，遊戲中也能用
- [ ] **情境模式**：一鍵套用一組設定（例如「遊戲」= Arctis + SoloCast 麥克風；「通話」= AirPods + 指定音量）
- [ ] **即時音量跳動條**：每個裝置旁顯示目前輸出的音量波形（`IAudioMeterInformation`）
- [ ] **每個程式指定輸出裝置**：例如 Discord 走 AirPods、遊戲走喇叭（需要未公開的 Windows 介面，難度高）
- [ ] AirPods 低電量的定時檢查：縮在右下角時也能提醒（目前決定**先不做**，只在使用中監聽）

## 🍡 讓 Mochi 更好玩

- [ ] **Mochi 隨狀態變表情**：有聲音時跟著彈跳、靜音時睡著、低電量冒汗、切換成功時眨眼（視窗大頭與右下角圖示）
- [ ] **切換音效**：切換預設裝置時，從新裝置播一聲短短的「刷碟」音效，順便確認聲音真的換過去了
- [ ] **Mochi 的台詞**：狀態訊息改成 Mochi 的口吻，例如「切到 AirPods 囉～」「右耳快沒電了！」
- [ ] 列表展開 / 收合、切換預設時的動畫
- [ ] 第一次使用的引導
- [ ] 季節造型彩蛋（櫻花、抹茶、聖誕 Mochi）

## 📦 發布

- [ ] **檢查更新**：開啟時查詢 GitHub 最新 Release，有新版就在角落提示
- [ ] 上架 winget（`winget install DJMochi`）
- [ ] 程式碼簽章，解決 SmartScreen 警告（可申請 SignPath 的開源免費簽章）
- [ ] 改用 `gh` 開 PR 合併，保留 PR 紀錄

## 🧹 品質

- [ ] AirPods 廣播解析的自動測試：把實機抓到的封包做成測試案例，放進 CI
- [ ] `docs/DEVELOPMENT.md` 英文版
- [ ] 實測藍牙關閉 / 重新開啟時，AirPods 監聽能自動恢復（code review M1，尚未實機驗證）

## ✅ 已完成

- [x] 顯示 / 隱藏、設為預設、音量 / 靜音、重新命名（v1.0.0）
- [x] 縮到右下角、開機自動啟動（v1.1.0）
- [x] AirPods 電量（v1.2.0）
- [x] 英文介面、程式拆檔與按功能分資料夾、README 中英版（v1.3.0）
- [x] 自動偵測裝置與音量變化，移除 F5（v1.4.0）
