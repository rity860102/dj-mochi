// Program.cs — 程式進入點：防多開、開機縮在右下角

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
