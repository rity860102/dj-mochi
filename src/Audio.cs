// Audio.cs — 音訊裝置資料與控制：讀取清單、顯示 / 隱藏、設為預設、重新命名、音量 / 靜音

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
}
