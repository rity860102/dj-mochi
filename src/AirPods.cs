// AirPods.cs — 監聽 AirPods 的 BLE 廣播並解析電量（依公開的 Apple Continuity 格式文件自行實作）
//
// 格式（Apple 廠商 ID 0x004C，資料第 0 位元組 0x07 = Proximity Pairing）：
//   [1] 長度  [2] 前綴  [3-4] 型號（大端序）  [5] 狀態（高 4 bit 含主耳旗標）
//   [6] 左右耳電量（各 4 bit，順序由主耳旗標決定）
//   [7] 高 4 bit 充電旗標（bit0/bit1 = 兩耳、bit2 = 充電盒）、低 4 bit 充電盒電量
//   電量 0~10 → ×10%（10 = 100%），15 = 沒有連線
// 限制：精度 10%；收進盒子蓋上後不廣播；藍牙位址約 15 分鐘輪換一次，附近有多副時只能用型號與訊號強度判斷。

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace DJMochi
{
    class AirPodsStatus
    {
        public ushort Model;
        public int Left = -1, Right = -1, Case = -1;      // 0~100；-1 = 未知 / 沒有連線
        public bool ChargingLeft, ChargingRight, ChargingCase;
        public short Rssi;
        public ulong Address;
        public DateTime Time;
        public byte[] Raw;

        public string ModelName { get { return AirPodsParser.ModelName(Model); } }

        public string Hex
        {
            get { return Raw == null ? "" : BitConverter.ToString(Raw).Replace("-", " "); }
        }
    }

    static class AirPodsParser
    {
        public const ushort AppleCompanyId = 0x004C;

        static readonly Dictionary<ushort, string> Models = new Dictionary<ushort, string>
        {
            { 0x0220, "AirPods" }, { 0x0F20, "AirPods 2" }, { 0x1320, "AirPods 3" },
            { 0x1920, "AirPods 4" }, { 0x1B20, "AirPods 4 (ANC)" },
            { 0x0E20, "AirPods Pro" }, { 0x1420, "AirPods Pro 2" }, { 0x2420, "AirPods Pro 2 (USB-C)" },
            { 0x2720, "AirPods Pro 3" },   // 實機確認
            { 0x0A20, "AirPods Max" }
        };

        public static string ModelName(ushort model)
        {
            string n;
            return Models.TryGetValue(model, out n) ? n : string.Format("AirPods (0x{0:X4})", model);
        }

        static int Level(int nibble) { return nibble <= 10 ? nibble * 10 : -1; }

        // 不是 AirPods 的 Proximity Pairing 廣播就回傳 null
        public static AirPodsStatus Parse(byte[] b)
        {
            if (b == null || b.Length < 8 || b[0] != 0x07) return null;
            var s = new AirPodsStatus { Raw = b, Time = DateTime.Now };
            s.Model = (ushort)((b[3] << 8) | b[4]);

            bool flipped = ((b[5] >> 4) & 0x02) == 0;   // 主耳旗標：決定左右耳的順序
            int hi = (b[6] >> 4) & 0xF, lo = b[6] & 0xF;
            s.Left  = Level(flipped ? hi : lo);
            s.Right = Level(flipped ? lo : hi);
            s.Case  = Level(b[7] & 0xF);

            int chg = (b[7] >> 4) & 0xF;
            s.ChargingLeft  = (chg & (flipped ? 0x02 : 0x01)) != 0;
            s.ChargingRight = (chg & (flipped ? 0x01 : 0x02)) != 0;
            s.ChargingCase  = (chg & 0x04) != 0;
            return s;
        }
    }

    // 註：編譯時要參考 System.Runtime.InteropServices.WindowsRuntime.dll，C# 5 才能用 += 訂閱 WinRT 事件；
    //     IBuffer 不用 ToArray()（會用到完整 Windows SDK 的 Windows.winmd），改用 DataReader 讀取。
    class AirPodsMonitor : IDisposable
    {
        BluetoothLEAdvertisementWatcher watcher;

        // 注意：在背景執行緒觸發，更新 UI 前要切回 UI 執行緒
        public event Action<AirPodsStatus> Updated;

        public void Start()
        {
            if (watcher != null) return;
            watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            // 只收 Apple 的廣播
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData { CompanyId = AirPodsParser.AppleCompanyId });
            watcher.Received += OnReceived;
            watcher.Start();
        }

        public BluetoothLEAdvertisementWatcherStatus Status
        {
            get { return watcher == null ? BluetoothLEAdvertisementWatcherStatus.Created : watcher.Status; }
        }

        void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            foreach (var md in args.Advertisement.ManufacturerData)
            {
                if (md.CompanyId != AirPodsParser.AppleCompanyId) continue;
                var bytes = new byte[md.Data.Length];
                DataReader.FromBuffer(md.Data).ReadBytes(bytes);
                var s = AirPodsParser.Parse(bytes);
                if (s == null) continue;
                s.Rssi = args.RawSignalStrengthInDBm;
                s.Address = args.BluetoothAddress;
                var h = Updated;
                if (h != null) h(s);
            }
        }

        public void Dispose()
        {
            if (watcher == null) return;
            try { watcher.Received -= OnReceived; watcher.Stop(); } catch { }
            watcher = null;
        }
    }
}
