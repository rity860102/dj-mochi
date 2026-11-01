// MainWindow.Tray.cs — 右下角通知區域、縮小 / 叫回、開機啟動按鈕

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;     // 只用來做右下角通知區域圖示（NotifyIcon）
using Drawing = System.Drawing;

namespace DJMochi
{
    partial class MainWindow
    {
        // ---------- 右下角通知區域 ----------

        void InitTray()
        {
            tray = new WinForms.NotifyIcon { Text = "DJ Mochi", Icon = LoadTrayIcon(), Visible = true };
            tray.MouseMove += delegate { UpdateTrayText(); };   // 滑過時更新「N 分鐘前」
            tray.MouseClick += delegate(object s, WinForms.MouseEventArgs e)
            {
                if (e.Button == WinForms.MouseButtons.Left) ShowFromTray();
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                BuildTrayMenu(menu);
                e.Cancel = false;   // 選單一開始是空的，預設會被取消
            };
            tray.ContextMenuStrip = menu;
        }

        static Drawing.Icon LoadTrayIcon()
        {
            try
            {
                var s = typeof(MainWindow).Assembly.GetManifestResourceStream("DJMochi.mochi.ico");
                if (s != null) using (s) return new Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
            }
            catch { }
            return Drawing.SystemIcons.Application;
        }

        void BuildTrayMenu(WinForms.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            var open = new WinForms.ToolStripMenuItem(Lang.Tr("tray.open"));
            open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
            open.Click += delegate { ShowFromTray(); };
            menu.Items.Add(open);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            // 不開視窗就能切換預設播放裝置
            var sub = new WinForms.ToolStripMenuItem(Lang.Tr("tray.default"));
            foreach (var d in Audio.Load(0).Where(x => x.Enabled && !x.Unplugged).OrderBy(x => x.Desc))
            {
                var item = new WinForms.ToolStripMenuItem(string.IsNullOrEmpty(d.Dev) ? d.Desc : d.Desc + "（" + d.Dev + "）");
                item.Checked = d.IsDefault;
                var dev = d;
                item.Click += delegate
                {
                    if (dev.IsDefault) return;
                    if (Audio.SetDefault(dev.Id) == 0)
                    {
                        foreach (var x in cache[0]) x.IsDefault = string.Equals(x.Id, dev.Id, StringComparison.OrdinalIgnoreCase);
                        if (IsVisible) Render();
                        ShowTip(1500, "DJ Mochi", Lang.Tr("tray.switched", dev.Desc), WinForms.ToolTipIcon.None);
                    }
                };
                sub.DropDownItems.Add(item);
            }
            if (sub.DropDownItems.Count == 0) sub.Enabled = false;
            menu.Items.Add(sub);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var auto = new WinForms.ToolStripMenuItem(Lang.Tr("tray.startup"));
            auto.Checked = Startup.IsEnabled();
            auto.Click += delegate { ToggleStartup(); };
            menu.Items.Add(auto);

            var keep = new WinForms.ToolStripMenuItem(Lang.Tr("tray.autoHide"));
            keep.Checked = hiddenDevices != null && hiddenDevices.AutoHide;
            keep.Click += delegate { ToggleAutoHide(); };
            menu.Items.Add(keep);

            // 語言：自動 / 繁體中文 / English（選了立刻生效）
            var lang = new WinForms.ToolStripMenuItem(Lang.Tr("tray.language"));
            foreach (var opt in new[] {
                new KeyValuePair<LangMode, string>(LangMode.Auto, Lang.Tr("lang.auto")),
                new KeyValuePair<LangMode, string>(LangMode.Chinese, "繁體中文"),
                new KeyValuePair<LangMode, string>(LangMode.English, "English") })
            {
                var item = new WinForms.ToolStripMenuItem(opt.Value) { Checked = Lang.Mode == opt.Key };
                var mode = opt.Key;
                item.Click += delegate { SwitchLanguage(mode); };
                lang.DropDownItems.Add(item);
            }
            menu.Items.Add(lang);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var exit = new WinForms.ToolStripMenuItem(Lang.Tr("tray.exit"));
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
        }

        // 切換語言：重建整個畫面（標題列、按鈕、列表都會換成新語言）
        void SwitchLanguage(LangMode mode)
        {
            Lang.Set(mode);
            FontFamily = TextFont;
            Content = BuildUi();
            UpdateMaxState();
            UpdateTrayText();
            SetStatus(Lang.Tr("st.language"));
            Render();
        }

        void ToggleStartup()
        {
            try
            {
                bool on = !Startup.IsEnabled();
                Startup.SetEnabled(on);
                SetStatus(Lang.Tr(on ? "st.startupAdded" : "st.startupRemoved"));
            }
            catch (Exception ex) { SetStatus(Lang.Tr("err.startup", ex.Message)); }
            UpdateStartupBtn();
            if (IsVisible) Render();
        }

        void UpdateStartupBtn()
        {
            bool on = false;
            try { on = Startup.IsEnabled(); } catch { }
            startupBtn.Content = IconLabel(GPower, Lang.Tr(on ? "startup.on" : "startup.off"), 17);
            startupBtn.Background = B(on ? "#107C10" : "#1B1E25");
            startupBtn.Foreground = B(on ? "#FFFFFF" : "#C9CDD4");
            startupBtn.Margin = new Thickness(0);   // 最右邊的按鈕（原本的「重新整理」已改為自動偵測）
        }

        void HideToTray()
        {
            Hide();
            if (!trayTipShown && tray != null)
            {
                trayTipShown = true;
                ShowTip(2500, "DJ Mochi", Lang.Tr("tray.minimized"), WinForms.ToolTipIcon.None);
            }
        }

        // 從右下角叫回（也供防多開時呼叫）
        public void ShowFromTray()
        {
            Show();
            WindowState = restoreState;
            Activate();
            Topmost = true; Topmost = false;   // 確保跑到最前面
            EnforceHidden();
            RefreshAll();                      // 縮小期間可能插拔過裝置
        }
    }
}
