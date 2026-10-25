using System;
using System.Collections.Generic;
using Gwent.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gwent.UI
{
    // Builds the battle's uGUI overlays in code (the pickers' bands, the discard viewer, the redraw prompt, tooltips): text
    // styles, panels, buttons, frames. Sizes are in the 1920x1080 reference space of the overlay canvas. (The menus and
    // dialogs are UI Toolkit: UIScreens.)
    public static class UIKit
    {
        public enum TextStyle { Title, Heading, Tab, Button, Body, Label, Caption }
        public enum ButtonStyle { Primary, Secondary }

        private static UITheme Theme { get { return UITheme.Current; } }

        //----------------------------------------Layout----------------------------------------//
        public static RectTransform Rect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // Fixed size at an anchor point
        public static RectTransform Place(this RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        // Fills the parent, minus margins
        public static RectTransform Stretch(this RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        // Covers the parent whatever the screen's shape, as a picture of this aspect (width / height) does: cut at the
        // sides or at the top and bottom, never stretched (the battle's table, the menu's key art)
        public static RectTransform Cover(this RectTransform rect, float aspect)
        {
            AspectRatioFitter fitter = rect.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = aspect;
            return rect;
        }

        // A phone: its menus, dialogs and the battle's corner buttons a size larger, so that their text and targets have
        // the size an eye and a finger need on a small screen (a 24-unit caption is some 1.5 mm there, a 46-unit button 3).
        // Measured on the screen's short side: a phone's is some 65 to 75 mm, which takes the most; a tablet's is twice
        // that, which takes none. A browser doesn't tell the screen's size: there, a phone's long shape takes the most.
        private const float PhoneShortSideMm = 90;
        private const float MaxPhoneScale = 1.3f;
        private const float MaxMenuScale = 1.3f;       // the menu size setting on top of it, as far as the layouts allow

        public static float PhoneScale
        {
            get
            {
                if (!GameInput.IsTouch)
                    return 1;
                float short_side = Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));
                float mm = Screen.dpi > 0 && !GameSettings.IsWeb ? short_side / GameInput.PixelsPerMm
                    : Mathf.Max(Screen.width, Screen.height) / short_side >= 1.7f ? 70 : 140;
                return Mathf.Clamp(PhoneShortSideMm / mm, 1, MaxPhoneScale);
            }
        }

        // The size of the menus and dialogs: the setting's, a phone's larger
        public static float MenuScale { get { return Mathf.Min(GameSettings.MenuScale * PhoneScale, MaxMenuScale); } }

        // On a touch screen, a small control takes a finger's press over a larger square around it (an invisible image
        // under it, which its clicks reach): the battle's menu button, crowns and pass button
        public static void GrowTouchArea(RectTransform control, Vector2 size)
        {
            if (!GameInput.IsTouch || control.Find("TouchArea") != null)
                return;
            Image area = NewImage(control, "TouchArea", null, Color.clear, true);
            area.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.Max(size, control.rect.size));
            area.transform.SetAsFirstSibling();
        }

        // A plain rectangular frame (square corners) around its parent: four bars, outset by the given margin
        public static RectTransform Frame(Transform parent, string name, Color color, float thickness, float outset = 0)
        {
            RectTransform frame = Rect(name, parent).Stretch(-outset, -outset, -outset, -outset);
            Vector2[] mins = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(0, 0) };
            Vector2[] maxs = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 0) };
            for (int i = 0; i < 4; i++)
            {
                RectTransform bar = NewImage(frame, "Side", null, color).rectTransform;
                bar.anchorMin = mins[i];
                bar.anchorMax = maxs[i];
                bar.pivot = new Vector2(mins[i].x == maxs[i].x ? mins[i].x : 0.5f, mins[i].y == maxs[i].y ? mins[i].y : 0.5f);
                bar.anchoredPosition = Vector2.zero;
                // The top and bottom stop at the sides: no corner is drawn twice (it would show brighter when faded)
                bar.sizeDelta = i < 2 ? new Vector2(thickness, 0) : new Vector2(-2 * thickness, thickness);
            }
            return frame;
        }

        public static Image NewImage(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            Image image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        //----------------------------------------Text----------------------------------------//
        public static TextMeshProUGUI Text(Transform parent, string text, TextStyle style, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            TextMeshProUGUI label = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            ApplyStyle(label, style);
            label.text = text;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        public static void ApplyStyle(TextMeshProUGUI label, TextStyle style)
        {
            UITheme theme = Theme;
            bool heading = style == TextStyle.Title || style == TextStyle.Heading || style == TextStyle.Tab || style == TextStyle.Button;
            TMP_FontAsset font = heading ? theme.headingFont : theme.bodyFont;
            if (font != null)
                label.font = font;
            label.characterSpacing = heading ? 4 : 0;
            switch (style)
            {
                case TextStyle.Title: label.fontSize = 60; label.color = theme.goldBright; break;
                case TextStyle.Heading: label.fontSize = 44; label.color = theme.goldBright; break;
                case TextStyle.Tab: label.fontSize = 28; label.color = theme.textSecondary; break;
                case TextStyle.Button: label.fontSize = 28; label.color = theme.gold; break;
                case TextStyle.Body: label.fontSize = 32; label.color = theme.text; break;
                case TextStyle.Label: label.fontSize = 30; label.color = theme.textSecondary; break;
                case TextStyle.Caption: label.fontSize = 24; label.color = theme.textMuted; break;
            }
        }

        //----------------------------------------Surfaces----------------------------------------//
        // Rounded panel with a thin bronze outline; blocks clicks to what is behind it
        public static RectTransform Panel(Transform parent, string name, bool raised = false)
        {
            Image fill = NewImage(parent, name, Theme.panelSprite, raised ? Theme.panelRaised : Theme.panel, true);
            Image outline = NewImage(fill.transform, "Outline", Theme.panelOutline, Theme.line);
            outline.rectTransform.Stretch();
            return fill.rectTransform;
        }

        // A vertical scroll bar in the menus' look (the same as the UI Toolkit screens' in Gwent.uss): a thin dark track
        // the whole height, a gold handle with rounded ends that brightens under the pointer and while dragged; no arrows.
        // Width: its own; the caller places it and gives it to its ScrollRect.
        public const float ScrollbarWidth = 16;

        public static Scrollbar VerticalScrollbar(Transform parent, string name)
        {
            UITheme theme = Theme;
            RectTransform root = Rect(name, parent);
            root.sizeDelta = new Vector2(ScrollbarWidth, 0);
            Image track = NewImage(root, "Track", theme.pill, theme.lineSoft);
            track.rectTransform.Stretch(6, 6, 0, 0); // 4 wide
            track.pixelsPerUnitMultiplier = 8; // the pill's ends, scaled to the track's width
            RectTransform area = Rect("Sliding Area", root).Stretch();
            Image handle = NewImage(area, "Handle", theme.pill, Color.white, true);
            handle.rectTransform.Stretch(3, 3, 0, 0); // 10 wide; its height and place are the Scrollbar's
            handle.pixelsPerUnitMultiplier = 3.2f;

            Scrollbar bar = root.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.navigation = new Navigation { mode = Navigation.Mode.None }; // the keys stay with the cards and menus
            ColorBlock colors = bar.colors;
            colors.normalColor = theme.gold;
            colors.selectedColor = theme.gold;
            colors.highlightedColor = theme.goldBright;
            colors.pressedColor = theme.goldBright;
            colors.disabledColor = UITheme.WithAlpha(theme.gold, 0.35f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = 0.12f;
            bar.colors = colors;
            return bar;
        }

        // Thin gold rule with a diamond in the middle
        public static Image Ornament(Transform parent, Vector2 anchor, Vector2 position, float width = 420)
        {
            Image rule = NewImage(parent, "Ornament", Theme.rule, UITheme.WithAlpha(Theme.gold, 0.85f));
            rule.rectTransform.Place(anchor, position, new Vector2(width, 12));
            return rule;
        }

        public static Image Divider(Transform parent, float y, float margin)
        {
            Image line = NewImage(parent, "Divider", null, Theme.lineSoft);
            RectTransform rect = line.rectTransform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margin, y - 1);
            rect.offsetMax = new Vector2(-margin, y + 1);
            return line;
        }

        //----------------------------------------Buttons----------------------------------------//
        public static Button Button(Transform parent, string text, ButtonStyle style, Action on_click, Vector2? size = null)
        {
            UITheme theme = Theme;
            Image fill = NewImage(parent, "Button " + text, theme.panelSprite, Color.clear, true);
            fill.rectTransform.sizeDelta = size ?? new Vector2(280, 68);
            Button button = fill.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = fill;

            TextMeshProUGUI label = Text(fill.transform, text, TextStyle.Button, TextAlignmentOptions.Center);
            label.rectTransform.Stretch(12, 12, 0, 2);

            UIStateColors states = fill.gameObject.AddComponent<UIStateColors>();
            switch (style)
            {
                case ButtonStyle.Primary:
                    states.Add(fill, theme.gold, theme.goldBright, UITheme.Shade(theme.gold, 0.85f));
                    states.Add(label, theme.background, theme.background, theme.background);
                    break;
                case ButtonStyle.Secondary:
                    Image outline = NewImage(fill.transform, "Outline", theme.panelOutline, theme.gold);
                    outline.rectTransform.Stretch();
                    outline.transform.SetAsFirstSibling();
                    states.Add(fill, UITheme.WithAlpha(theme.gold, 0), UITheme.WithAlpha(theme.gold, 0.16f), UITheme.WithAlpha(theme.gold, 0.28f));
                    states.Add(outline, theme.gold, theme.goldBright, theme.goldBright);
                    states.Add(label, theme.gold, theme.goldBright, theme.goldBright);
                    break;
            }

            if (on_click != null)
                button.onClick.AddListener(() =>
                {
                    GameAudio.PlaySfx("ui_click");
                    on_click();
                });
            return button;
        }

        // A round button with a gold ring, lit under the pointer (or the keys): a glyph in it ("×" closes), or an icon (a
        // sprite) when glyph is null; what it shows takes the ring's light. The chat window's × and speaker, the pickers' ×
        // (backed: on a dark fill of its own, over whatever lies under the band).
        public const float RoundSize = 34;

        public static Button RoundButton(Transform parent, string name, string glyph, Sprite icon, Action on_click, float size = RoundSize, bool backed = false)
        {
            UITheme theme = Theme;
            Image fill = NewImage(parent, name, theme.panelSprite, Color.clear, true);
            fill.rectTransform.sizeDelta = new Vector2(size, size);
            Button button = fill.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = fill;
            Image ring = NewImage(fill.transform, "Ring", theme.panelOutline, theme.gold);
            ring.raycastTarget = false;
            ring.rectTransform.Stretch();
            Graphic shown;
            if (glyph != null)
            {
                TextMeshProUGUI text = Text(fill.transform, glyph, TextStyle.Body, TextAlignmentOptions.Center);
                text.fontSize = size * 0.88f;
                text.raycastTarget = false;
                text.rectTransform.Stretch(0, 0, 0, size * 0.09f);
                shown = text;
            }
            else
            {
                Image image = NewImage(fill.transform, "Icon", icon, theme.gold);
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.rectTransform.Stretch(size * 0.15f, size * 0.15f, size * 0.15f, size * 0.15f);
                shown = image;
            }
            UIStateColors states = fill.gameObject.AddComponent<UIStateColors>();
            Color gold = UITheme.WithAlpha(theme.gold, 1);
            Color under = backed ? UITheme.WithAlpha(theme.panelRaised, 1) : UITheme.WithAlpha(theme.gold, 0);
            Color lit = backed ? Color.Lerp(under, gold, 0.16f) : UITheme.WithAlpha(theme.gold, 0.16f);
            Color held = backed ? Color.Lerp(under, gold, 0.28f) : UITheme.WithAlpha(theme.gold, 0.28f);
            states.Add(fill, under, lit, held);
            states.Add(ring, UITheme.WithAlpha(theme.gold, 0.7f), theme.goldBright, theme.goldBright);
            states.Add(shown, theme.gold, theme.goldBright, theme.goldBright);
            if (on_click != null)
                button.onClick.AddListener(() =>
                {
                    GameAudio.PlaySfx("ui_click");
                    on_click();
                });
            return button;
        }

        // A key's name in a small ringed cap ("1", "Space"), as the keys' hints show (no "[Space]"): as wide as its name,
        // gold, or the colour given (dark on a gold button)
        public static RectTransform KeyCap(Transform parent, string key, float height = 28, Color? color = null)
        {
            UITheme theme = Theme;
            Color shade = color ?? theme.gold;
            RectTransform cap = Rect("Key " + key, parent);
            Image ring = NewImage(cap, "Ring", theme.panelOutline, UITheme.WithAlpha(shade, 0.7f));
            ring.raycastTarget = false;
            ring.rectTransform.Stretch();
            TextMeshProUGUI text = Text(cap, key, TextStyle.Body, TextAlignmentOptions.Center);
            text.fontSize = height * 0.68f;
            text.color = shade;
            text.raycastTarget = false;
            text.rectTransform.Stretch(0, 0, 0, height * 0.07f);
            cap.sizeDelta = new Vector2(Mathf.Max(height, text.GetPreferredValues(key).x + height * 0.6f), height);
            return cap;
        }

        // Puts keyboard / gamepad focus on a control
        public static void Focus(Selectable selectable)
        {
            if (selectable != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }

    // Color states of a themed control (normal, highlighted, pressed, disabled), eased in unscaled time so menus
    // also animate while the game is paused
    public class UIStateColors : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private struct Entry
        {
            public Graphic graphic;
            public Color normal, highlighted, pressed;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private Selectable selectable;
        private bool hovered, pressed, selected, selectedByPointer;

        public void Add(Graphic graphic, Color normal, Color highlighted, Color pressed_color)
        {
            entries.Add(new Entry { graphic = graphic, normal = normal, highlighted = highlighted, pressed = pressed_color });
            graphic.color = normal;
        }

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        private void OnEnable()
        {
            hovered = pressed = false;
            Apply(1f);
        }

        private void Update()
        {
            Apply(1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
        }

        private void Apply(float blend)
        {
            bool interactable = selectable == null || selectable.IsInteractable();
            bool highlighted = interactable && (hovered || (selected && !selectedByPointer));
            foreach (Entry entry in entries)
            {
                if (entry.graphic == null)
                    continue;
                Color target = !interactable ? UITheme.WithAlpha(entry.normal, entry.normal.a * 0.35f)
                    : pressed && hovered ? entry.pressed
                    : highlighted ? entry.highlighted
                    : entry.normal;
                entry.graphic.color = Color.Lerp(entry.graphic.color, target, blend);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
        }

        public void OnPointerExit(PointerEventData eventData) { hovered = false; pressed = false; }
        public void OnPointerDown(PointerEventData eventData) { pressed = true; }
        public void OnPointerUp(PointerEventData eventData) { pressed = false; }

        public void OnSelect(BaseEventData eventData)
        {
            selected = true;
            // Selected by a click: not highlighted once the pointer leaves (keyboard selection stays highlighted)
            selectedByPointer = eventData is PointerEventData;
        }

        public void OnDeselect(BaseEventData eventData) { selected = false; }
    }
}
