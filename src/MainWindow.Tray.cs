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
            var open = new WinForms.ToolStripMenuItem("開啟 DJ Mochi");
            open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
            open.Click += delegate { ShowFromTray(); };
            menu.Items.Add(open);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            // 不開視窗就能切換預設播放裝置
            var sub = new WinForms.ToolStripMenuItem("預設播放裝置");
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
                        tray.ShowBalloonTip(1500, "DJ Mochi", "已切換到：" + dev.Desc, WinForms.ToolTipIcon.None);
                    }
                };
                sub.DropDownItems.Add(item);
            }
            if (sub.DropDownItems.Count == 0) sub.Enabled = false;
            menu.Items.Add(sub);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var auto = new WinForms.ToolStripMenuItem("開機時自動啟動");
            auto.Checked = Startup.IsEnabled();
            auto.Click += delegate { ToggleStartup(); };
            menu.Items.Add(auto);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var exit = new WinForms.ToolStripMenuItem("結束");
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
        }

        void ToggleStartup()
        {
            try
            {
                bool on = !Startup.IsEnabled();
                Startup.SetEnabled(on);
                SetStatus(on ? "已加入開機自動啟動（工作管理員 →「啟動應用程式」）" : "已取消開機自動啟動");
            }
            catch (Exception ex) { SetStatus("設定開機啟動失敗：" + ex.Message); }
            UpdateStartupBtn();
            if (IsVisible) Render();
        }

        void UpdateStartupBtn()
        {
            bool on = false;
            try { on = Startup.IsEnabled(); } catch { }
            startupBtn.Content = IconLabel(GPower, on ? "開機啟動：開" : "開機啟動：關", 17);
            startupBtn.Background = B(on ? "#107C10" : "#1B1E25");
            startupBtn.Foreground = B(on ? "#FFFFFF" : "#C9CDD4");
            startupBtn.Margin = new Thickness(0, 0, 12, 0);
        }

        void HideToTray()
        {
            Hide();
            if (!trayTipShown && tray != null)
            {
                trayTipShown = true;
                tray.ShowBalloonTip(2500, "DJ Mochi", "縮小到右下角了，點圖示就能叫回來 🍡", WinForms.ToolTipIcon.None);
            }
        }

        // 從右下角叫回（也供防多開時呼叫）
        public void ShowFromTray()
        {
            Show();
            WindowState = restoreState;
            Activate();
            Topmost = true; Topmost = false;   // 確保跑到最前面
            RefreshAll();                      // 縮小期間可能插拔過裝置
        }
    }
}
