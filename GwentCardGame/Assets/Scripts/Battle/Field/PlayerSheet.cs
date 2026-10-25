using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // Everything about a side at a glance, opened by a tap or a click on its side panel (the whole band: portrait, name,
    // faction, gems, score): its leader's face, name and faction; its faction's ability; its leader's ability and its state
    // (ready, used, passive, cancelled); the cards in its hand, its deck and its discard pile. It opens beside the band, so
    // the band stays in sight; the ×, a click outside it, Escape or Back close it. The battle waits while it is open (not a
    // LAN battle's: it goes on on the other device).
    public class PlayerSheet : MonoBehaviour
    {
        private const float Width = 600;
        private const float Pad = 26;
        private const float Face = 88;
        private const float IconColumn = 70;

        private static PlayerSheet current;
        private float previousTimeScale;
        private bool stopped;

        public static bool IsOpen { get { return current != null; } }

        // Whose sheet is open (for the tools): true for the player's
        public static bool ShowsPlayer { get; private set; }

        public static void Close()
        {
            if (current != null)
                UIOverlay.CloseModal(current.gameObject);
        }

        public static void Open(bool is_player)
        {
            BattleController battle = BattleController.Instance;
            if (current != null || battle == null || battle.IsBattleOver)
                return;
            BattleController.BattleSide side = is_player ? battle.PlayerInfo : battle.EnemyInfo;
            SidePanel band = battle.PanelOf(is_player);
            LeaderSlot slot = battle.LeaderOf(is_player);
            if (side == null || band == null)
                return;
            UITheme theme = UITheme.Current;
            GameAudio.PlaySfx("card_select");

            // Over the whole screen: a click outside the sheet closes it
            RectTransform root = UIKit.Rect("PlayerSheet", UIOverlay.Root).Stretch();
            Image outside = UIKit.NewImage(root, "Outside", null, Color.clear, true);
            outside.rectTransform.Stretch();
            outside.gameObject.AddComponent<CloseOnClick>();
            current = root.gameObject.AddComponent<PlayerSheet>();
            ShowsPlayer = is_player;
            current.previousTimeScale = Time.timeScale;
            current.stopped = !(Gwent.Net.LanSession.Current != null && Gwent.Net.LanSession.Current.State == Gwent.Net.LanSession.Stage.Battle);
            if (current.stopped)
                Time.timeScale = 0f;

            RectTransform panel = UIKit.Panel(root, "Sheet", true);
            panel.Find("Outline").GetComponent<Image>().color = theme.gold;
            float y = Pad;

            // The leader's face, the side's name and faction
            Image ring = UIKit.NewImage(panel, "Ring", theme.ring, theme.gold);
            ring.rectTransform.Place(new Vector2(0, 1), new Vector2(Pad - 4, -y + 4), new Vector2(Face + 8, Face + 8), new Vector2(0, 1));
            Image mask = UIKit.NewImage(panel, "Portrait", theme.circle, Color.white);
            mask.rectTransform.Place(new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(Face, Face), new Vector2(0, 1));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Image art = UIKit.NewImage(mask.transform, "Art", null, Color.white);
            CardArt.ShowPortrait(art, side.Leader);
            float text_x = Pad + Face + 20, text_width = Width - text_x - Pad - 50;
            TextMeshProUGUI name = Line(panel, side.Name, UIKit.TextStyle.Heading, 32, theme.goldBright, text_x, y + 12, text_width, false);
            Line(panel, side.Faction.DisplayName(), UIKit.TextStyle.Body, 24, theme.textSecondary, text_x, y + 12 + name.preferredHeight + 2, text_width, false);
            y += Face + 18;

            Button close = UIKit.RoundButton(panel, "Close", "×", null, Close, 44);
            ((RectTransform)close.transform).Place(new Vector2(1, 1), new Vector2(-Pad + 6, -Pad + 6), new Vector2(44, 44), new Vector2(1, 1));

            // The faction's ability
            y = Rule(panel, y);
            float row = y;
            Image shield = UIKit.NewImage(panel, "Shield", SpriteCache.Load("Stats/faction/" + side.Faction.Code()), Color.white);
            shield.preserveAspect = true;
            shield.rectTransform.Place(new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(52, 60), new Vector2(0, 1));
            y = Section(panel, "Faction ability", side.Faction.Ability() ?? "None", y);
            y = Mathf.Max(y, row + 60) + 16;

            // The leader's ability and its state
            y = Rule(panel, y);
            row = y;
            Sprite crown = slot != null ? slot.CrownSprite : null;
            if (crown != null)
            {
                Image icon = UIKit.NewImage(panel, "Crown", crown, theme.gold);
                icon.preserveAspect = true;
                icon.rectTransform.Place(new Vector2(0, 1), new Vector2(Pad + 4, -y - 2), new Vector2(44, 44), new Vector2(0, 1));
            }
            Card leader = battle.GetCard(side.Leader);
            string ability = CardDescription.LeaderAbility(side.Leader) ?? "";
            y = Section(panel, "Leader: " + (leader != null ? leader.DisplayName : "?"), ability, y);
            if (slot != null)
            {
                bool usable;
                string state = slot.StateText(out usable);
                TextMeshProUGUI chip = Line(panel, state, UIKit.TextStyle.Body, 22, usable ? theme.goldBright : theme.textMuted, Pad + IconColumn, y + 4,
                    Width - 2 * Pad - IconColumn, true);
                chip.fontStyle = FontStyles.Italic;
                y += chip.preferredHeight + 4;
            }
            y = Mathf.Max(y, row + 52) + 16;

            // Its cards
            y = Rule(panel, y);
            string[] what = { "in hand", "in deck", "discarded" };
            int[] counts = { side.HandList.Count, side.DeckList.Count, side.DiscardList.Count };
            float column = (Width - 2 * Pad) / 3;
            for (int i = 0; i < 3; i++)
            {
                Line(panel, counts[i].ToString(), UIKit.TextStyle.Heading, 40, theme.text, Pad + i * column, y, column, false).name = "Count " + what[i];
                Line(panel, what[i], UIKit.TextStyle.Caption, 22, theme.textSecondary, Pad + i * column, y + 48, column, false);
            }
            y += 48 + 28 + Pad;

            panel.sizeDelta = new Vector2(Width, y);
            current.PlaceBeside(panel, band);
            UIOverlay.PushModal(root.gameObject, current.Closed);
            UIMotion.Appear(panel.gameObject);
        }

        // Beside the band, on the board's side of it (to its right), level with it; kept on the screen
        private void PlaceBeside(RectTransform panel, SidePanel side)
        {
            RectTransform root = (RectTransform)transform;
            RectTransform band = side.Band;
            if (band == null)
                return;
            Rect screen = CardMotion.ScreenRect(band);
            Vector2 at;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(screen.xMax, screen.center.y), null, out at);
            float half = root.rect.height / 2 - 16;
            float top = Mathf.Clamp(at.y + panel.sizeDelta.y / 2, -half + panel.sizeDelta.y, half);
            float left = Mathf.Min(at.x + 16, root.rect.width / 2 - 16 - panel.sizeDelta.x);
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(left, top), panel.sizeDelta, new Vector2(0, 1));
        }

        // A section: its title (gold capitals) and its text below, right of the icon column; returns where it ends
        private static float Section(RectTransform panel, string title, string text, float y)
        {
            UITheme theme = UITheme.Current;
            float x = Pad + IconColumn, width = Width - x - Pad;
            TextMeshProUGUI heading = Line(panel, title.ToUpperInvariant(), UIKit.TextStyle.Tab, 19, theme.gold, x, y, width, true);
            y += heading.preferredHeight + 4;
            TextMeshProUGUI body = Line(panel, text, UIKit.TextStyle.Body, 24, theme.text, x, y, width, true);
            return y + body.preferredHeight;
        }

        // A text at a place of the sheet (from its top left), wrapped to the width or on one line
        private static TextMeshProUGUI Line(RectTransform panel, string text, UIKit.TextStyle style, float size, Color color, float x, float y, float width, bool wrap)
        {
            TextMeshProUGUI label = UIKit.Text(panel, text, style);
            label.fontSize = size;
            label.color = color;
            label.richText = false;
            if (wrap)
                label.textWrappingMode = TextWrappingModes.Normal;
            else
                label.overflowMode = TextOverflowModes.Ellipsis;
            float height = label.GetPreferredValues(text, width, 0).y;
            label.rectTransform.Place(new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height), new Vector2(0, 1));
            return label;
        }

        // A thin line across the sheet; returns where what follows it starts
        private static float Rule(RectTransform panel, float y)
        {
            Image line = UIKit.NewImage(panel, "Rule", null, UITheme.Current.line);
            line.rectTransform.Place(new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(Width - 2 * Pad, 2), new Vector2(0, 1));
            return y + 16;
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

        private sealed class CloseOnClick : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData e)
            {
                PlayerSheet.Close();
            }
        }
    }
}
