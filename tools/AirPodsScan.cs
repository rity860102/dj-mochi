// AirPodsScan.cs — 命令列測試工具：監聽附近 AirPods 廣播並印出解析結果
// 建置與執行：pwsh -File tools\airpods-scan.ps1 [-Seconds 30]
// 隱私：藍牙位址只顯示後 4 碼。原始封包（Hex）不含位址，但貼到 issue 前仍建議確認內容。

using System;
using System.Collections.Generic;
using System.Threading;

namespace DJMochi
{
    static class AirPodsScan
    {
        static string L(int v) { return v < 0 ? "  --" : string.Format("{0,3}%", v); }
        static string C(bool b) { return b ? "+" : " "; }   // 充電中

        static void Main(string[] args)
        {
            int seconds = args.Length > 0 ? int.Parse(args[0]) : 30;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var paired = AirPodsParser.PairedModels();
            Console.WriteLine("已配對的 AirPods 型號：{0}", paired.Count == 0 ? "（無）" :
                string.Join("、", System.Linq.Enumerable.Select(paired, x => string.Format("{0}（0x{1:X4}）", AirPodsParser.ModelName(x), x))));
            var seen = new HashSet<string>();
            var gate = new object();

            using (var m = new AirPodsMonitor())
            {
                m.Updated += delegate(AirPodsStatus s)
                {
                    string key = s.Address + "|" + s.Hex;
                    lock (gate)
                    {
                        if (!seen.Add(key)) return;
                        Console.WriteLine("{0:HH:mm:ss} {11} {1,-22} RSSI {2,4}  左 {3}{4}  右 {5}{6}  盒 {7}{8}  addr …{9:X4}  [{10}]",
                            s.Time, s.ModelName, s.Rssi,
                            L(s.Left), C(s.ChargingLeft), L(s.Right), C(s.ChargingRight), L(s.Case), C(s.ChargingCase),
                            s.Address & 0xFFFF, s.Hex, paired.Contains(s.Model) ? "[我的型號]" : "[其他]    ");
                    }
                };
                m.Start();
                Console.WriteLine("監聽 {0} 秒中…（watcher 狀態：{1}）", seconds, m.Status);
                Thread.Sleep(seconds * 1000);
                Console.WriteLine("結束。watcher 狀態：{0}，共 {1} 種不同的廣播內容。", m.Status, seen.Count);
            }
        }
    }
}
