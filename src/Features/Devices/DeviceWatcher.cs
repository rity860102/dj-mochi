// DeviceWatcher.cs — 自動偵測裝置與音量變化（取代手動重新整理）

using System;
using System.Runtime.InteropServices;

namespace DJMochi
{
    // 裝置變化：插拔、啟用 / 停用、隱藏 / 顯示、預設裝置、名稱。
    // 注意：Windows 在背景執行緒呼叫這些方法，而且插拔時常常一次連發好幾個，使用端要切回 UI 執行緒並合併處理。
    class DeviceWatcher : IMMNotificationClient, IDisposable
    {
        static readonly Guid DeviceDescKey = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");   // pid 2 = 名稱、14 = 完整名稱
        static readonly Guid DeviceInterfaceKey = new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"); // pid 6 = 硬體名稱

        IMMDeviceEnumerator enumr;
        public event Action Changed;

        public void Start()
        {
            if (enumr != null) return;
            enumr = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumr.RegisterEndpointNotificationCallback(this) != 0)
            {
                Marshal.ReleaseComObject(enumr);
                enumr = null;
            }
        }

        void Raise()
        {
            try { var h = Changed; if (h != null) h(); } catch { }   // 例外不能丟回 Windows
        }

        public int OnDeviceStateChanged(string id, int state) { Raise(); return 0; }
        public int OnDeviceAdded(string id) { Raise(); return 0; }
        public int OnDeviceRemoved(string id) { Raise(); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { Raise(); return 0; }

        // 屬性變化非常頻繁（有些驅動會一直送），只關心名稱相關的屬性
        public int OnPropertyValueChanged(string id, PROPERTYKEY key)
        {
            if ((key.fmtid == DeviceDescKey && (key.pid == 2 || key.pid == 14)) ||
                (key.fmtid == DeviceInterfaceKey && key.pid == 6))
                Raise();
            return 0;
        }

        public void Dispose()
        {
            if (enumr == null) return;
            try { enumr.UnregisterEndpointNotificationCallback(this); } catch { }
            try { Marshal.ReleaseComObject(enumr); } catch { }
            enumr = null;
        }
    }

    // 單一裝置的音量 / 靜音變化。由 Audio 在建立 IAudioEndpointVolume 時訂閱
    class VolumeCallback : IAudioEndpointVolumeCallback
    {
        readonly string id;
        public VolumeCallback(string id) { this.id = id; }

        // AUDIO_VOLUME_NOTIFICATION_DATA：Guid guidEventContext(16) / BOOL bMuted(4) / float fMasterVolume(4) / ...
        public int OnNotify(IntPtr data)
        {
            try
            {
                var context = (Guid)Marshal.PtrToStructure(data, typeof(Guid));
                if (context == Audio.OwnContext) return 0;   // 自己調的，畫面已經是對的
                bool muted = Marshal.ReadInt32(data, 16) != 0;
                float vol = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
                Audio.RaiseVolumeChanged(id, vol, muted);
            }
            catch { }
            return 0;
        }
    }
}
