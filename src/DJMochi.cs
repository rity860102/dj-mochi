// DJMochi.cs — DJ Mochi 音訊裝置管理
// 音訊裝置管理：顯示/隱藏（= Windows 設定「允許 / 不允許」）、設為預設、音量 / 靜音、重新命名。
// 以 Windows 內建的 .NET Framework 4.8 編譯（C# 5 語法），不需要安裝任何東西。建置：執行 build.ps1

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
    // ===================== COM 介面 =====================

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, ref PROPERTYKEY key, out PROPVARIANT val);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, ref PROPERTYKEY key, ref PROPVARIANT val);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    [ComImport, Guid("870AF99C-171D-4f9e-AF0D-E63DF40C2BC9")]
    class CPolicyConfigClient { }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr col);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr n);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
        [PreserveSig] int GetChannelCount(out uint c);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float v, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float v);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float v, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float v);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }

    // ===================== 資料 =====================

    class Device
    {
        public int Flow;          // 0=播放 1=錄音
        public string Desc;       // Windows 顯示的名稱（可改）
        public string Dev;        // 硬體名稱
        public bool Enabled;
        public bool Unplugged;
        public bool IsDefault;
        public string Id;
        public string Name { get { return string.IsNullOrEmpty(Dev) ? Desc : Desc + " (" + Dev + ")"; } }
        public bool IsVirtual { get { return Dev != null && (Dev.Contains("Sonar") || Dev.Contains("Steam Streaming") || Dev.Contains("Virtual")); } }
    }

    static class Audio
    {
        const string PolicyKeyDesc = "a45c254e-df1c-4efd-8020-67d146a850e0";

        public static int SetVisibility(string id, bool visible)
        {
            var pc = (IPolicyConfig)new CPolicyConfigClient();
            try { return pc.SetEndpointVisibility(id, visible ? 1 : 0); }
            finally { Marshal.ReleaseComObject(pc); }
        }

        public static int SetDefault(string id)
        {
            var pc = (IPolicyConfig)new CPolicyConfigClient();
            try
            {
                for (int r = 0; r < 3; r++) { int hr = pc.SetDefaultEndpoint(id, r); if (hr != 0) return hr; }
                return 0;
            }
            finally { Marshal.ReleaseComObject(pc); }
        }

        // 重新命名 = 改 PKEY_Device_DeviceDesc（Windows 設定 → 音效 → 重新命名 改的就是這個）
        public static int SetName(string id, string name)
        {
            var pc = (IPolicyConfig)new CPolicyConfigClient();
            var key = new PROPERTYKEY { fmtid = new Guid(PolicyKeyDesc), pid = 2 };
            var pv = new PROPVARIANT { vt = 31 };   // VT_LPWSTR
            pv.p = Marshal.StringToCoTaskMemUni(name);
            try { return pc.SetPropertyValue(id, 0, ref key, ref pv); }
            finally { Marshal.FreeCoTaskMem(pv.p); Marshal.ReleaseComObject(pc); }
        }

        public static string GetDefault(int flow)
        {
            var e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                IMMDevice d;
                if (e.GetDefaultAudioEndpoint(flow, 0, out d) != 0 || d == null) return null;
                string id; d.GetId(out id); Marshal.ReleaseComObject(d); return id;
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(e); }
        }

        // ---- 音量（快取 IAudioEndpointVolume，拖滑桿時不用每次重建）----
        static readonly Dictionary<string, IAudioEndpointVolume> vols = new Dictionary<string, IAudioEndpointVolume>();
        static IMMDeviceEnumerator enumr;

        static IAudioEndpointVolume Vol(string id)
        {
            IAudioEndpointVolume v;
            if (vols.TryGetValue(id, out v)) return v;
            if (enumr == null) enumr = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice d;
            if (enumr.GetDevice(id, out d) != 0 || d == null) return null;
            try
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return null;
                v = (IAudioEndpointVolume)o;
                vols[id] = v;
                return v;
            }
            finally { Marshal.ReleaseComObject(d); }
        }

        public static void ClearCache()
        {
            foreach (var v in vols.Values) { try { Marshal.ReleaseComObject(v); } catch { } }
            vols.Clear();
        }

        public static float GetVolume(string id)
        {
            try { var v = Vol(id); float f; if (v != null && v.GetMasterVolumeLevelScalar(out f) == 0) return f; } catch { }
            return -1;
        }

        public static int SetVolume(string id, float f)
        {
            try { var v = Vol(id); if (v == null) return -1; Guid g = Guid.Empty; return v.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, f)), ref g); }
            catch { return -1; }
        }

        public static int GetMute(string id)
        {
            try { var v = Vol(id); bool m; if (v != null && v.GetMute(out m) == 0) return m ? 1 : 0; } catch { }
            return -1;
        }

        public static int SetMute(string id, bool m)
        {
            try { var v = Vol(id); if (v == null) return -1; Guid g = Guid.Empty; return v.SetMute(m, ref g); }
            catch { return -1; }
        }

        // DeviceState 低位：1=啟用 2=停用 4=不存在 8=未接上；0x10000000 = 被「不允許」隱藏
        public static List<Device> Load(int flow)
        {
            var list = new List<Device>();
            string sub = flow == 0 ? "Render" : "Capture";
            string prefix = flow == 0 ? "{0.0.0.00000000}." : "{0.0.1.00000000}.";
            string def = GetDefault(flow);
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var root = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\" + sub))
            {
                if (root == null) return list;
                foreach (string name in root.GetSubKeyNames())
                {
                    using (var k = root.OpenSubKey(name))
                    {
                        if (k == null) continue;
                        object so = k.GetValue("DeviceState");
                        if (so == null) continue;
                        uint state = unchecked((uint)Convert.ToInt32(so));
                        uint low = state & 0xF;
                        if (low != 1 && low != 2 && low != 8) continue;
                        string desc = "", dev = "";
                        using (var p = k.OpenSubKey("Properties"))
                        {
                            if (p != null)
                            {
                                desc = (p.GetValue("{" + PolicyKeyDesc + "},2") as string) ?? "";
                                dev = (p.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string) ?? "";
                            }
                        }
                        string id = prefix + name;
                        list.Add(new Device
                        {
                            Flow = flow, Desc = desc, Dev = dev, Id = id,
                            Enabled = (state & 0x10000000) == 0 && low != 2,
                            Unplugged = low == 8,
                            IsDefault = def != null && string.Equals(id, def, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            return list;
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    // 開機自動啟動：HKCU\...\Run（工作管理員「啟動應用程式」讀的就是這裡）
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string Name = "DJMochi";
        public const string TrayArg = "--tray";

        static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        static string Command { get { return "\"" + ExePath + "\" " + TrayArg; } }

        // 已登記、路徑是目前這個 exe、且沒有在工作管理員被停用
        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var v = k == null ? null : k.GetValue(Name) as string;
                if (v == null || v.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            using (var a = Registry.CurrentUser.OpenSubKey(ApprovedKey))
            {
                var b = a == null ? null : a.GetValue(Name) as byte[];
                if (b != null && b.Length > 0 && (b[0] & 1) == 1) return false;   // 工作管理員停用：第一個位元組為奇數
            }
            return true;
        }

        public static void SetEnabled(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, Command);
                else k.DeleteValue(Name, false);
            }
            // 清掉工作管理員的停用紀錄，避免「登記了但被停用」
            using (var a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
            {
                if (a != null) a.DeleteValue(Name, false);
            }
        }
    }

    // ===================== 主視窗 =====================

    class MainWindow : Window
    {
        static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily TextFont = new FontFamily("Microsoft JhengHei UI");

        const string GVolume = "", GMute = "", GHeadphone = "", GSpeaker = "", GMonitor = "",
                     GMic = "", GMicOff = "", GGame = "", GRefresh = "", GHidden = "",
                     GShow = "", GRight = "", GDown = "", GEdit = "", GUndo = "",
                     GMin = "", GMax = "", GRestore = "", GClose = "";

        static readonly string[] TabNames = { "播放", "錄音" };

        // DJ Mochi 圖示（build.ps1 以 /resource 內嵌 src\icon\mochi.png）
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
        const int PodsMinRssi = -80;        // 太遠的（可能是別人的）不採用
        const int PodsLowLevel = 10;        // 低於等於這個電量就提醒

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
            // AirPods 電量只在視窗顯示時監聽；縮到右下角或關閉就停止，平常不在背景掃描
            IsVisibleChanged += delegate { if (IsVisible) StartPods(); else StopPods(); };
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
            var minBtn = Btn("Caption"); minBtn.Content = GMin; minBtn.ToolTip = "縮小到右下角";
            minBtn.Click += delegate { HideToTray(); };
            maxBtn = Btn("Caption"); maxBtn.Content = GMax; maxBtn.ToolTip = "最大化";
            maxBtn.Click += delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            var closeBtn = Btn("CaptionClose"); closeBtn.Content = GClose; closeBtn.ToolTip = "關閉";
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
            tabR = Btn("Pill"); tabR.Content = "播放"; tabR.Click += delegate { SwitchTab(0); };
            tabC = Btn("Pill"); tabC.Content = "錄音"; tabC.Click += delegate { SwitchTab(1); };
            DockPanel.SetDock(tabR, Dock.Left); DockPanel.SetDock(tabC, Dock.Left);
            var refresh = Btn("Pill"); refresh.Content = IconLabel(GRefresh, "重新整理", 17);
            refresh.Margin = new Thickness(0); refresh.Padding = new Thickness(18, 10, 18, 10);
            refresh.Click += delegate { RefreshAll(); };
            DockPanel.SetDock(refresh, Dock.Right);
            startupBtn = Btn("Pill"); startupBtn.Padding = new Thickness(18, 10, 18, 10);
            startupBtn.ToolTip = "開機時自動啟動，並直接縮在右下角";
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

            var foot = T("拖曳或滾輪調整音量　　鉛筆：重新命名（Windows 也會顯示）　　F5：重新整理", 14, "#6B7079");
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
            maxBtn.ToolTip = max ? "還原" : "最大化";
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
                rows.Children.Add(SectionHeader("顯示中", shown.Count, showShown[tab], new Thickness(0, 0, 0, 10),
                    delegate { showShown[tab] = !showShown[tab]; Render(); }));
                if (showShown[tab]) foreach (var d in shown) rows.Children.Add(Row(d, false));
            }

            if (hidden.Count > 0)
            {
                rows.Children.Add(SectionHeader("已隱藏", hidden.Count, showHidden[tab], new Thickness(0, 14, 0, 10),
                    delegate { showHidden[tab] = !showHidden[tab]; Render(); }));
                if (showHidden[tab]) foreach (var d in hidden) rows.Children.Add(Row(d, true));
            }

            tabR.Background = B(tab == 0 ? "#107C10" : "#1B1E25"); tabR.Foreground = B(tab == 0 ? "#FFFFFF" : "#C9CDD4");
            tabC.Background = B(tab == 1 ? "#107C10" : "#1B1E25"); tabC.Foreground = B(tab == 1 ? "#FFFFFF" : "#C9CDD4");

            // 狀態列只顯示操作結果，平常留空
            if (!statusSticky) status.Text = "";
            statusSticky = false;
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
            edit.Content = GEdit; edit.ToolTip = "重新命名"; edit.Margin = new Thickness(6, 0, 0, 0); edit.Foreground = B("#8A8F98");
            var dEdit = d; var lineRef = line; var titleRef = title;
            edit.Click += delegate { StartRename(dEdit, lineRef, titleRef); };
            line.Children.Add(edit);

            string o;
            if (orig.TryGetValue(d.Id, out o) && o != d.Desc)
            {
                var undo = Btn("Round"); undo.Width = 30; undo.Height = 30; undo.FontSize = 14;
                undo.Content = GUndo; undo.ToolTip = "還原成「" + o + "」"; undo.Foreground = B("#8A8F98");
                var dUndo = d; var oName = o;
                undo.Click += delegate { Rename(dUndo, oName); };
                line.Children.Add(undo);
            }

            if (d.IsDefault) line.Children.Add(Badge("預設", "#107C10", "#FFFFFF"));
            if (d.IsVirtual) line.Children.Add(Badge("虛擬", "#3A3F4B", "#C9CDD4"));
            if (d.Unplugged) line.Children.Add(Badge("未接上", "#5A4A1E", "#F2D27A"));
            info.Children.Add(line);
            if (!compact && !string.IsNullOrEmpty(d.Dev))
            {
                var sub = T(d.Dev, 13, "#8A8F98"); sub.Margin = new Thickness(0, 1, 0, 0);
                info.Children.Add(sub);
            }
            if (!compact && podsStatus != null && d.Dev != null && d.Dev.IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0)
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
                var def = Btn("Mini"); def.Content = "設為預設"; def.VerticalAlignment = VerticalAlignment.Center; def.Margin = new Thickness(0, 0, 10, 0);
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
                var cancel = Btn("Ghost"); cancel.Content = "取消"; cancel.Margin = new Thickness(0, 0, 8, 0);
                cancel.Click += delegate { pendingHideId = null; Render(); };
                var confirm = Btn("Danger"); confirm.Content = IconLabel(GHidden, "確認隱藏？", 14);
                confirm.Click += delegate { Toggle(dTg, true); };
                confirmPanel.Children.Add(cancel); confirmPanel.Children.Add(confirm);
                Grid.SetColumn(confirmPanel, 4);
                grid.Children.Add(confirmPanel);
            }
            else
            {
                var tg = Btn("Mini"); tg.VerticalAlignment = VerticalAlignment.Center; tg.MinWidth = 96;
                tg.Content = d.Enabled ? IconLabel(GHidden, "隱藏", 14) : IconLabel(GShow, "顯示", 14);
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
            b.ToolTip = muted ? "取消靜音" : "靜音";
        }

        // ---------- 動作 ----------

        void Toggle(Device d, bool confirmed)
        {
            try
            {
                if (d.Enabled && d.Flow == 0 && !cache[0].Any(x => x.Enabled && x.Id != d.Id))
                {
                    SetStatus("播放裝置至少要保留一個顯示。");
                    Render();
                    return;
                }
                // 隱藏預設裝置要二次確認：第一次只顯示「取消 / 確認隱藏？」
                if (d.Enabled && d.IsDefault && !confirmed)
                {
                    pendingHideId = d.Id;
                    SetStatus("「" + d.Desc + "」是預設裝置，隱藏後 Windows 會改用其他裝置。");
                    Render();
                    return;
                }
                pendingHideId = null;
                int hr = Audio.SetVisibility(d.Id, !d.Enabled);
                if (hr != 0) throw new Exception(string.Format("{0} 設定失敗 (HRESULT 0x{1:X8})", d.Name, hr));
                d.Enabled = !d.Enabled;
                // 隱藏的若是預設裝置，Windows 會自動換一個預設
                if (!d.Enabled && d.IsDefault)
                {
                    string def = Audio.GetDefault(d.Flow);
                    foreach (var x in cache[d.Flow]) x.IsDefault = def != null && string.Equals(x.Id, def, StringComparison.OrdinalIgnoreCase);
                }
                SetStatus((d.Enabled ? "已顯示：" : "已隱藏：") + d.Name);
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
                if (hr != 0) throw new Exception(string.Format("設為預設失敗 (HRESULT 0x{0:X8})", hr));
                foreach (var x in cache[d.Flow]) x.IsDefault = x.Id == d.Id;
                SetStatus("已設為預設：" + d.Name);
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
                ToolTip = "Enter 儲存、Esc 取消；清空後儲存 = 還原原始名稱", Tag = d
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
                if (hr != 0) throw new Exception(string.Format("重新命名失敗 (HRESULT 0x{0:X8})", hr));
                string old = d.Desc;
                d.Desc = newName;
                SetStatus("已重新命名：" + old + " → " + newName);
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
            catch (Exception ex) { SetStatus("無法儲存原始名稱備份：" + ex.Message); }
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

        // ---------- 右下角通知區域 ----------

        void InitTray()
        {
            tray = new WinForms.NotifyIcon { Text = "DJ Mochi", Icon = LoadTrayIcon(), Visible = true };
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

        // ---------- AirPods 電量 ----------

        void StopPods()
        {
            if (pods != null) { pods.Dispose(); pods = null; }
        }

        void StartPods()
        {
            if (pods != null) return;
            try
            {
                pods = new AirPodsMonitor();
                pods.Updated += delegate(AirPodsStatus s)
                {
                    // 廣播在背景執行緒進來，切回 UI 執行緒處理
                    Dispatcher.BeginInvoke(new Action(delegate { OnPods(s); }));
                };
                pods.Start();
            }
            catch { pods = null; }   // 沒有藍牙或不支援 BLE：不顯示電量就好
        }

        void OnPods(AirPodsStatus s)
        {
            if (s.Rssi < PodsMinRssi) return;
            var cur = podsStatus;
            // 附近有多副時：同一副、訊號明顯更強、或原本那副已經 30 秒沒消息，才換
            bool take = cur == null || s.Address == cur.Address || s.Rssi > cur.Rssi + 5 || (DateTime.Now - podsSeen).TotalSeconds > 30;
            if (!take) return;

            bool changed = cur == null || cur.Left != s.Left || cur.Right != s.Right || cur.Case != s.Case ||
                           cur.ChargingLeft != s.ChargingLeft || cur.ChargingRight != s.ChargingRight || cur.ChargingCase != s.ChargingCase;
            podsStatus = s;
            podsSeen = DateTime.Now;
            if (!changed) return;   // 廣播很頻繁，數值沒變就不重畫

            UpdateTrayText();
            CheckPodsLow(s);
            if (IsVisible && editBox == null) Render();
        }

        static string PodsSummary(AirPodsStatus s)
        {
            var parts = new List<string>();
            if (s.Left >= 0)  parts.Add("左 " + s.Left + "%" + (s.ChargingLeft ? "⚡" : ""));
            if (s.Right >= 0) parts.Add("右 " + s.Right + "%" + (s.ChargingRight ? "⚡" : ""));
            if (s.Case >= 0)  parts.Add("盒 " + s.Case + "%" + (s.ChargingCase ? "⚡" : ""));
            return string.Join("  ", parts);
        }

        void UpdateTrayText()
        {
            if (tray == null) return;
            string text = "DJ Mochi";
            if (podsStatus != null)
            {
                string sum = PodsSummary(podsStatus);
                if (sum.Length > 0) text += "\n" + podsStatus.ModelName + "\n" + sum;
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
                if (tray != null) tray.ShowBalloonTip(4000, "AirPods 電量低", s.ModelName + "：" + PodsSummary(s), WinForms.ToolTipIcon.Warning);
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
            add("左", s.Left, s.ChargingLeft);
            add("右", s.Right, s.ChargingRight);
            add("盒", s.Case, s.ChargingCase);
            if (sp.Children.Count == 0) return null;
            if (stale)
            {
                var ago = T(string.Format("{0:0} 分鐘前", (DateTime.Now - podsSeen).TotalMinutes), 12, "#6B7079");
                sp.Children.Add(ago);
            }
            sp.ToolTip = "AirPods 廣播的電量精度為 10%（" + s.ModelName + "）";
            return sp;
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

        // ---------- 樣式 ----------

        const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <FontFamily x:Key='Icons'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>

  <Style x:Key='Caption' TargetType='Button'>
    <Setter Property='Width' Value='46'/>
    <Setter Property='Height' Value='40'/>
    <Setter Property='Foreground' Value='#B5BAC1'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='FontFamily' Value='{StaticResource Icons}'/>
    <Setter Property='FontSize' Value='10'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#1F2228'/>
              <Setter Property='Foreground' Value='#FFFFFF'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='CaptionClose' TargetType='Button' BasedOn='{StaticResource Caption}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#E81123'/>
              <Setter Property='Foreground' Value='#FFFFFF'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Pill' TargetType='Button'>
    <Setter Property='Foreground' Value='#C9CDD4'/>
    <Setter Property='Background' Value='#1B1E25'/>
    <Setter Property='FontSize' Value='18'/>
    <Setter Property='Padding' Value='26,10'/>
    <Setter Property='Margin' Value='0,0,12,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='22' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.85'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Mini' TargetType='Button'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='Background' Value='#2A2E37'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Padding' Value='14,7'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='14' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#107C10'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource Mini}'>
    <Setter Property='Background' Value='#C42B1C'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='14' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#E0453A'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource Mini}'>
    <Setter Property='Background' Value='#2A2E37'/>
    <Setter Property='Foreground' Value='#C9CDD4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='14' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#3A3F4B'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Round' TargetType='Button'>
    <Setter Property='Foreground' Value='#E6E8EB'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='FontFamily' Value='{StaticResource Icons}'/>
    <Setter Property='FontSize' Value='18'/>
    <Setter Property='Width' Value='38'/>
    <Setter Property='Height' Value='38'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='19' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#2A2E37'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='TrackFill' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Grid Background='Transparent'><Border Height='6' CornerRadius='3' Background='#107C10'/></Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='TrackEmpty' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Grid Background='Transparent'><Border Height='6' CornerRadius='3' Background='#3A3F4B'/></Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='VolThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Ellipse x:Name='e' Width='18' Height='18' Fill='#FFFFFF'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='e' Property='Fill' Value='#C8F5C8'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Vol' TargetType='Slider'>
    <Setter Property='Minimum' Value='0'/>
    <Setter Property='Maximum' Value='100'/>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid Height='28' Background='Transparent'>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource TrackFill}'/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource TrackEmpty}'/></Track.IncreaseRepeatButton>
              <Track.Thumb><Thumb Style='{StaticResource VolThumb}'/></Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 深色細捲軸 -->
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border CornerRadius='4' Background='#3A3F4B' Margin='2,0'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }

    // ===================== 進入點 =====================

    static class Program
    {
        const string ShowEventName = @"Local\DJMochi_Show";

        [STAThread]
        static void Main(string[] args)
        {
            // --tray：開機自動啟動時直接縮在右下角，不顯示視窗
            bool startInTray = args.Any(a => string.Equals(a, Startup.TrayArg, StringComparison.OrdinalIgnoreCase));
            bool created;
            using (var mutex = new Mutex(true, @"Local\DJMochi_SingleInstance", out created))
            {
                // 防多開：已經開著就通知它把視窗叫出來（縮在右下角時沒有視窗可找，所以用系統事件通知）
                if (!created)
                {
                    Native.AllowSetForegroundWindow(-1);   // 允許原本的程式搶到前景
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set();
                    }
                    catch
                    {
                        // 舊版沒有這個事件：退回找視窗
                        var me = Process.GetCurrentProcess();
                        foreach (var p in Process.GetProcessesByName(me.ProcessName))
                        {
                            if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                            if (Native.IsIconic(p.MainWindowHandle)) Native.ShowWindow(p.MainWindowHandle, 9);
                            Native.SetForegroundWindow(p.MainWindowHandle);
                            break;
                        }
                    }
                    return;
                }

                var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                var app = new Application();
                MainWindow win = null;
                // 全域錯誤保護：沒接住的例外只顯示在狀態列，不讓程式閃退
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    e.Handled = true;
                    if (win != null) win.Title = "DJ Mochi";
                    MessageBox.Show(e.Exception.GetBaseException().Message, "DJ Mochi - 發生錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                };
                win = new MainWindow();

                // 等待其他實例的「叫出來」通知
                var listener = new Thread(delegate()
                {
                    while (true)
                    {
                        showEvent.WaitOne();
                        win.Dispatcher.BeginInvoke(new Action(win.ShowFromTray));
                    }
                }) { IsBackground = true };
                listener.Start();

                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.MainWindow = win;
                if (startInTray) app.Run();   // 視窗不顯示，只留右下角圖示
                else app.Run(win);
            }
        }
    }
}
