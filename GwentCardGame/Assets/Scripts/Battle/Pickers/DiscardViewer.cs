using System.Collections.Generic;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // A discard pile in the carousel, as The Witcher 3 shows it (click a pile on the board), its top card in front. The
    // battle waits while it is open (not a LAN battle's: it goes on on the other device, and a screen held still would
    // hold its turn's clock too); Close, Enter, Escape or a right click puts it away.
    public class DiscardViewer : MonoBehaviour
    {
        private static DiscardViewer current;
        private float previousTimeScale;
        private bool stopped; // it stopped the time

        public static bool IsOpen { get { return current != null; } }

        // Closes it if it is open (the battle ends under it), restoring the time scale
        public static void Close()
        {
            if (current != null)
                UIOverlay.CloseModal(current.gameObject);
        }

        public static void Open(List<int> cards, bool is_player)
        {
            if (current != null || cards == null || cards.Count == 0)
                return;
            // Over the whole screen, so the board under it takes no click
            RectTransform root = UIKit.Rect("DiscardViewer", UIOverlay.Root).Stretch();
            Image blocker = root.gameObject.AddComponent<Image>();
            blocker.color = Color.clear;
            current = root.gameObject.AddComponent<DiscardViewer>();
            current.previousTimeScale = Time.timeScale;
            current.stopped = !(Gwent.Net.LanSession.Current != null && Gwent.Net.LanSession.Current.State == Gwent.Net.LanSession.Stage.Battle);
            if (current.stopped)
                Time.timeScale = 0f;

            string whose = Loc.T(Loc.UI, is_player ? "pick.your_discard" : "pick.opp_discard");
            CardPicker picker = CardPicker.Open(root, whose + "  (" + cards.Count + ")", cards, null, () => UIOverlay.CloseModal(root.gameObject), cards.Count - 1);
            picker.StandIn(root.gameObject);
            UIOverlay.PushModal(root.gameObject, current.Closed);
        }

        private void Closed()
        {
            if (stopped)
                Time.timeScale = previousTimeScale > 0 ? previousTimeScale : 1f;
            if (current == this)
                current = null;
        }

        private void OnDestroy()
        {
            if (current == this)
                current = null; // gone with the scene (which sets its own time scale)
        }
    }
}
