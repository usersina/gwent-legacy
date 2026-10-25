using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gwent.Core;
using Gwent.Net;
using Gwent.UI;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // The players of a LAN battle talking (LanSession, Chat), as in Rocket League. The quick chat: E (or a click on your
    // portrait) pops The Witcher 3's quick lines up beside your portrait, numbered; 1 to 8 (or a click) says one, and the
    // list goes. The chat window (T, or your portrait's "..." badge) opens at the foot of the screen in the middle, over
    // your hand: what was said this session (quick lines too; it scrolls, its bar showing once there is more than it
    // holds) and a message to type, with the speaker that mutes the other player and the × that hides it. What either says
    // pops up in a bubble out of their portrait on both screens (yours under it, the opponent's over it; on the end screen,
    // out of its scoreboard's, left of the board); one line every second and a half.
    public class ChatView : MonoBehaviour
    {
        private const float Every = 1.5f;        // between two of yours
        private const float BubbleHeight = 52;   // a line's; more lines, taller
        private const float BubblePad = 18;
        private const float BoardBubbleText = 340; // a board bubble's lines, short of the rows' scores
        private const float EndBubbleText = 400;
        private const float LeastWidth = 760;
        private const float Pad = 14;
        private const float HeaderHeight = 38;
        private const float LogHeight = 240;
        private const float FieldHeight = 50;
        private const float SendWidth = 120;
        private const float QuickWidth = 320;
        private const float QuickLine = 42;
        private const float QuickPad = 10;

        private static ChatView instance;
        private BattleController battle;
        private LanSession lan;
        private SidePanel mine, theirs;
        private EndScreen end;
        private bool endShown;  // the end screen's scoreboard is up: bubbles and the quick lines come out of it
        private GameObject badge, endBadge;
        private GameObject mark, endMark; // the quick chat's speech bubble on the portrait's corner
        private GameObject panel;  // the chat window
        private GameObject quick;  // the quick lines
        private float panelWidth;  // the window's: your hand's
        private TextMeshProUGUI title;
        private RectTransform logContent;
        private ScrollRect logScroll;
        private TMP_InputField field;
        private Button send;
        private readonly List<Button> lines = new List<Button>();
        private readonly GameObject[] bubbles = new GameObject[2]; // yours, theirs
        private float sentAt = float.MinValue;
        private int openedAt = -1; // the frame the window or the quick lines opened in (the key that did is not theirs)

        public static void Attach(BattleController battle, LanSession lan, SidePanel mine, SidePanel theirs)
        {
            ChatView view = battle.gameObject.AddComponent<ChatView>();
            view.battle = battle;
            view.lan = lan;
            view.mine = mine;
            view.theirs = theirs;
            view.end = battle.PostGame.GetComponent<EndScreen>();
            lan.Heard += view.Heard;
            RectTransform picture = mine.Picture;
            picture.GetComponent<Image>().raycastTarget = true;
            view.badge = view.Badge(picture, out view.mark);
            // On a phone the portrait takes a finger over a larger circle (behind the badge and the mark, which keep theirs)
            if (GameInput.IsTouch)
            {
                Image area = UIKit.NewImage(picture.parent, "TalkArea", UITheme.Current.circle, Color.clear, true);
                area.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140, 140));
                area.rectTransform.localPosition = picture.localPosition;
                area.transform.SetAsFirstSibling();
            }
            PortraitClick click = picture.parent.gameObject.AddComponent<PortraitClick>();
            click.view = view;
            click.badge = view.badge.transform;
            instance = view;
        }

        // T: the chat window, to type; E: the quick lines (their own keys, and the window's, while they are open)
        public static void KeyPressed(bool typing)
        {
            if (instance == null)
                return;
            if (typing)
                instance.ToggleWindow(true);
            else
                instance.ToggleQuick();
        }

        private void Update()
        {
            if (lan == null)
            {
                // The session is over (the other player left): nothing more to say
                CloseAll();
                foreach (GameObject talk in new[] { badge, endBadge, mark, endMark })
                    if (talk != null)
                        Destroy(talk);
                return;
            }
            RectTransform scored = battle.IsBattleOver && end != null ? end.Portrait(true) : null;
            if (scored != null && !endShown)
            {
                // To the end screen: what came out of the board's portraits goes, its own portrait (and badge) takes over
                endShown = true;
                CloseQuick();
                foreach (GameObject bubble in bubbles)
                    if (bubble != null)
                        Destroy(bubble);
                scored.GetComponent<Image>().raycastTarget = true;
                endBadge = Badge(scored, out endMark);
                PortraitClick click = scored.gameObject.AddComponent<PortraitClick>();
                click.view = this;
                click.badge = endBadge.transform;
            }
            if (panel == null && quick == null)
                return;
            if (!lan.Linked)
            {
                CloseAll(); // the other player away: the notice over the board says so
                return;
            }
            RefreshButtons();
            Keys();
        }

        private void OnDestroy()
        {
            if (lan != null)
                lan.Heard -= Heard;
            CloseAll();
            foreach (GameObject bubble in bubbles)
                if (bubble != null)
                    Destroy(bubble);
            if (instance == this)
                instance = null;
        }

        // A line can be said once the last one's time is up, while the other player is there
        private bool CanSay { get { return lan != null && lan.Linked && Time.unscaledTime - sentAt >= Every; } }

        //----------------------------------------The badge----------------------------------------//
        // A small "..." bubble on a portrait's top left: the chat window (the portrait itself is the quick lines'); it swells
        // under the pointer, and its tooltip says so with its key (T). On a phone, a finger's area about twice its size, mostly
        // out from the portrait. And the quick chat's mark (mark): a speech bubble on the portrait's bottom right; the
        // portrait's tooltip and the mark's say what they open, with the key (E)
        private GameObject Badge(RectTransform picture, out GameObject mark)
        {
            UITheme theme = UITheme.Current;
            RectTransform rect = GoldPanel(picture.parent, "TalkBadge");
            Vector2 size = picture.rect.size;
            float scale = Mathf.Clamp(size.x / 118f, 0.75f, 1f); // the scoreboard's portrait is smaller than the board's
            rect.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42, 30) * scale);
            rect.localPosition = picture.localPosition + new Vector3(-size.x * 0.38f, size.y * 0.36f, 0);
            TextMeshProUGUI dots = UIKit.Text(rect, "...", UIKit.TextStyle.Body, TextAlignmentOptions.Center);
            dots.fontSize = 26 * scale;
            dots.color = theme.goldBright;
            dots.raycastTarget = false;
            dots.rectTransform.Stretch(0, 0, 0, 8 * scale);
            rect.gameObject.AddComponent<BadgeClick>().view = this;
            if (GameInput.IsTouch)
            {
                Image area = UIKit.NewImage(rect, "TouchArea", null, Color.clear, true);
                area.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(-12, 10) * scale, new Vector2(80, 66) * scale);
                area.transform.SetAsFirstSibling();
            }
            rect.gameObject.AddComponent<UITooltip>().text = () => "Chat window" + KeyName("T");
            // The board portrait's silver ring lies over it and takes the pointer: the tooltip is the ring's there
            Transform ring = picture.parent.Find("Border");
            GameObject hovered = ring != null ? ring.gameObject : picture.gameObject;
            if (hovered.GetComponent<UITooltip>() == null)
                hovered.AddComponent<UITooltip>().text = () => "Quick chat" + KeyName("E");
            mark = TalkMark(picture, scale);
            return rect.gameObject;
        }

        // The quick chat's mark: a gold-ringed speech bubble against the portrait's ring at its bottom right (clear of the
        // name beside it), a button for the quick lines too, as the portrait is
        private GameObject TalkMark(RectTransform picture, float scale)
        {
            UITheme theme = UITheme.Current;
            Vector2 size = picture.rect.size;
            Image disc = UIKit.NewImage(picture.parent, "TalkMark", theme.circle, UITheme.WithAlpha(theme.panelRaised, 1), true);
            disc.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40) * scale);
            disc.rectTransform.localPosition = picture.localPosition + new Vector3(size.x * 0.3f, -size.y * 0.36f, 0);
            Image ring = UIKit.NewImage(disc.transform, "Ring", theme.ring, theme.gold);
            ring.raycastTarget = false;
            ring.rectTransform.Stretch();
            Image icon = UIKit.NewImage(disc.transform, "Icon", theme.iconTalk, theme.goldBright);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.rectTransform.Stretch(9 * scale, 9 * scale, 9 * scale, 9 * scale);
            if (GameInput.IsTouch)
            {
                Image area = UIKit.NewImage(disc.transform, "TouchArea", null, Color.clear, true);
                area.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(8, -8) * scale, new Vector2(70, 70) * scale);
                area.transform.SetAsFirstSibling();
            }
            disc.gameObject.AddComponent<MarkClick>().view = this;
            disc.gameObject.AddComponent<UITooltip>().text = () => "Quick chat" + KeyName("E");
            return disc.gameObject;
        }

        // A key's name after a tooltip's words, on a keyboard (a phone has none)
        private static string KeyName(string key)
        {
            return GameInput.IsTouch ? "" : " (" + key + ")";
        }

        // A raised panel (opaque) with a gold outline: the badge's, the bubbles' and the quick lines' look
        private static RectTransform GoldPanel(Transform parent, string name)
        {
            RectTransform panel = UIKit.Panel(parent, name, true);
            panel.Find("Outline").GetComponent<Image>().color = UITheme.Current.gold;
            return panel;
        }

        // A clear layer over the screen under a popup: a click on it closes the popup
        private RectTransform Popup(string name, System.Action close)
        {
            RectTransform root = UIKit.Rect(name, UIOverlay.Root).Stretch();
            Image outside = UIKit.NewImage(root, "Outside", null, Color.clear, true);
            outside.rectTransform.Stretch();
            outside.gameObject.AddComponent<OutsideClick>().close = close;
            return root;
        }

        private void CloseAll()
        {
            Close();
            CloseQuick();
        }

        // The keys of what is open (the battle's wait): the quick lines' numbers, T and E; typing, the field has them
        private void Keys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || Time.frameCount == openedAt)
                return;
            if (quick != null && UIOverlay.IsTopModal(quick))
            {
                for (int i = 0; i < lines.Count && i < 9; i++)
                    if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                    {
                        if (lines[i].interactable)
                            lines[i].onClick.Invoke();
                        return;
                    }
                if (keyboard.tKey.wasPressedThisFrame)
                {
                    CloseQuick();
                    ToggleWindow(true);
                }
                else if (keyboard.eKey.wasPressedThisFrame)
                    CloseQuick();
            }
            else if (panel != null && UIOverlay.IsTopModal(panel) && field != null && !field.isFocused)
            {
                if (keyboard.tKey.wasPressedThisFrame)
                    Type();
                else if (keyboard.eKey.wasPressedThisFrame)
                {
                    Close();
                    ToggleQuick();
                }
            }
        }

        //----------------------------------------The quick lines----------------------------------------//
        // Beside your portrait (on the end screen, left of the scoreboard's), its point at it: the quick lines, numbered
        // (not on a touch screen); one said, the list goes
        private void ToggleQuick()
        {
            if (quick != null)
            {
                CloseQuick();
                return;
            }
            if (lan == null || !lan.Linked || UIOverlay.HasModal)
                return;
            UITheme theme = UITheme.Current;
            RectTransform root = Popup("QuickChat", CloseQuick);
            float height = 2 * QuickPad + Chat.Lines.Length * QuickLine;
            RectTransform box = UIKit.Rect("Lines", root);

            Rect portrait = CardMotion.ScreenRect(Anchor(true, out Way way));
            bool left = way == Way.Left;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(left ? portrait.xMin : portrait.xMax, portrait.center.y), null, out Vector2 beside);
            float half_height = root.rect.height / 2;
            float y = Mathf.Clamp(beside.y, -half_height + 12 + height / 2, half_height - 12 - height / 2);
            box.Place(new Vector2(0.5f, 0.5f), new Vector2(beside.x + (left ? -18 : 18), y), new Vector2(QuickWidth, height), new Vector2(left ? 1 : 0, 0.5f));
            Point(box, new Vector2(left ? 1 : 0, 0.5f), new Vector2(0, beside.y - y), left ? Vector2.left : Vector2.right, () => GoldPanel(box, "Body").Stretch());

            lines.Clear();
            for (int i = 0; i < Chat.Lines.Length; i++)
            {
                int line = i;
                lines.Add(Line(box, Chat.Lines[i], GameInput.IsTouch ? null : (i + 1).ToString(), QuickPad, QuickPad + i * QuickLine, QuickWidth - 2 * QuickPad,
                    QuickLine, theme.text, () => Emote(line)));
            }
            quick = root.gameObject;
            openedAt = Time.frameCount;
            UIOverlay.PushModal(quick, () => quick = null);
            UIMotion.Appear(box.gameObject);
            RefreshButtons();
        }

        private void CloseQuick()
        {
            if (quick != null)
                UIOverlay.CloseModal(quick);
            quick = null;
            lines.Clear();
        }

        private void Emote(int line)
        {
            CloseQuick();
            Speak(() => lan.SendEmote(line));
        }

        //----------------------------------------The window----------------------------------------//
        // At the foot of the screen in the middle, over your hand (it is back once the window closes): its title, the
        // speaker (mute) and ×, what was said (scrolls), and a message to type with its Send. Typing: the field has the
        // keys (not on a touch screen: its keyboard would cover the window at once).
        private void ToggleWindow(bool typing)
        {
            if (panel != null)
            {
                Close();
                return;
            }
            if (lan == null || !lan.Linked || UIOverlay.HasModal)
                return;
            RectTransform root = Popup("Talk", Close);
            float height = Pad + HeaderHeight + LogHeight + 10 + FieldHeight + Pad;
            Vector2 at = Where(root); // and its width
            RectTransform box = UIKit.Panel(root, "Window", true);
            box.Place(new Vector2(0.5f, 0.5f), at, new Vector2(panelWidth, height), new Vector2(0.5f, 0));

            float y = Pad;
            Header(box, y);
            y += HeaderHeight;
            BuildLog(box, y);
            y += LogHeight + 10;
            BuildField(box, y);

            panel = root.gameObject;
            openedAt = Time.frameCount;
            UIOverlay.PushModal(panel, () => panel = null);
            UIMotion.Appear(box.gameObject);
            ShowLog();
            RefreshButtons();
            if (typing && !GameInput.IsTouch)
                Type();
        }

        // Its title (and whether the other player is muted), the speaker that mutes them, and the × that hides it (as
        // Escape, a click beside it, or E do)
        private void Header(RectTransform box, float y)
        {
            UITheme theme = UITheme.Current;
            title = UIKit.Text(box, "", UIKit.TextStyle.Heading, TextAlignmentOptions.MidlineLeft);
            title.fontSize = 24;
            title.color = theme.gold;
            title.raycastTarget = false;
            TopLeft(title.rectTransform, Pad + 12, y, panelWidth - 2 * Pad - 2 * UIKit.RoundSize - 40, HeaderHeight);
            ShowTitle();

            float top = y + (HeaderHeight - UIKit.RoundSize) / 2;
            Button close = UIKit.RoundButton(box, "Close", "\u00D7", null, Close);
            TopLeft((RectTransform)close.transform, panelWidth - Pad - UIKit.RoundSize - 4, top, UIKit.RoundSize, UIKit.RoundSize);
            UIKit.GrowTouchArea((RectTransform)close.transform, new Vector2(UIKit.RoundSize + 10, 70)); // a finger's, not over the speaker's
            Image icon = null;
            Button speaker = UIKit.RoundButton(box, "Mute", null, lan.Muted ? theme.iconMuted : theme.iconSpeaker, () =>
            {
                lan.Muted = !lan.Muted;
                icon.sprite = lan.Muted ? theme.iconMuted : theme.iconSpeaker;
                ShowTitle();
            });
            icon = speaker.transform.Find("Icon").GetComponent<Image>();
            TopLeft((RectTransform)speaker.transform, panelWidth - Pad - 2 * UIKit.RoundSize - 14, top, UIKit.RoundSize, UIKit.RoundSize);
            UIKit.GrowTouchArea((RectTransform)speaker.transform, new Vector2(UIKit.RoundSize + 10, 70));
        }

        // "Chat", and while the other player is muted, so
        private void ShowTitle()
        {
            if (title == null)
                return;
            UITheme theme = UITheme.Current;
            title.text = "Chat" + (lan.Muted ? "<size=20><color=#" + ColorUtility.ToHtmlStringRGB(theme.textMuted) + ">   ·   " + lan.TheirName + " muted</color></size>" : "");
        }

        // The window's foot at the screen's, over your hand and as wide (its tray's edges too; within the screen)
        private Vector2 Where(RectTransform root)
        {
            Rect hand = battle.HandScreenRect;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(hand.xMin, hand.center.y), null, out Vector2 left);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(hand.xMax, hand.center.y), null, out Vector2 right);
            float half_width = root.rect.width / 2, half_height = root.rect.height / 2;
            panelWidth = Mathf.Clamp(right.x - left.x + 16, LeastWidth, root.rect.width - 32);
            float middle = (left.x + right.x) / 2;
            return new Vector2(Mathf.Clamp(middle, -half_width + 16 + panelWidth / 2, half_width - 16 - panelWidth / 2), -half_height + 12);
        }

        private void Type()
        {
            if (field == null || GameInput.IsTouch)
                return;
            field.Select();
            field.ActivateInputField();
        }

        // What was said, newest at the foot: it scrolls (the wheel, a drag, its bar on the right once it overflows)
        private void BuildLog(RectTransform box, float y)
        {
            RectTransform area = TopLeft(UIKit.Rect("Log", box), Pad, y, panelWidth - 2 * Pad, LogHeight);
            Image well = area.gameObject.AddComponent<Image>(); // the wheel's and the drag's, and a dark well
            well.sprite = UITheme.Current.panelSprite;
            well.type = Image.Type.Sliced;
            well.color = new Color(0, 0, 0, 0.3f);
            RectTransform viewport = UIKit.Rect("Viewport", area).Stretch(4, 4 + UIKit.ScrollbarWidth + 6, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            logContent = UIKit.Rect("Content", viewport);
            logContent.anchorMin = new Vector2(0, 1);
            logContent.anchorMax = new Vector2(1, 1);
            logContent.pivot = new Vector2(0.5f, 1);
            logContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = logContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 6, 6);
            layout.spacing = 2;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            logContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            logScroll = area.gameObject.AddComponent<ScrollRect>();
            logScroll.horizontal = false;
            logScroll.viewport = viewport;
            logScroll.content = logContent;
            logScroll.movementType = ScrollRect.MovementType.Clamped;
            logScroll.scrollSensitivity = 30;
            Scrollbar bar = UIKit.VerticalScrollbar(area, "Scrollbar");
            ((RectTransform)bar.transform).Place(new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(UIKit.ScrollbarWidth, LogHeight - 12), new Vector2(1, 0.5f));
            logScroll.verticalScrollbar = bar;
            logScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        // Each line: who said it (you, or their name in gold), and what (as typed: <noparse>)
        private void ShowLog()
        {
            if (logContent == null)
                return;
            for (int i = logContent.childCount - 1; i >= 0; i--)
            {
                Transform entry = logContent.GetChild(i);
                entry.SetParent(null, false); // out of the layout at once
                Destroy(entry.gameObject);
            }
            UITheme theme = UITheme.Current;
            IReadOnlyList<LanSession.Said> talk = lan.Talk;
            if (talk.Count == 0)
                Entry("Nothing said yet", theme.textMuted, TextAlignmentOptions.Center).name = "Nothing";
            foreach (LanSession.Said said in talk)
                Entry(said);
            ScrollToFoot();
        }

        // One more line at the foot (the oldest goes past what the session keeps)
        private void AddToLog(LanSession.Said said)
        {
            Transform nothing = logContent.Find("Nothing");
            if (nothing != null)
                Destroy(nothing.gameObject);
            Entry(said);
            if (logContent.childCount > lan.Talk.Count)
                Destroy(logContent.GetChild(0).gameObject);
            ScrollToFoot();
        }

        private void ScrollToFoot()
        {
            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0;
        }

        private void Entry(LanSession.Said said)
        {
            UITheme theme = UITheme.Current;
            string who = said.Mine ? ColorUtility.ToHtmlStringRGB(theme.textSecondary) + ">You" : ColorUtility.ToHtmlStringRGB(theme.goldBright) + ">" + lan.TheirName;
            Entry("<color=#" + who + "</color>  <noparse>" + Regex.Replace(said.Text, "</noparse>", "", RegexOptions.IgnoreCase) + "</noparse>",
                theme.text, TextAlignmentOptions.TopLeft);
        }

        private TextMeshProUGUI Entry(string text, Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = UIKit.Text(logContent, text, UIKit.TextStyle.Body, alignment);
            label.fontSize = 23;
            label.color = color;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        // A message to type (kept to what the fonts show, up to 100), and Send (Enter too)
        private void BuildField(RectTransform box, float y)
        {
            UITheme theme = UITheme.Current;
            float width = panelWidth - 2 * Pad - SendWidth - 10;
            RectTransform well = TopLeft(UIKit.Rect("Message", box), Pad, y, width, FieldHeight);
            well.gameObject.SetActive(false); // the field set up before it wakes
            Image fill = well.gameObject.AddComponent<Image>();
            fill.sprite = theme.panelSprite;
            fill.type = Image.Type.Sliced;
            fill.color = new Color(0.04f, 0.03f, 0.02f, 0.85f);
            UIKit.NewImage(well, "Outline", theme.panelOutline, theme.line).rectTransform.Stretch();
            RectTransform area = UIKit.Rect("Text Area", well).Stretch(16, 16, 4, 4);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI hint = UIKit.Text(area, GameInput.IsTouch ? "Say something…" : "Say something…  (T)", UIKit.TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            hint.fontSize = 24;
            hint.color = theme.textMuted;
            hint.rectTransform.Stretch();
            TextMeshProUGUI text = UIKit.Text(area, "", UIKit.TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            text.fontSize = 24;
            text.color = theme.textBright;
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.Stretch();
            field = well.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.fontAsset = text.font;
            field.pointSize = 24;
            field.richText = false;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = Chat.MaxLength;
            field.customCaretColor = true;
            field.caretColor = theme.goldBright;
            field.caretWidth = 2;
            field.selectionColor = UITheme.WithAlpha(theme.gold, 0.35f);
            field.onValidateInput = (typed, at, c) => Chat.Allowed(c) ? c : '\0';
            field.onSubmit.AddListener(_ => SendTyped());
            well.gameObject.SetActive(true);

            send = UIKit.Button(box, "Send", UIKit.ButtonStyle.Secondary, SendTyped, new Vector2(SendWidth, FieldHeight));
            TopLeft((RectTransform)send.transform, Pad + width + 10, y, SendWidth, FieldHeight);
        }

        private void RefreshButtons()
        {
            bool can = CanSay;
            foreach (Button line in lines)
                if (line != null && line.interactable != can)
                    line.interactable = can;
            if (send != null && send.interactable != can)
                send.interactable = can;
        }

        private void Close()
        {
            if (panel != null)
                UIOverlay.CloseModal(panel);
            panel = null;
            field = null;
            send = null;
            title = null;
            logContent = null;
        }

        private void SendTyped()
        {
            if (field == null)
                return;
            if (Chat.Clean(field.text) == null || !CanSay)
            {
                if (!GameInput.IsTouch)
                    field.ActivateInputField(); // nothing to send yet: the keys stay in the field
                return;
            }
            string message = field.text;
            Close();
            Speak(() => lan.SendChat(message));
        }

        // Says it, once the last line's time is up and the other player is there: your bubble, and what was said (null:
        // nothing said)
        private LanSession.Said Speak(System.Func<LanSession.Said> send)
        {
            if (!CanSay)
                return null;
            LanSession.Said said = send();
            if (said == null)
                return null;
            sentAt = Time.unscaledTime;
            Bubble(true, said.Text);
            GameLog.Event(LogCategory.Battle, "talk", "length", said.Text.Length); // not the words: they are the players' own
            return said;
        }

        // What a tool says through the screen, as a player would (LanBot): a quick line, or a message as typed
        public static LanSession.Said Say(int line)
        {
            return instance != null ? instance.Speak(() => instance.lan.SendEmote(line)) : null;
        }

        public static LanSession.Said Say(string message)
        {
            return instance != null ? instance.Speak(() => instance.lan.SendChat(message)) : null;
        }

        // A quick line: its number key (none on a touch screen) and text, lit under the pointer
        private static Button Line(RectTransform box, string text, string key, float x, float y, float width, float height, Color color, System.Action on_click)
        {
            UITheme theme = UITheme.Current;
            Image fill = UIKit.NewImage(box, "Line " + text, theme.panelSprite, Color.clear, true);
            TopLeft(fill.rectTransform, x, y, width, height);
            Button button = fill.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = fill;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // the number keys say them
            UIStateColors states = fill.gameObject.AddComponent<UIStateColors>();
            states.Add(fill, UITheme.WithAlpha(theme.gold, 0), UITheme.WithAlpha(theme.gold, 0.16f), UITheme.WithAlpha(theme.gold, 0.28f));
            float left = 12;
            if (key != null)
            {
                RectTransform cap = UIKit.KeyCap(fill.transform, key);
                cap.Place(new Vector2(0, 0.5f), new Vector2(left, 0), cap.sizeDelta, new Vector2(0, 0.5f));
                left += cap.sizeDelta.x + 10;
            }
            TextMeshProUGUI label = UIKit.Text(fill.transform, text, UIKit.TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            label.fontSize = 23;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.rectTransform.Stretch(left, 8, 0, 2);
            states.Add(label, color, theme.goldBright, theme.goldBright);
            button.onClick.AddListener(() =>
            {
                GameAudio.PlaySfx("ui_click");
                on_click();
            });
            return button;
        }

        // Places a child by its top left corner, from its parent's top left
        private static RectTransform TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            return rect.Place(new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height), new Vector2(0, 1));
        }

        //----------------------------------------The bubbles----------------------------------------//
        private void Heard(LanSession.Said said)
        {
            if (this == null)
                return;
            Bubble(false, said.Text);
            GameAudio.PlaySfx("ui_click", 0.7f);
            if (panel != null)
                AddToLog(said);
        }

        // Which way a bubble goes from what it comes out of: under your portrait, over the opponent's (the scores and the
        // rows stay in sight), left of the end screen's scoreboard
        private enum Way { Down, Up, Left }

        // Where a side's bubble comes out: its portrait on the board, or on the end screen its scoreboard portrait
        private RectTransform Anchor(bool me, out Way way)
        {
            RectTransform scored = battle.IsBattleOver && end != null ? end.Portrait(me) : null;
            way = scored != null ? Way.Left : me ? Way.Down : Way.Up;
            return scored != null ? scored : (me ? mine : theirs).Picture;
        }

        // A bubble out of a side's portrait, its point at it; it pops up, stays a while (longer for more words) and fades.
        // More lines grow it away from the portrait (on the end screen, both ways). The side's next replaces it.
        private void Bubble(bool me, string text)
        {
            int side = me ? 0 : 1;
            if (bubbles[side] != null)
                Destroy(bubbles[side]);
            UITheme theme = UITheme.Current;

            RectTransform anchor = Anchor(me, out Way way);
            CardMotion.ToLayout(CardMotion.ScreenRect(anchor), out Vector2 center, out Vector2 size);
            RectTransform root = UIKit.Rect("Said", UIOverlay.FixedRoot);
            TextMeshProUGUI label = UIKit.Text(root, text, UIKit.TextStyle.Body, TextAlignmentOptions.Center);
            label.fontSize = way == Way.Left ? 27 : 25;
            label.color = theme.textBright;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            float most = way == Way.Left ? EndBubbleText : BoardBubbleText;
            Vector2 wide = label.GetPreferredValues(text);
            Vector2 fits = wide.x <= most ? wide : label.GetPreferredValues(text, most, 0);
            Vector2 bubble = new Vector2(Mathf.Min(fits.x, most) + 2 * BubblePad, Mathf.Max(BubbleHeight, fits.y + 18));

            // The bubble's place (by its side next to the portrait), and its point's: on that side, at the portrait
            Vector2 pivot, at, point_offset, inward;
            if (way == Way.Left)
            {
                pivot = new Vector2(1, 0.5f);
                at = new Vector2(center.x - size.x / 2 - 18, center.y);
                point_offset = Vector2.zero;
                inward = Vector2.left;
            }
            else
            {
                float half_width = ((RectTransform)UIOverlay.FixedRoot).rect.width / 2;
                float left = Mathf.Max(center.x - size.x / 2 - 20, -half_width + 8);
                bool down = way == Way.Down;
                pivot = new Vector2(0, down ? 1 : 0);
                at = new Vector2(left, down ? center.y - size.y / 2 - 14 : center.y + size.y / 2 + 14);
                point_offset = new Vector2(Mathf.Clamp(center.x - left, 24, bubble.x - 24), 0);
                inward = down ? Vector2.down : Vector2.up;
            }
            root.Place(new Vector2(0.5f, 0.5f), at, bubble, pivot);
            root.gameObject.AddComponent<FollowPortrait>().Init(anchor, way);
            Point(root, pivot, point_offset, inward, () => GoldPanel(root, "Body").Stretch());
            label.transform.SetAsLastSibling();
            label.rectTransform.Stretch(BubblePad, BubblePad, 0, 2);

            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.alpha = 0;
            float grown = UIKit.PhoneScale; // a phone's a size larger, from its side at the portrait
            root.localScale = Vector3.one * 0.85f * grown;
            LMotion.Create(0f, 1f, 0.2f).WithEase(Ease.OutBack).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(x => { group.alpha = Mathf.Clamp01(x); root.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, x) * grown; })
                .AddTo(root.gameObject);
            GameObject go = root.gameObject;
            float shown = Mathf.Clamp(2.5f + 0.06f * text.Length, 3f, 8f);
            LMotion.Create(1f, 0f, 0.35f).WithDelay(shown).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => Destroy(go)).BindToAlpha(group).AddTo(go);
            bubbles[side] = go;
        }

        // A speech point on one side of a gold panel (built by body, between the point's two halves): a gold diamond under
        // the panel, a dark one over it, hiding the outline where they meet
        private static void Point(RectTransform root, Vector2 side, Vector2 offset, Vector2 inward, System.Action body)
        {
            UITheme theme = UITheme.Current;
            Image edge = UIKit.NewImage(root, "PointEdge", null, theme.gold);
            edge.raycastTarget = false;
            edge.rectTransform.Place(side, offset, new Vector2(22, 22));
            edge.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
            body();
            Image point = UIKit.NewImage(root, "Point", null, UITheme.WithAlpha(theme.panelRaised, 1));
            point.raycastTarget = false;
            point.rectTransform.Place(side, offset + inward, new Vector2(20, 20));
            point.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
        }

        // A bubble keeps to its portrait's edge as the board moves under it (zoomed in or out by a pinch, BoardZoom)
        private sealed class FollowPortrait : MonoBehaviour
        {
            private RectTransform anchor, root;
            private Way way;
            private Vector2 start, startAt;

            public void Init(RectTransform portrait, Way side)
            {
                anchor = portrait;
                way = side;
                root = (RectTransform)transform;
                start = Edge();
                startAt = root.anchoredPosition;
            }

            // The portrait's side the bubble comes out of (its point's place), in the overlay's layout
            private Vector2 Edge()
            {
                CardMotion.ToLayout(CardMotion.ScreenRect(anchor), out Vector2 center, out Vector2 size);
                return way == Way.Left ? new Vector2(center.x - size.x / 2, center.y)
                    : new Vector2(center.x, center.y + (way == Way.Down ? -size.y : size.y) / 2);
            }

            private void LateUpdate()
            {
                if (anchor != null)
                    root.anchoredPosition = startAt + Edge() - start;
            }
        }

        //----------------------------------------Clicks----------------------------------------//
        // Your portrait (the board's, the end screen's): the quick lines; its badge swells under the pointer
        private sealed class PortraitClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public ChatView view;
            public Transform badge;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left && view != null)
                    view.ToggleQuick();
            }

            public void OnPointerEnter(PointerEventData e)
            {
                if (badge != null)
                    badge.localScale = Vector3.one * 1.2f;
            }

            public void OnPointerExit(PointerEventData e)
            {
                if (badge != null)
                    badge.localScale = Vector3.one;
            }
        }

        // The portrait's "..." badge: the chat window
        // The quick chat's mark: the quick lines, as the portrait
        private sealed class MarkClick : MonoBehaviour, IPointerClickHandler
        {
            public ChatView view;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left && view != null)
                    view.ToggleQuick();
            }
        }

        private sealed class BadgeClick : MonoBehaviour, IPointerClickHandler
        {
            public ChatView view;

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left && view != null)
                    view.ToggleWindow(false);
            }
        }

        private sealed class OutsideClick : MonoBehaviour, IPointerClickHandler
        {
            public System.Action close;

            public void OnPointerClick(PointerEventData e)
            {
                close?.Invoke();
            }
        }
    }
}
