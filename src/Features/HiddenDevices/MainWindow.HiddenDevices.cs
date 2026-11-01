// MainWindow.HiddenDevices.cs — 記住隱藏的裝置：何時檢查、狀態提示、預設裝置的確認通知

using System;
using System.Collections.Generic;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace DJMochi
{
    partial class MainWindow
    {
        HiddenDevices hiddenDevices;
        List<Device> pendingConfirm;   // 通知上等使用者點擊確認隱藏的預設裝置

        void InitHiddenDevices()
        {
            hiddenDevices = new HiddenDevices(OriginalDesc);
            tray.BalloonTipClicked += delegate { ConfirmPendingHide(); };
            // 開機以 --tray 啟動時視窗不會顯示、ContentRendered 不會觸發，所以在這裡主動檢查一次
            EnforceHidden();
        }

        // 用 Windows 給的原始名稱比對：使用者改過名的裝置，驅動更新後以預設名稱重新出現時也認得出來
        string OriginalDesc(Device d)
        {
            string o;
            return orig.TryGetValue(d.Id, out o) ? o : d.Desc;
        }

        // 裝置有變化、啟動、從右下角叫回時呼叫；之後呼叫端的 RefreshAll() 會反映最新狀態
        void EnforceHidden()
        {
            if (hiddenDevices == null) return;
            EnforceResult r;
            try
            {
                var all = Audio.Load(0);
                all.AddRange(Audio.Load(1));
                r = hiddenDevices.Enforce(all);
            }
            catch { return; }   // 讀不到裝置清單就等下次通知，不影響其他功能

            if (r.Hidden.Count == 1) SetStatus(Lang.Tr("st.autoHidden", r.Hidden[0].Desc));
            else if (r.Hidden.Count > 1) SetStatus(Lang.Tr("st.autoHiddenMany", r.Hidden[0].Desc, r.Hidden.Count));
            foreach (var d in r.Released) SetStatus(Lang.Tr("st.autoHideReleased", d.Desc));
            foreach (var d in r.Paused) SetStatus(Lang.Tr("st.autoHidePaused", d.Desc));

            if (r.NeedConfirm.Count > 0 && tray != null)
            {
                string names = string.Join("、", r.NeedConfirm.Select(d => d.Desc));
                ShowTip(8000, Lang.Tr("tip.confirmDefaultTitle"), Lang.Tr("tip.confirmDefault", names), WinForms.ToolTipIcon.Info);
                pendingConfirm = r.NeedConfirm;   // 要在 ShowTip 之後設，ShowTip 會清掉舊的
            }
        }

        void ConfirmPendingHide()
        {
            var list = pendingConfirm;
            pendingConfirm = null;
            if (list == null) return;
            try
            {
                var hidden = list.Where(d => hiddenDevices.HideConfirmed(d)).ToList();
                if (hidden.Count > 0) SetStatus(Lang.Tr("st.hidden", string.Join("、", hidden.Select(d => d.Desc))));
            }
            catch { }
            RefreshAll();
        }

        // 所有通知都經過這裡：換成別的通知時，舊的「點這裡隱藏」就失效，避免點到別的通知卻隱藏了裝置
        void ShowTip(int timeout, string title, string text, WinForms.ToolTipIcon icon)
        {
            if (tray == null) return;
            pendingConfirm = null;
            tray.ShowBalloonTip(timeout, title, text, icon);
        }

        void ToggleAutoHide()
        {
            hiddenDevices.AutoHide = !hiddenDevices.AutoHide;
            SetStatus(Lang.Tr(hiddenDevices.AutoHide ? "st.autoHideOn" : "st.autoHideOff"));
            if (hiddenDevices.AutoHide) EnforceHidden();
            RefreshAll();
        }
    }
}
