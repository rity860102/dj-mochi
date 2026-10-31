// Device.cs — 音訊裝置資料

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
}
