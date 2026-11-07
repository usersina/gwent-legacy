using System;
using Gwent.Core;
using Gwent.Net;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // The other device of a LAN battle away, said over the board (dimmed, its clicks and keys taken: it waits meanwhile)
    // in the battle notices' band: who is away, and a minute counting down (01:00, 00:59...) to a way out: the host claims
    // the win (its guest left the battle), the guest gives up (counted as a loss). A battle that cannot go on (the host
    // lost it, the other player left) says why, with the way back to the main menu.
    public class LanNotice : MonoBehaviour
    {
        private const float WayOutAfter = 60f;
        private const float Dim = 0.55f;

        private LanSession session;
        private bool over;
        private TextMeshProUGUI text;
        private Button wayOut;

        public static LanNotice Show(LanSession session, bool over, Action way_out)
        {
            RectTransform root = UIKit.Rect("LanNotice", UIOverlay.Root).Stretch();
            Image veil = UIKit.NewImage(root, "Veil", null, new Color(0, 0, 0, Dim), true); // takes the clicks
            veil.rectTransform.Stretch();
            LanNotice notice = root.gameObject.AddComponent<LanNotice>();
            notice.session = session;
            notice.over = over;

            RectTransform band = BattleBanner.Band("Band", root);
            notice.text = BattleBanner.Text(band);
            notice.text.rectTransform.sizeDelta = new Vector2(1400, band.sizeDelta.y);
            string label = Loc.T(Loc.UI, over ? "pause.main_menu" : session.Hosting ? "lan.claim_win" : "lan.give_up");
            notice.wayOut = UIKit.Button(root, label, UIKit.ButtonStyle.Secondary, () => way_out(), new Vector2(320, 64));
            ((RectTransform)notice.wayOut.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(320, 64));
            notice.wayOut.gameObject.SetActive(over);
            notice.Refresh();
            return notice;
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (over)
            {
                text.text = Loc.T(Loc.UI, "lan.notice_over", session.Problem ?? Loc.T(Loc.UI, "lan.notice_gone"));
                return;
            }
            int left = Mathf.Max(0, Mathf.CeilToInt(WayOutAfter - session.AwayFor));
            string time = left > 0 ? "  " + (left / 60).ToString("00") + ":" + (left % 60).ToString("00") : "";
            text.text = left > 0
                ? Loc.T(Loc.UI, session.Hosting ? "lan.notice_lost_host" : "lan.notice_lost_guest", session.TheirName, time)
                : Loc.T(Loc.UI, session.Hosting ? "lan.notice_away_host" : "lan.notice_away_guest", session.TheirName);
            if (!wayOut.gameObject.activeSelf && session.AwayFor >= WayOutAfter)
            {
                wayOut.gameObject.SetActive(true);
                UIKit.Focus(wayOut);
            }
        }
    }
}
