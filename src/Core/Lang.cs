// Lang.cs — 介面文字（繁體中文 / English）與語言設定

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace DJMochi
{
    enum LangMode { Auto, Chinese, English }

    static class Lang
    {
        const string SettingsKey = @"Software\DJMochi";
        const string ValueName = "Language";

        static LangMode mode = LangMode.Auto;
        public static LangMode Mode { get { return mode; } }
        public static bool IsEnglish { get; private set; }

        // 自動：Windows 顯示語言是中文就用繁體中文，其他語言用英文
        static bool WindowsIsChinese
        {
            get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"; }
        }

        public static void Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    var v = k == null ? null : k.GetValue(ValueName) as string;
                    mode = v == "zh" ? LangMode.Chinese : v == "en" ? LangMode.English : LangMode.Auto;
                }
            }
            catch { mode = LangMode.Auto; }
            Apply();
        }

        public static void Set(LangMode m)
        {
            mode = m;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (m == LangMode.Auto) k.DeleteValue(ValueName, false);
                    else k.SetValue(ValueName, m == LangMode.Chinese ? "zh" : "en");
                }
            }
            catch { }
            Apply();
        }

        static void Apply()
        {
            IsEnglish = mode == LangMode.English || (mode == LangMode.Auto && !WindowsIsChinese);
        }

        // 取得目前語言的文字；有參數時套用 string.Format。找不到的 key 直接回傳 key，方便發現漏翻
        public static string Tr(string key, params object[] args)
        {
            string[] v;
            if (!Table.TryGetValue(key, out v)) return key;
            string s = IsEnglish ? v[1] : v[0];
            return args.Length > 0 ? string.Format(s, args) : s;
        }

        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // 標題列
            { "cap.min",            new[] { "縮小到右下角", "Minimize to notification area" } },
            { "cap.max",            new[] { "最大化", "Maximize" } },
            { "cap.restore",        new[] { "還原", "Restore" } },
            { "cap.close",          new[] { "關閉", "Close" } },

            // 上方按鈕
            { "tab.playback",       new[] { "播放", "Playback" } },
            { "tab.recording",      new[] { "錄音", "Recording" } },
            { "startup.on",         new[] { "開機啟動：開", "Startup: On" } },
            { "startup.off",        new[] { "開機啟動：關", "Startup: Off" } },
            { "startup.tip",        new[] { "開機時自動啟動，並直接縮在右下角", "Start with Windows, minimized to the notification area" } },
            { "footer",             new[] { "拖曳或滾輪調整音量　　鉛筆：重新命名（Windows 也會顯示）　　插拔耳機、在 Windows 改設定會自動更新",
                                            "Drag or scroll to adjust volume    Pencil: rename (also shown in Windows)    Device changes update automatically" } },

            // 裝置列表
            { "sec.shown",          new[] { "顯示中", "Visible" } },
            { "sec.hidden",         new[] { "已隱藏", "Hidden" } },
            { "badge.default",      new[] { "預設", "Default" } },
            { "badge.virtual",      new[] { "虛擬", "Virtual" } },
            { "badge.unplugged",    new[] { "未接上", "Unplugged" } },
            { "rename",             new[] { "重新命名", "Rename" } },
            { "rename.restore",     new[] { "還原成「{0}」", "Restore to \"{0}\"" } },
            { "rename.tip",         new[] { "Enter 儲存、Esc 取消；清空後儲存 = 還原原始名稱", "Enter to save, Esc to cancel. Save an empty name to restore the original." } },
            { "setDefault",         new[] { "設為預設", "Set default" } },
            { "hide",               new[] { "隱藏", "Hide" } },
            { "show",               new[] { "顯示", "Show" } },
            { "cancel",             new[] { "取消", "Cancel" } },
            { "confirmHide",        new[] { "確認隱藏？", "Hide anyway?" } },
            { "mute",               new[] { "靜音", "Mute" } },
            { "unmute",             new[] { "取消靜音", "Unmute" } },

            // 狀態訊息
            { "st.keepOne",         new[] { "播放裝置至少要保留一個顯示。", "At least one playback device must stay visible." } },
            { "st.defaultWarn",     new[] { "「{0}」是預設裝置，隱藏後 Windows 會改用其他裝置。", "\"{0}\" is the default device. Windows will switch to another device if you hide it." } },
            { "st.shown",           new[] { "已顯示：{0}", "Shown: {0}" } },
            { "st.hidden",          new[] { "已隱藏：{0}", "Hidden: {0}" } },
            { "st.defaultSet",      new[] { "已設為預設：{0}", "Default device: {0}" } },
            { "st.renamed",         new[] { "已重新命名：{0} → {1}", "Renamed: {0} → {1}" } },
            { "st.startupAdded",    new[] { "已加入開機自動啟動（工作管理員 →「啟動應用程式」）", "Added to startup apps (Task Manager → Startup apps)" } },
            { "st.startupRemoved",  new[] { "已取消開機自動啟動", "Removed from startup apps" } },
            { "st.language",        new[] { "介面語言：繁體中文", "Language: English" } },

            // 錯誤
            { "err.title",          new[] { "DJ Mochi - 發生錯誤", "DJ Mochi - Error" } },
            { "err.visibility",     new[] { "{0} 設定失敗 (HRESULT 0x{1:X8})", "Failed to update {0} (HRESULT 0x{1:X8})" } },
            { "err.default",        new[] { "設為預設失敗 (HRESULT 0x{0:X8})", "Failed to set default device (HRESULT 0x{0:X8})" } },
            { "err.rename",         new[] { "重新命名失敗 (HRESULT 0x{0:X8})", "Rename failed (HRESULT 0x{0:X8})" } },
            { "err.saveNames",      new[] { "無法儲存原始名稱備份：{0}", "Could not save the original name backup: {0}" } },
            { "err.startup",        new[] { "設定開機啟動失敗：{0}", "Failed to change the startup setting: {0}" } },

            // 右下角
            { "tray.open",          new[] { "開啟 DJ Mochi", "Open DJ Mochi" } },
            { "tray.default",       new[] { "預設播放裝置", "Default playback device" } },
            { "tray.switched",      new[] { "已切換到：{0}", "Switched to: {0}" } },
            { "tray.startup",       new[] { "開機時自動啟動", "Start with Windows" } },
            { "tray.language",      new[] { "語言 / Language", "Language / 語言" } },
            { "tray.exit",          new[] { "結束", "Exit" } },
            { "tray.minimized",     new[] { "縮小到右下角了，點圖示就能叫回來 🍡", "DJ Mochi is in the notification area. Click the icon to bring it back 🍡" } },
            { "lang.auto",          new[] { "自動（跟隨 Windows）", "Auto (follow Windows)" } },

            // AirPods
            { "pods.left",          new[] { "左", "L" } },
            { "pods.right",         new[] { "右", "R" } },
            { "pods.case",          new[] { "盒", "Case" } },
            { "pods.agoTray",       new[] { "（{0:0} 分鐘前）", " ({0:0} min ago)" } },
            { "pods.ago",           new[] { "{0:0} 分鐘前", "{0:0} min ago" } },
            { "pods.precision",     new[] { "AirPods 廣播的電量精度為 10%（{0}）", "AirPods report battery in 10% steps ({0})" } },
            { "pods.lowTitle",      new[] { "AirPods 電量低", "AirPods battery low" } },
        };
    }
}
