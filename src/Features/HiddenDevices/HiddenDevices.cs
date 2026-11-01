// HiddenDevices.cs — 記住使用者隱藏的裝置，被重新啟用時自動再隱藏
//
// 為什麼需要：DJ Mochi 原本直接把可見性寫進 Windows，自己不記錄。SteelSeries Sonar 開機時會重新打開自己的
// 虛擬裝置、驅動更新會讓裝置換成新的 ID，隱藏設定因此失效。設計見 docs/sdd/persistent-hidden-devices/SDD.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DJMochi
{
    // settings.json 的一筆記錄。欄位名稱就是 JSON 的鍵，所以用小寫
    class HiddenEntry
    {
        public string id;          // 隱藏當下的端點 ID，優先用它比對
        public int flow;           // 0 = 播放、1 = 錄音
        public string desc;        // Windows 給的原始名稱（不是使用者改過的名稱），ID 變了時用來比對
        public string dev;         // 硬體名稱
        public string added;       // 加入時間
        public string released;    // 不再自動隱藏的時間；null = 仍在作用中
        public string releasedBy;  // "user" = 在 DJ Mochi 按「顯示」；"other" = 在 DJ Mochi 以外被重新打開
    }

    class HiddenSettings
    {
        public int version = 1;
        public bool autoHide = true;
        public List<HiddenEntry> hiddenDevices = new List<HiddenEntry>();
    }

    // 一次檢查的結果，交給 MainWindow 決定怎麼提示
    class EnforceResult
    {
        public readonly List<Device> Hidden = new List<Device>();       // 這次自動隱藏的
        public readonly List<Device> Paused = new List<Device>();       // 拉鋸太多次，這次執行期間不再處理
        public readonly List<Device> Released = new List<Device>();     // 在外部被打開，視為使用者想顯示
        public readonly List<Device> NeedConfirm = new List<Device>();  // 是預設裝置，要使用者確認才隱藏
    }

    class HiddenDevices
    {
        // 啟動後這段時間內，被重新打開一律再隱藏。
        // 為什麼：開機時 SteelSeries GG / Sonar 可能比 DJ Mochi 晚啟動，再把裝置打開；這時使用者幾乎不可能
        // 正在 Windows 設定裡操作。過了這段時間，外部打開就當作使用者的意思，不再跟它搶
        static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(3);
        // 拉鋸保護：同一個裝置 60 秒內最多自動隱藏 3 次
        static readonly TimeSpan HideWindow = TimeSpan.FromSeconds(60);
        const int MaxHidesPerWindow = 3;

        readonly string file;
        readonly Func<Device, string> originalDesc;
        readonly DateTime graceEnd = DateTime.Now + StartupGrace;
        HiddenSettings settings;
        bool firstRun;

        // 以下只存在記憶體，程式結束就清掉
        readonly HashSet<string> seenHidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);  // 曾看到它是隱藏的
        readonly HashSet<string> skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // 這次執行期間不處理
        readonly Dictionary<string, List<DateTime>> hideTimes = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

        public HiddenDevices(Func<Device, string> originalDesc)
        {
            this.originalDesc = originalDesc;
            file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJMochi", "settings.json");
            Load();
        }

        public bool AutoHide
        {
            get { return settings.autoHide; }
            set { settings.autoHide = value; Save(); }
        }

        // ---------- 讀寫 ----------

        void Load()
        {
            settings = null;
            try
            {
                if (File.Exists(file))
                {
                    settings = new JavaScriptSerializer().Deserialize<HiddenSettings>(File.ReadAllText(file, Encoding.UTF8));
                    if (settings == null) throw new FormatException();
                    if (settings.hiddenDevices == null) settings.hiddenDevices = new List<HiddenEntry>();
                    settings.hiddenDevices.RemoveAll(e => e == null || string.IsNullOrEmpty(e.id));
                    return;
                }
            }
            catch
            {
                // 損毀：保留原檔成 .bak 供查看，不直接覆寫
                try { File.Copy(file, file + ".bak", true); } catch { }
                settings = null;
            }
            settings = new HiddenSettings();
            firstRun = true;   // 檔案不存在或損毀：第一次 Enforce 時用目前隱藏中的裝置建立初始清單
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
                // 先寫暫存檔再替換，寫到一半斷電也不會留下壞掉的設定檔
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
            }
            catch { }   // 存不了只是下次啟動不記得，不影響目前操作
        }

        static string Now() { return DateTime.Now.ToString("s"); }

        // ---------- 比對 ----------

        static bool SameId(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        // 先比 ID；ID 不同（驅動更新、換 USB 孔）時，比方向 + 硬體名稱 + 原始名稱
        HiddenEntry Find(Device d, bool includeReleased)
        {
            var list = settings.hiddenDevices.Where(e => includeReleased || e.released == null).ToList();
            var byId = list.FirstOrDefault(e => SameId(e.id, d.Id));
            if (byId != null) return byId;
            string desc = originalDesc(d);
            return list.FirstOrDefault(e => e.flow == d.Flow && e.dev == (d.Dev ?? "") && e.desc == desc);
        }

        // ---------- 使用者在 DJ Mochi 操作 ----------

        public void Remember(Device d)
        {
            var e = Find(d, true);
            if (e == null)
            {
                e = new HiddenEntry { added = Now() };
                settings.hiddenDevices.Add(e);
            }
            e.id = d.Id; e.flow = d.Flow; e.dev = d.Dev ?? ""; e.desc = originalDesc(d);
            e.released = null; e.releasedBy = null;
            seenHidden.Add(d.Id);
            skipped.Remove(d.Id);
            Save();
        }

        public void Forget(Device d)
        {
            Release(Find(d, false), "user");
            seenHidden.Remove(d.Id);
        }

        void Release(HiddenEntry e, string by)
        {
            if (e == null) return;
            e.released = Now(); e.releasedBy = by;
            Save();
        }

        // ---------- 檢查並自動隱藏 ----------

        public EnforceResult Enforce(List<Device> devices)
        {
            var r = new EnforceResult();
            if (firstRun)
            {
                firstRun = false;
                foreach (var d in devices.Where(x => !x.Enabled))
                    settings.hiddenDevices.Add(new HiddenEntry { id = d.Id, flow = d.Flow, dev = d.Dev ?? "", desc = originalDesc(d), added = Now() });
                Save();
            }

            bool changed = false;
            foreach (var d in devices)
            {
                if (d.Unplugged) continue;
                if (!d.Enabled) { seenHidden.Add(d.Id); continue; }
                if (!settings.autoHide || skipped.Contains(d.Id)) continue;
                var e = Find(d, false);
                if (e == null) continue;

                // 之前看到它是隱藏的、現在被打開了：過了啟動寬限期，就當作使用者在 Windows 設定裡打開的
                if (seenHidden.Contains(d.Id) && DateTime.Now > graceEnd)
                {
                    seenHidden.Remove(d.Id);
                    Release(e, "other");
                    r.Released.Add(d);
                    continue;
                }

                // 預設裝置：突然隱藏會沒聲音，先跳過，讓使用者在通知上確認
                if (d.IsDefault)
                {
                    skipped.Add(d.Id);
                    r.NeedConfirm.Add(d);
                    continue;
                }

                if (!AllowHide(d.Id))
                {
                    skipped.Add(d.Id);
                    r.Paused.Add(d);
                    continue;
                }

                if (Audio.SetVisibility(d.Id, false) != 0) continue;
                seenHidden.Add(d.Id);
                r.Hidden.Add(d);
                // 依名稱比對到的新 ID：記下來，之後直接用 ID 比對
                if (!SameId(e.id, d.Id)) { e.id = d.Id; changed = true; }
            }
            if (changed) Save();
            return r;
        }

        bool AllowHide(string id)
        {
            List<DateTime> times;
            if (!hideTimes.TryGetValue(id, out times)) hideTimes[id] = times = new List<DateTime>();
            var now = DateTime.Now;
            times.RemoveAll(t => now - t > HideWindow);
            if (times.Count >= MaxHidesPerWindow) return false;
            times.Add(now);
            return true;
        }

        // 使用者在通知上確認要隱藏預設裝置
        public bool HideConfirmed(Device d)
        {
            skipped.Remove(d.Id);
            if (Audio.SetVisibility(d.Id, false) != 0) return false;
            seenHidden.Add(d.Id);
            return true;
        }
    }
}
