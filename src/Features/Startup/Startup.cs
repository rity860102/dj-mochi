// Startup.cs — 開機自動啟動（HKCU Run）

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
}
