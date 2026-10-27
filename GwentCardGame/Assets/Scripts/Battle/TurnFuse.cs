using Gwent.Net;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // A LAN battle's turn clock, its last seconds (Hearthstone's rope): a fuse along the line between the two halves of the
    // board (during your redraw, along its band's foot) burns from its right end to its left as the side waited for runs
    // out of time, a spark at its burning end and the seconds left at its start; yours first, else the opponent's. It
    // holds still while the clock does (a screen catching up, the other device away). Out of time, the side loses the
    // round (LanSession's clock, Match.TimeOut).
    public class TurnFuse : MonoBehaviour
    {
        private const float Shown = 15f;   // the last seconds burn
        private const float Hurry = 5f;    // the last of them redder
        private const float Height = 4f;

        private BattleController battle;
        private LanSession lan;
        private RectTransform root, burning, spark;
        private Image line, sparkImage, core;
        private TextMeshProUGUI seconds;
        private CanvasGroup group;
        private Transform placedIn;

        public static void Attach(BattleController battle, LanSession lan)
        {
            TurnFuse fuse = battle.gameObject.AddComponent<TurnFuse>();
            fuse.battle = battle;
            fuse.lan = lan;
        }

        private void Update()
        {
            if (lan == null || battle.IsBattleOver || !Waited(out bool mine, out float left, out bool running) || left > Shown)
            {
                Hide();
                return;
            }
            if (root == null)
                Build();
            Place(mine);
            float fraction = Mathf.Clamp01(left / Shown);
            float width = root.rect.width;
            burning.sizeDelta = new Vector2(width * fraction, Height);
            spark.anchoredPosition = new Vector2(width * fraction, 0);

            UITheme theme = UITheme.Current;
            Color ember = left <= Hurry ? new Color(1f, 0.42f, 0.18f) : new Color(1f, 0.66f, 0.26f);
            line.color = ember;
            float flicker = running ? Mathf.PerlinNoise(Time.unscaledTime * 9f, 0.3f) : 0.5f;
            sparkImage.color = UITheme.WithAlpha(Color.Lerp(ember, theme.goldBright, 0.3f), running ? 0.65f + 0.35f * flicker : 0.35f);
            spark.localScale = Vector3.one * (running ? 0.8f + 0.45f * flicker : 0.7f);
            core.color = running ? new Color(1f, 0.95f, 0.8f) : UITheme.WithAlpha(ember, 0.6f);
            seconds.text = Mathf.CeilToInt(left).ToString();
            seconds.color = left <= Hurry ? ember : theme.goldBright;
            group.alpha = Mathf.MoveTowards(group.alpha, 1, Time.unscaledDeltaTime * 4);
        }

        // The side whose clock shows: yours while you are waited for, else the opponent's
        private bool Waited(out bool mine, out float left, out bool running)
        {
            mine = true;
            if (lan.Clock(true, out left, out running))
                return true;
            mine = false;
            return lan.Clock(false, out left, out running);
        }

        private void Build()
        {
            UITheme theme = UITheme.Current;
            root = UIKit.Rect("TurnFuse", UIOverlay.FixedRoot);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0;
            Image track = UIKit.NewImage(root, "Track", null, UITheme.WithAlpha(theme.gold, 0.14f));
            track.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 2), new Vector2(0, 0.5f));
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, 2);
            line = UIKit.NewImage(root, "Burning", null, Color.white);
            burning = line.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, Height), new Vector2(0, 0.5f));
            sparkImage = UIKit.NewImage(root, "Spark", theme.radial, Color.white);
            spark = sparkImage.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(54, 54));
            core = UIKit.NewImage(spark, "Core", theme.circle, Color.white);
            core.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            seconds = UIKit.Text(root, "", UIKit.TextStyle.Heading, TextAlignmentOptions.MidlineRight);
            seconds.fontSize = 26;
            seconds.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(-14, 1), new Vector2(60, 34), new Vector2(1, 0.5f));
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>())
                graphic.raycastTarget = false;
        }

        // In the line between the halves of the board (under the cards and their highlights; it moves with a zoom), else
        // along it on the overlay; your redraw's: along its band's foot, over the board
        private void Place(bool mine)
        {
            RectTransform band = mine ? battle.RedrawBand : null;
            RectTransform divider = battle.Divider;
            Transform parent = band != null ? band : divider != null ? divider : UIOverlay.FixedRoot;
            if (placedIn != parent)
            {
                placedIn = parent;
                root.SetParent(parent, false);
                root.SetAsLastSibling();
            }
            if (band != null)
            {
                root.anchorMin = new Vector2(0, 0);
                root.anchorMax = new Vector2(1, 0);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.offsetMin = new Vector2(320, -1);
                root.offsetMax = new Vector2(-320, 1);
                return;
            }
            if (divider != null)
            {
                root.anchorMin = new Vector2(0, 0.5f);
                root.anchorMax = new Vector2(1, 0.5f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.offsetMin = new Vector2(BattleController.DividerInset, -1);
                root.offsetMax = new Vector2(-BattleController.DividerInset, 1);
                return;
            }
            CardMotion.ToLayout(battle.MiddleScreenRect, out Vector2 center, out Vector2 size);
            root.Place(new Vector2(0.5f, 0.5f), center, new Vector2(size.x, 2));
        }

        private void Hide()
        {
            if (root != null)
                Destroy(root.gameObject);
            root = null;
            placedIn = null;
        }

        private void OnDestroy()
        {
            Hide();
        }
    }
}
