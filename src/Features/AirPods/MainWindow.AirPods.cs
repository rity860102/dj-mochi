// MainWindow.AirPods.cs — AirPods 電量：只在使用中監聽、篩選、顯示

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
        // ---------- AirPods 電量 ----------

        // 使用中才監聽：視窗開著，且預設播放裝置是 AirPods（被隱藏的裝置不會是預設）
        static bool IsAirPods(Device d)
        {
            return d.Dev != null && d.Dev.IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool ShouldListenPods()
        {
            return IsVisible && cache[0].Any(d => d.IsDefault && d.Enabled && IsAirPods(d));
        }

        void UpdatePodsListening()
        {
            if (ShouldListenPods()) StartPods(); else StopPods();
        }

        // 停止監聽，也不重試
        void StopPods()
        {
            if (podsRetry != null) podsRetry.Stop();
            if (pods != null) { pods.Dispose(); pods = null; }
            UpdateTrayText();
        }

        void StartPods()
        {
            if (pods != null || !ShouldListenPods()) return;
            podsModels = AirPodsParser.PairedModels();   // 每次重讀：執行中才配對的新 AirPods 也能被接受
            var m = new AirPodsMonitor();
            pods = m;
            try
            {
                // 事件在背景執行緒進來，切回 UI 執行緒；並確認是「目前這個」monitor，丟掉已停止的舊 monitor 殘留事件
                m.Updated += delegate(AirPodsStatus s)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { if (m == pods) OnPods(s); }));
                };
                m.Aborted += delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate { if (m == pods) RetryPodsLater(); }));
                };
                m.Start();
            }
            catch { RetryPodsLater(); }   // 沒有藍牙或不支援 BLE
        }

        // 監聽中斷（例如藍牙被關掉）：30 秒後重試，只在仍然「使用中」時
        void RetryPodsLater()
        {
            if (pods != null) { pods.Dispose(); pods = null; }
            if (!ShouldListenPods()) return;
            if (podsRetry == null)
            {
                podsRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                podsRetry.Tick += delegate { podsRetry.Stop(); StartPods(); };
            }
            podsRetry.Stop(); podsRetry.Start();
        }

        void OnPods(AirPodsStatus s)
        {
            if (s.Rssi < PodsMinRssi) return;
            // 只採用跟這台電腦已配對 AirPods 同型號的廣播（排除附近別人的不同型號 AirPods）
            if (podsModels != null && podsModels.Count > 0 && !podsModels.Contains(s.Model)) return;
            var cur = podsStatus;
            // 換到另一個位址的條件（避免左右耳各自廣播、訊號強弱交替時來回跳）：
            //   原本的位址已經 5 秒沒消息（位址輪換、或換耳機），或新的訊號強 10 dB 以上（更近的另一副）
            bool take = cur == null || s.Address == cur.Address ||
                        (DateTime.Now - podsSeen).TotalSeconds > 5 || s.Rssi > cur.Rssi + 10;
            if (!take) return;

            bool changed = cur == null || cur.Left != s.Left || cur.Right != s.Right || cur.Case != s.Case ||
                           cur.ChargingLeft != s.ChargingLeft || cur.ChargingRight != s.ChargingRight || cur.ChargingCase != s.ChargingCase;
            podsStatus = s;
            podsSeen = DateTime.Now;
            if (!changed) return;   // 廣播很頻繁，數值沒變就不重畫

            UpdateTrayText();
            CheckPodsLow(s);
            RenderPodsThrottled();
        }

        // 電量變化引起的重畫最多每秒一次（Render 會重建整個列表）
        void RenderPodsThrottled()
        {
            if (podsRender == null)
            {
                podsRender = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                podsRender.Tick += delegate
                {
                    podsRender.Stop();
                    if (IsVisible && editBox == null) Render();
                };
            }
            if (!podsRender.IsEnabled) podsRender.Start();
        }

        static string PodsSummary(AirPodsStatus s)
        {
            var parts = new List<string>();
            if (s.Left >= 0)  parts.Add(Lang.Tr("pods.left") + " " + s.Left + "%" + (s.ChargingLeft ? "⚡" : ""));
            if (s.Right >= 0) parts.Add(Lang.Tr("pods.right") + " " + s.Right + "%" + (s.ChargingRight ? "⚡" : ""));
            if (s.Case >= 0)  parts.Add(Lang.Tr("pods.case") + " " + s.Case + "%" + (s.ChargingCase ? "⚡" : ""));
            return string.Join("  ", parts);
        }

        // 右下角提示文字。沒在監聽時（縮在右下角）顯示的是最後一次讀到的電量，加上「N 分鐘前」
        void UpdateTrayText()
        {
            if (tray == null) return;
            string text = "DJ Mochi";
            if (podsStatus != null)
            {
                string sum = PodsSummary(podsStatus);
                if (sum.Length > 0)
                {
                    text += "\n" + podsStatus.ModelName + "\n" + sum;
                    double min = (DateTime.Now - podsSeen).TotalMinutes;
                    if (pods == null && min >= 1) text += Lang.Tr("pods.agoTray", min);
                }
            }
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;   // NotifyIcon.Text 上限 63 字
        }

        void CheckPodsLow(AirPodsStatus s)
        {
            int low = int.MaxValue;
            if (s.Left >= 0 && !s.ChargingLeft) low = Math.Min(low, s.Left);
            if (s.Right >= 0 && !s.ChargingRight) low = Math.Min(low, s.Right);
            if (low == int.MaxValue) return;
            if (low <= PodsLowLevel && !podsLowWarned)
            {
                podsLowWarned = true;
                if (tray != null) tray.ShowBalloonTip(4000, Lang.Tr("pods.lowTitle"), s.ModelName + ": " + PodsSummary(s), WinForms.ToolTipIcon.Warning);
            }
            else if (low > PodsLowLevel + 10) podsLowWarned = false;   // 充回 20% 以上才重新提醒
        }

        // AirPods 那一列名稱下方的電量顯示
        UIElement PodsBattery()
        {
            var s = podsStatus;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            bool stale = (DateTime.Now - podsSeen).TotalMinutes > 3;
            Action<string, int, bool> add = delegate(string label, int v, bool charging)
            {
                if (v < 0) return;
                string color = stale ? "#6B7079" : v >= 50 ? "#3FD13F" : v >= 20 ? "#F2C14E" : "#E05A5A";
                var b = new Border
                {
                    CornerRadius = new CornerRadius(8), Background = B("#14161B"), Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 6, 0), BorderBrush = B(color), BorderThickness = new Thickness(1)
                };
                var t = T(label + " " + v + "%" + (charging ? " ⚡" : ""), 12, color, true);
                b.Child = t;
                sp.Children.Add(b);
            };
            add(Lang.Tr("pods.left"), s.Left, s.ChargingLeft);
            add(Lang.Tr("pods.right"), s.Right, s.ChargingRight);
            add(Lang.Tr("pods.case"), s.Case, s.ChargingCase);
            if (sp.Children.Count == 0) return null;
            if (stale)
            {
                var ago = T(Lang.Tr("pods.ago", (DateTime.Now - podsSeen).TotalMinutes), 12, "#6B7079");
                sp.Children.Add(ago);
            }
            sp.ToolTip = Lang.Tr("pods.precision", s.ModelName);
            return sp;
        }
    }
}
