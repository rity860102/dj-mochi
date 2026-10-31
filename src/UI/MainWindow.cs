// MainWindow.cs — 主視窗：版面、裝置列表、顯示 / 隱藏、設為預設、重新命名

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
    // ===================== 主視窗 =====================

    partial class MainWindow : Window
    {
        static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily ChineseFont = new FontFamily("Microsoft JhengHei UI");
        static readonly FontFamily EnglishFont = new FontFamily("Segoe UI");
        static FontFamily TextFont { get { return Lang.IsEnglish ? EnglishFont : ChineseFont; } }

        const string GVolume = "", GMute = "", GHeadphone = "", GSpeaker = "", GMonitor = "",
                     GMic = "", GMicOff = "", GGame = "", GRefresh = "", GHidden = "",
                     GShow = "", GRight = "", GDown = "", GEdit = "", GUndo = "",
                     GMin = "", GMax = "", GRestore = "", GClose = "";




        // DJ Mochi 圖示（build.ps1 以 /resource 內嵌 src\Assets\mochi.png）
        static readonly ImageSource Mochi = LoadMochi();
        static ImageSource LoadMochi()
        {
            try
            {
                var s = typeof(MainWindow).Assembly.GetManifestResourceStream("DJMochi.mochi.png");
                if (s == null) return null;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.StreamSource = s; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        static UIElement Logo(double size, string fallbackColor)
        {
            if (Mochi == null) return GlyphText(GVolume, size * 0.8, fallbackColor);
            var img = new Image { Source = Mochi, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        int tab = 0;
        readonly List<Device>[] cache = { new List<Device>(), new List<Device>() };
        readonly bool[] showHidden = new bool[2];             // 已隱藏：預設收合
        readonly bool[] showShown = new bool[] { true, true }; // 顯示中：預設展開
        TextBox editBox;
        bool statusSticky;
        string pendingHideId;          // 等待二次確認隱藏的預設裝置

        Button startupBtn;
        const string GPower = "";

        // AirPods 電量（BLE 廣播）
        AirPodsMonitor pods;
        AirPodsStatus podsStatus;
        DateTime podsSeen;
        bool podsLowWarned;
        HashSet<ushort> podsModels;         // 已配對的 AirPods 型號（只採用這些型號的廣播）
        const int PodsMinRssi = -75;        // 太遠的（可能是別人的）不採用
        const int PodsLowLevel = 10;        // 低於等於這個電量就提醒
        DispatcherTimer podsRetry;          // 監聽中斷後的重試
        DispatcherTimer podsRender;         // 電量變化時的重畫節流

        // 右下角通知區域
        WinForms.NotifyIcon tray;
        bool trayTipShown;
        WindowState restoreState = WindowState.Normal;
        Dictionary<string, string> orig;
        readonly string namesFile;

        Grid outer;
        StackPanel rows;
        TextBlock status;
        Button tabR, tabC, maxBtn;

        public MainWindow()
        {
            namesFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "original-names.json");
            orig = LoadNames();

            Title = "DJ Mochi";
            Width = 1040; Height = 760; MinWidth = 760; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = B("#0E1014"); Foreground = Brushes.White; FontFamily = TextFont;
            UseLayoutRounding = true;
            if (Mochi != null) Icon = Mochi;

            // 自訂標題列（保留拖曳、縮放、Snap）
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(StylesXaml));
            Content = BuildUi();

            PreviewKeyDown += OnKey;
            StateChanged += delegate
            {
                // 任何方式最小化（按鈕、工作列、Win+↓）都縮到右下角
                if (WindowState == WindowState.Minimized) { HideToTray(); return; }
                restoreState = WindowState;
                UpdateMaxState();
            };
            SourceInitialized += delegate { ApplyDwm(); };
            ContentRendered += delegate { RefreshAll(); };
            InitTray();
            UpdateTrayText();   // 提示文字依語言設定
            // AirPods 電量只在「使用中」監聽：視窗開著，且 AirPods 是預設播放裝置
            IsVisibleChanged += delegate { UpdatePodsListening(); };
        }

        // ---------- 外觀小工具 ----------

        static SolidColorBrush B(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        static TextBlock T(string text, double size, string color, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, Foreground = B(color),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock GlyphText(string glyph, double size, string color)
        {
            var t = T(glyph, size, color);
            t.FontFamily = IconFont;
            t.TextTrimming = TextTrimming.None;
            return t;
        }

        // 圖示 + 文字分成兩個 TextBlock，避免圖示字型把中文吃掉
        static StackPanel IconLabel(string glyph, string text, double size)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var i = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(i);
            if (!string.IsNullOrEmpty(text))
                sp.Children.Add(new TextBlock { Text = text, FontFamily = TextFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            return sp;
        }

        static Border Badge(string text, string bg, string fg)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(9), Background = B(bg), Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = T(text, 12, fg, true)
            };
        }

        Button Btn(string style)
        {
            return new Button { Style = (Style)FindResource(style) };
        }

        // ---------- 版面 ----------

        UIElement BuildUi()
        {
            outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 標題列（Discord 風格）
            var bar = new Grid { Background = B("#0A0B0E") };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Logo(20, "#107C10"));
            var bt = T("DJ Mochi", 13, "#9AA0A8", true); bt.Margin = new Thickness(10, 0, 0, 0);
            brand.Children.Add(bt);
            bar.Children.Add(brand);

            var caps = new StackPanel { Orientation = Orientation.Horizontal };
            var minBtn = Btn("Caption"); minBtn.Content = GMin; minBtn.ToolTip = Lang.Tr("cap.min");
            minBtn.Click += delegate { HideToTray(); };
            maxBtn = Btn("Caption"); maxBtn.Content = GMax; maxBtn.ToolTip = Lang.Tr("cap.max");
            maxBtn.Click += delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            var closeBtn = Btn("CaptionClose"); closeBtn.Content = GClose; closeBtn.ToolTip = Lang.Tr("cap.close");
            closeBtn.Click += delegate { Close(); };
            foreach (var b in new[] { minBtn, maxBtn, closeBtn })
            {
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                caps.Children.Add(b);
            }
            Grid.SetColumn(caps, 1);
            bar.Children.Add(caps);
            outer.Children.Add(bar);

            // 主內容
            var main = new Grid { Margin = new Thickness(40, 24, 40, 20) };
            Grid.SetRow(main, 1);
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                main.RowDefinitions.Add(new RowDefinition { Height = h });

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(Logo(48, "#107C10"));
            var ht = T("DJ Mochi", 34, "#FFFFFF", true); ht.Margin = new Thickness(16, 0, 0, 0);
            header.Children.Add(ht);
            main.Children.Add(header);

            var tabsBar = new DockPanel { Margin = new Thickness(0, 20, 0, 10), LastChildFill = true };
            Grid.SetRow(tabsBar, 1);
            tabR = Btn("Pill"); tabR.Content = Lang.Tr("tab.playback"); tabR.Click += delegate { SwitchTab(0); };
            tabC = Btn("Pill"); tabC.Content = Lang.Tr("tab.recording"); tabC.Click += delegate { SwitchTab(1); };
            DockPanel.SetDock(tabR, Dock.Left); DockPanel.SetDock(tabC, Dock.Left);
            var refresh = Btn("Pill"); refresh.Content = IconLabel(GRefresh, Lang.Tr("refresh"), 17);
            refresh.Margin = new Thickness(0); refresh.Padding = new Thickness(18, 10, 18, 10);
            refresh.Click += delegate { RefreshAll(); };
            DockPanel.SetDock(refresh, Dock.Right);
            startupBtn = Btn("Pill"); startupBtn.Padding = new Thickness(18, 10, 18, 10);
            startupBtn.ToolTip = Lang.Tr("startup.tip");
            startupBtn.Click += delegate { ToggleStartup(); };
            DockPanel.SetDock(startupBtn, Dock.Right);
            UpdateStartupBtn();
            status = T("", 15, "#8A8F98"); status.Margin = new Thickness(16, 0, 16, 0);
            tabsBar.Children.Add(tabR); tabsBar.Children.Add(tabC); tabsBar.Children.Add(refresh); tabsBar.Children.Add(startupBtn); tabsBar.Children.Add(status);
            main.Children.Add(tabsBar);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Grid.SetRow(scroll, 2);
            rows = new StackPanel { Margin = new Thickness(0, 6, 6, 6) };
            scroll.Content = rows;
            main.Children.Add(scroll);

            var foot = T(Lang.Tr("footer"), 14, "#6B7079");
            foot.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(foot, 3);
            main.Children.Add(foot);

            outer.Children.Add(main);
            return outer;
        }

        void ApplyDwm()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1; Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);      // 深色模式
            int round = 2; Native.DwmSetWindowAttribute(hwnd, 33, ref round, 4);    // Win11 圓角
            int border = 0x00372E2A; Native.DwmSetWindowAttribute(hwnd, 34, ref border, 4); // 邊框色 #2A2E37
        }

        void UpdateMaxState()
        {
            bool max = WindowState == WindowState.Maximized;
            // 最大化時 WindowChrome 會超出螢幕邊緣，補邊距
            outer.Margin = max ? new Thickness(8) : new Thickness(0);
            maxBtn.Content = max ? GRestore : GMax;
            maxBtn.ToolTip = Lang.Tr(max ? "cap.restore" : "cap.max");
        }

        // ---------- 資料 ----------

        void RefreshAll()
        {
            Audio.ClearCache();
            cache[0] = Audio.Load(0);
            cache[1] = Audio.Load(1);
            Render();
        }

        void SwitchTab(int t)
        {
            if (tab == t) return;
            tab = t;
            pendingHideId = null;
            Render();
        }

        void SetStatus(string msg)
        {
            status.Text = msg;
            statusSticky = true;
        }

        string IconFor(Device d)
        {
            string o;
            string desc = orig.TryGetValue(d.Id, out o) ? o : d.Desc;   // 用原始名稱判斷，改名後圖示不會跑掉
            string dev = d.Dev ?? "";
            if (dev.Contains("Steam Streaming")) return GGame;
            if (d.Flow == 1 || desc.Contains("麥克風") || desc.Contains("Microphone")) return GMic;
            if (desc.Contains("耳機") || desc.Contains("Headphone") || desc.Contains("Headset") ||
                dev.Contains("AirPods") || dev.Contains("Arctis") || dev.Contains("Headset")) return GHeadphone;
            if (dev.Contains("NVIDIA") || dev.Contains("HDMI") || dev.Contains("Monitor") || desc.Contains("螢幕") || desc.Contains("HDMI") ||
                System.Text.RegularExpressions.Regex.IsMatch(desc, @"XV\d")) return GMonitor;
            return GSpeaker;
        }

        // ---------- 畫面 ----------

        void Render()
        {
            editBox = null;
            rows.Children.Clear();
            var devs = cache[tab];
            var shown = devs.Where(d => d.Enabled).OrderBy(d => !d.IsDefault).ThenBy(d => d.Unplugged).ThenBy(d => d.Desc).ToList();
            var hidden = devs.Where(d => !d.Enabled).OrderBy(d => d.Desc).ToList();

            if (shown.Count > 0)
            {
                rows.Children.Add(SectionHeader(Lang.Tr("sec.shown"), shown.Count, showShown[tab], new Thickness(0, 0, 0, 10),
                    delegate { showShown[tab] = !showShown[tab]; Render(); }));
                if (showShown[tab]) foreach (var d in shown) rows.Children.Add(Row(d, false));
            }

            if (hidden.Count > 0)
            {
                rows.Children.Add(SectionHeader(Lang.Tr("sec.hidden"), hidden.Count, showHidden[tab], new Thickness(0, 14, 0, 10),
                    delegate { showHidden[tab] = !showHidden[tab]; Render(); }));
                if (showHidden[tab]) foreach (var d in hidden) rows.Children.Add(Row(d, true));
            }

            tabR.Background = B(tab == 0 ? "#107C10" : "#1B1E25"); tabR.Foreground = B(tab == 0 ? "#FFFFFF" : "#C9CDD4");
            tabC.Background = B(tab == 1 ? "#107C10" : "#1B1E25"); tabC.Foreground = B(tab == 1 ? "#FFFFFF" : "#C9CDD4");

            // 狀態列只顯示操作結果，平常留空
            if (!statusSticky) status.Text = "";
            statusSticky = false;

            // 預設裝置或可見狀態可能改變了（設為預設、隱藏、右下角切換…），重新判斷要不要監聽 AirPods
            UpdatePodsListening();
        }

        Button SectionHeader(string text, int count, bool open, Thickness margin, RoutedEventHandler onClick)
        {
            var hdr = Btn("Mini");
            hdr.HorizontalAlignment = HorizontalAlignment.Left;
            hdr.Margin = margin;
            hdr.Background = B("#14161B"); hdr.Foreground = B("#9AA0A8");
            hdr.Content = IconLabel(open ? GDown : GRight, text + " (" + count + ")", 14);
            hdr.Click += onClick;
            return hdr;
        }

        // 一列一個裝置；compact = 已隱藏區的精簡列（縮排、沒有音量）
        UIElement Row(Device d, bool compact)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(14),
                Margin = compact ? new Thickness(36, 0, 0, 8) : new Thickness(0, 0, 0, 10),
                Padding = compact ? new Thickness(16, 8, 16, 8) : new Thickness(18, 14, 18, 14),
                Background = B(compact ? "#14161B" : "#1B1E25"),
                BorderThickness = new Thickness(4, 0, 0, 0),
                BorderBrush = B(d.IsDefault ? "#107C10" : compact ? "#22252C" : "#2A2E37"),
                ToolTip = d.Name
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 0：圖示
            var icon = GlyphText(IconFor(d), compact ? 24 : 32, d.IsDefault ? "#3FD13F" : compact ? "#6B7079" : "#E6E8EB");
            grid.Children.Add(icon);

            // 1：名稱 + 編輯 / 還原 + 標籤；第二行硬體名稱
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var title = T(d.Desc, compact ? 16 : 19, compact ? "#9AA0A8" : "#FFFFFF", true);
            line.Children.Add(title);
            if (compact && !string.IsNullOrEmpty(d.Dev))
            {
                var dt = T("·  " + d.Dev, 14, "#6B7079"); dt.Margin = new Thickness(10, 0, 0, 0);
                line.Children.Add(dt);
            }

            var edit = Btn("Round"); edit.Width = 30; edit.Height = 30; edit.FontSize = 14;
            edit.Content = GEdit; edit.ToolTip = Lang.Tr("rename"); edit.Margin = new Thickness(6, 0, 0, 0); edit.Foreground = B("#8A8F98");
            var dEdit = d; var lineRef = line; var titleRef = title;
            edit.Click += delegate { StartRename(dEdit, lineRef, titleRef); };
            line.Children.Add(edit);

            string o;
            if (orig.TryGetValue(d.Id, out o) && o != d.Desc)
            {
                var undo = Btn("Round"); undo.Width = 30; undo.Height = 30; undo.FontSize = 14;
                undo.Content = GUndo; undo.ToolTip = Lang.Tr("rename.restore", o); undo.Foreground = B("#8A8F98");
                var dUndo = d; var oName = o;
                undo.Click += delegate { Rename(dUndo, oName); };
                line.Children.Add(undo);
            }

            if (d.IsDefault) line.Children.Add(Badge(Lang.Tr("badge.default"), "#107C10", "#FFFFFF"));
            if (d.IsVirtual) line.Children.Add(Badge(Lang.Tr("badge.virtual"), "#3A3F4B", "#C9CDD4"));
            if (d.Unplugged) line.Children.Add(Badge(Lang.Tr("badge.unplugged"), "#5A4A1E", "#F2D27A"));
            info.Children.Add(line);
            if (!compact && !string.IsNullOrEmpty(d.Dev))
            {
                var sub = T(d.Dev, 13, "#8A8F98"); sub.Margin = new Thickness(0, 1, 0, 0);
                info.Children.Add(sub);
            }
            if (!compact && podsStatus != null && d.IsDefault && IsAirPods(d))
            {
                var bat = PodsBattery();
                if (bat != null) info.Children.Add(bat);
            }
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // 2：音量
            if (!compact && !d.Unplugged)
            {
                float vol = Audio.GetVolume(d.Id);
                if (vol >= 0)
                {
                    var vp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 18, 0) };
                    var mute = Btn("Round");
                    UpdateMute(mute, d, Audio.GetMute(d.Id) == 1);

                    var slider = new Slider { Style = (Style)FindResource("Vol"), Width = 220, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                    slider.Value = Math.Round(vol * 100);
                    var pct = T(((int)slider.Value).ToString(), 16, "#E6E8EB", true);
                    pct.Width = 38; pct.TextAlignment = TextAlignment.Right;

                    var id = d.Id; var dMute = d;
                    slider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
                    {
                        int v = (int)Math.Round(e.NewValue);
                        Audio.SetVolume(id, v / 100f);
                        pct.Text = v.ToString();
                    };
                    slider.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
                    {
                        slider.Value = Math.Max(0, Math.Min(100, slider.Value + (e.Delta > 0 ? 2 : -2)));
                        e.Handled = true;
                    };
                    mute.Click += delegate
                    {
                        bool now = Audio.GetMute(id) == 1;
                        if (Audio.SetMute(id, !now) == 0) UpdateMute(mute, dMute, !now);
                    };

                    vp.Children.Add(mute); vp.Children.Add(slider); vp.Children.Add(pct);
                    Grid.SetColumn(vp, 2);
                    grid.Children.Add(vp);
                }
            }

            // 3：設為預設
            if (d.Enabled && !d.IsDefault && !d.Unplugged)
            {
                var def = Btn("Mini"); def.Content = Lang.Tr("setDefault"); def.VerticalAlignment = VerticalAlignment.Center; def.Margin = new Thickness(0, 0, 10, 0);
                var dDef = d;
                def.Click += delegate { SetDefault(dDef); };
                Grid.SetColumn(def, 3);
                grid.Children.Add(def);
            }

            // 4：顯示 / 隱藏
            var dTg = d;
            if (d.Enabled && d.Id == pendingHideId)
            {
                // 預設裝置的二次確認：灰色「取消」+ 紅色「確認隱藏？」
                var confirmPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var cancel = Btn("Ghost"); cancel.Content = Lang.Tr("cancel"); cancel.Margin = new Thickness(0, 0, 8, 0);
                cancel.Click += delegate { pendingHideId = null; Render(); };
                var confirm = Btn("Danger"); confirm.Content = IconLabel(GHidden, Lang.Tr("confirmHide"), 14);
                confirm.Click += delegate { Toggle(dTg, true); };
                confirmPanel.Children.Add(cancel); confirmPanel.Children.Add(confirm);
                Grid.SetColumn(confirmPanel, 4);
                grid.Children.Add(confirmPanel);
            }
            else
            {
                var tg = Btn("Mini"); tg.VerticalAlignment = VerticalAlignment.Center; tg.MinWidth = 96;
                tg.Content = d.Enabled ? IconLabel(GHidden, Lang.Tr("hide"), 14) : IconLabel(GShow, Lang.Tr("show"), 14);
                tg.Click += delegate { Toggle(dTg, false); };
                Grid.SetColumn(tg, 4);
                grid.Children.Add(tg);
            }

            row.Child = grid;
            return row;
        }

        void UpdateMute(Button b, Device d, bool muted)
        {
            b.Content = d.Flow == 1 ? (muted ? GMicOff : GMic) : (muted ? GMute : GVolume);
            b.Foreground = B(muted ? "#E05A5A" : "#E6E8EB");
            b.ToolTip = Lang.Tr(muted ? "unmute" : "mute");
        }

        // ---------- 動作 ----------

        void Toggle(Device d, bool confirmed)
        {
            try
            {
                if (d.Enabled && d.Flow == 0 && !cache[0].Any(x => x.Enabled && x.Id != d.Id))
                {
                    SetStatus(Lang.Tr("st.keepOne"));
                    Render();
                    return;
                }
                // 隱藏預設裝置要二次確認：第一次只顯示「取消 / 確認隱藏？」
                if (d.Enabled && d.IsDefault && !confirmed)
                {
                    pendingHideId = d.Id;
                    SetStatus(Lang.Tr("st.defaultWarn", d.Desc));
                    Render();
                    return;
                }
                pendingHideId = null;
                int hr = Audio.SetVisibility(d.Id, !d.Enabled);
                if (hr != 0) throw new Exception(Lang.Tr("err.visibility", d.Name, hr));
                d.Enabled = !d.Enabled;
                // 隱藏的若是預設裝置，Windows 會自動換一個預設
                if (!d.Enabled && d.IsDefault)
                {
                    string def = Audio.GetDefault(d.Flow);
                    foreach (var x in cache[d.Flow]) x.IsDefault = def != null && string.Equals(x.Id, def, StringComparison.OrdinalIgnoreCase);
                }
                SetStatus(Lang.Tr(d.Enabled ? "st.shown" : "st.hidden", d.Name));
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            Render();
        }

        void SetDefault(Device d)
        {
            if (!d.Enabled || d.IsDefault || d.Unplugged) return;
            try
            {
                int hr = Audio.SetDefault(d.Id);
                if (hr != 0) throw new Exception(Lang.Tr("err.default", hr));
                foreach (var x in cache[d.Flow]) x.IsDefault = x.Id == d.Id;
                SetStatus(Lang.Tr("st.defaultSet", d.Name));
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            Render();
        }

        // ---- 重新命名：標題換成輸入框；Enter / 再按一次鉛筆 = 儲存，Esc / 點別處 = 取消 ----
        void StartRename(Device d, StackPanel line, TextBlock title)
        {
            if (editBox != null)
            {
                var cur = editBox; editBox = null;
                var curDev = (Device)cur.Tag;
                if (curDev.Id == d.Id) Rename(curDev, cur.Text.Trim()); else Render();
                return;
            }
            int idx = line.Children.IndexOf(title);
            if (idx < 0) return;

            var tb = new TextBox
            {
                Text = d.Desc, FontSize = 18, MinWidth = 240, Padding = new Thickness(6, 3, 6, 3),
                Background = B("#0E1014"), Foreground = Brushes.White, CaretBrush = Brushes.White,
                BorderBrush = B("#107C10"), BorderThickness = new Thickness(2), FontFamily = TextFont,
                ToolTip = Lang.Tr("rename.tip"), Tag = d
            };
            line.Children.RemoveAt(idx);
            line.Children.Insert(idx, tb);
            editBox = tb;

            tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (editBox != tb) return;
                if (e.Key == Key.Return) { e.Handled = true; editBox = null; Rename(d, tb.Text.Trim()); }
                else if (e.Key == Key.Escape) { e.Handled = true; editBox = null; Render(); }
            };
            tb.LostKeyboardFocus += delegate
            {
                if (editBox != tb) return;
                editBox = null;
                // 延後到目前的事件處理完再重畫，避免在點擊途中把畫面拆掉
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Render));
            };
            tb.Focus();
            tb.SelectAll();
        }

        void Rename(Device d, string newName)
        {
            string o;
            bool hasOrig = orig.TryGetValue(d.Id, out o);
            if (string.IsNullOrEmpty(newName)) newName = hasOrig ? o : d.Desc;   // 清空 = 還原
            if (newName == d.Desc) { Render(); return; }
            try
            {
                if (!hasOrig) { orig[d.Id] = d.Desc; SaveNames(); }
                int hr = Audio.SetName(d.Id, newName);
                if (hr != 0) throw new Exception(Lang.Tr("err.rename", hr));
                string old = d.Desc;
                d.Desc = newName;
                SetStatus(Lang.Tr("st.renamed", old, newName));
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            Render();
        }

        Dictionary<string, string> LoadNames()
        {
            try
            {
                if (File.Exists(namesFile))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(namesFile, Encoding.UTF8));
                    if (d != null) return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        void SaveNames()
        {
            try { File.WriteAllText(namesFile, new JavaScriptSerializer().Serialize(orig), new UTF8Encoding(false)); }
            catch (Exception ex) { SetStatus(Lang.Tr("err.saveNames", ex.Message)); }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;   // 正在輸入名稱時不攔截
            if (e.Key == Key.F5) { RefreshAll(); e.Handled = true; }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }   // X = 直接結束，圖示一起移除
            if (pods != null) { pods.Dispose(); pods = null; }
            Audio.ClearCache();
            base.OnClosed(e);
        }

    }
}
