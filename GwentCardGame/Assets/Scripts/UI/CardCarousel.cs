using System;
using System.Collections.Generic;
using Gwent.Battle;
using Gwent.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gwent.UI
{
    // The Witcher 3's big-card carousel, for the redraw, the card pickers and a discard pile: the front card large in the
    // middle with a gold glow, the others smaller on each side. A side card (or the arrow keys, A/D, the mouse wheel)
    // comes to the front; the front card's description shows where the card info's does, at the bottom right. What the
    // front card does when clicked (or Enter) is the redraw's or the picker's.
    public abstract class CardCarousel : MonoBehaviour
    {
        public const float CardWidth = 300;
        protected const float CardHeight = CardWidth * 591f / 380f;
        private const float Spacing = 245;     // between side cards
        private const float FrontGap = 45;     // extra room on each side of the front card
        private const float SideScale = 0.74f;
        private const float BandBottom = 208;  // the band's lower edge, under the screen's top
        private const int VisibleSide = 3;     // cards shown on each side of the front one

        private static int keyFrame = -1;

        protected readonly List<int> shown = new List<int>();
        private readonly List<CarouselCard> cards = new List<CarouselCard>();
        protected int focus;
        protected int hidden = -1; // a card on its way into its place (the redraw's new card), not shown yet
        private float scrolled;
        private Image details;
        private int detailsFor = -1;
        private bool detailsPlaced;
        private float frontSince;
        private int frontCard = -1;

        private class CarouselCard
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Image glow;
        }

        // A carousel used Enter this frame: the battle's own keys leave it alone (a picked card waits for its place)
        public static bool UsedKeyThisFrame { get { return keyFrame == Time.frameCount; } }

        public int FocusIndex { get { return focus; } }
        public int CardCount { get { return cards.Count; } }
        public int FocusedCardId { get { return focus < shown.Count ? shown[focus] : -1; } }

        // A card is chosen (redrawn, picked) only once it has been at the front for a moment: a quick double-click on a
        // card further away brings it forward and does not choose it by accident
        protected bool FrontIsReady { get { return Time.unscaledTime - frontSince >= DoubleClick.Interval; } }

        public RectTransform CardAt(int index)
        {
            return index >= 0 && index < cards.Count ? cards[index].rect : null;
        }

        // The carousel's strip on a parent (a soft dark pool behind the cards separates them from the board) and the
        // place of the front card's description
        // On a short screen (a phone's larger menu size, or a large one chosen), the cards go down, as far as needed to
        // stay clear of the band at the top
        protected static T Create<T>(Transform parent, Vector2 center) where T : CardCarousel
        {
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                float top = ((RectTransform)canvas.rootCanvas.transform).rect.height / 2 - BandBottom - 12;
                center.y = Mathf.Min(center.y, top - CardHeight / 2);
            }
            RectTransform root = UIKit.Rect("CardCarousel", parent).Place(new Vector2(0.5f, 0.5f), center, new Vector2(1920, CardHeight + 60));
            Image backdrop = UIKit.NewImage(root, "Backdrop", UITheme.Current.radial, new Color(0, 0, 0, 0.75f));
            backdrop.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2300, CardHeight + 260));
            T carousel = root.gameObject.AddComponent<T>();
            carousel.details = UIKit.NewImage(root, "Details", null, Color.white); // the description box's place
            carousel.details.enabled = false; // until a card with a description is in front (DescriptionBox.Show)
            return carousel;
        }

        // The Witcher 3's band across the top of the screen: its title, and at its right a button: a round × (a picker's,
        // to give the choice up or close a look: button_label null), or a labelled one (the redraw's Done) with its key in
        // a cap (key: null on a touch screen). Returns the button.
        public static Button Band(Transform root, string title, string button_label, string key, Action on_button, out TextMeshProUGUI title_text)
        {
            UITheme theme = UITheme.Current;
            RectTransform band = UIKit.NewImage(root, "Band", null, new Color(0, 0, 0, 0.78f)).rectTransform;
            band.anchorMin = new Vector2(0, 1);
            band.anchorMax = new Vector2(1, 1);
            band.pivot = new Vector2(0.5f, 0.5f);
            band.anchoredPosition = new Vector2(0, -BandBottom + 58);
            band.sizeDelta = new Vector2(0, 116);
            foreach (float edge in new[] { 0f, 1f })
            {
                Image line = UIKit.NewImage(band, "Edge", theme.gradientH, UITheme.WithAlpha(theme.gold, 0.5f));
                line.rectTransform.anchorMin = new Vector2(0, edge);
                line.rectTransform.anchorMax = new Vector2(1, edge);
                line.rectTransform.sizeDelta = new Vector2(0, 2);
            }

            title_text = UIKit.Text(band, title, UIKit.TextStyle.Heading, TextAlignmentOptions.Center);
            title_text.fontSize = 36;
            title_text.color = theme.text;
            title_text.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(1100, 50));

            if (button_label == null)
            {
                Button close = UIKit.RoundButton(band, "Button Close", "\u00D7", null, on_button, 48, true);
                ((RectTransform)close.transform).Place(new Vector2(1f, 0.5f), new Vector2(-114, 0), new Vector2(48, 48));
                return close;
            }
            Button button = UIKit.Button(band, button_label, UIKit.ButtonStyle.Primary, on_button, new Vector2(200, 60));
            float width = 200;
            if (key != null)
            {
                // Its key in a dark cap before its word: "(Space) Done"
                RectTransform cap = UIKit.KeyCap(button.transform, key, 28, UITheme.WithAlpha(theme.background, 0.85f));
                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                width = 64 + cap.sizeDelta.x + label.GetPreferredValues(button_label).x;
                cap.Place(new Vector2(0, 0.5f), new Vector2(22, 0), cap.sizeDelta, new Vector2(0, 0.5f));
                label.rectTransform.Stretch(22 + cap.sizeDelta.x + 10, 12, 0, 2);
            }
            ((RectTransform)button.transform).Place(new Vector2(1f, 0.5f), new Vector2(-90 - width / 2, 0), new Vector2(width, 60));
            return button;
        }

        // Shows these cards (the front one stays at its index when it can)
        protected void Rebuild(IList<int> ids)
        {
            foreach (CarouselCard card in cards)
                Destroy(card.rect.gameObject);
            cards.Clear();
            shown.Clear();
            shown.AddRange(ids);

            UITheme theme = UITheme.Current;
            for (int i = 0; i < shown.Count; i++)
            {
                RectTransform holder = UIKit.Rect("Card " + shown[i], transform).Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardWidth, CardHeight));
                Image glow = UIKit.NewImage(holder, "Glow", theme.softShadow, UITheme.WithAlpha(theme.goldBright, 0));
                glow.rectTransform.Stretch(-34, -34, -34, -34);
                Image shadow = UIKit.NewImage(holder, "Shadow", theme.softShadow, new Color(0, 0, 0, 0.7f));
                shadow.rectTransform.Stretch(-16, -26, -10, -34);
                Image art = UIKit.NewImage(holder, "Art", null, Color.white, true);
                CardArt.Show(art, shown[i]);
                art.rectTransform.Stretch();
                art.preserveAspect = true;

                CanvasGroup group = holder.gameObject.AddComponent<CanvasGroup>();
                holder.gameObject.AddComponent<CarouselCardClick>().Init(this, i);
                cards.Add(new CarouselCard { rect = holder, group = group, glow = glow });
            }
            focus = Mathf.Clamp(focus, 0, Mathf.Max(0, cards.Count - 1));
            Animate(1f);
        }

        protected virtual void Update()
        {
            if (FocusedCardId != frontCard)
            {
                frontCard = FocusedCardId;
                frontSince = Time.unscaledTime;
            }
            if (!detailsPlaced)
            {
                // The front card's description where the card info's description goes: at the bottom right
                PlaceOnScreen(details.rectTransform, CardShowcase.DescriptionScreenRect());
                detailsPlaced = true;
            }
            if (TakesInput())
            {
                Navigate();
                HandleKeys(Keyboard.current);
            }
            Animate(1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            UpdateDetails();
        }

        // Keys and the wheel are the carousel's when no dialog is over it (its own dialog may be)
        protected virtual bool TakesInput()
        {
            return !UIOverlay.HasModal;
        }

        // The front card's description (a plain unit has none)
        private void UpdateDetails()
        {
            int id = FocusedCardId;
            if (id == detailsFor)
                return;
            detailsFor = id;
            DescriptionBox.Show(details, id >= 0 ? CardLibrary.Get().Get(id) : null);
        }

        // Puts an element of the carousel over a screen rectangle
        protected void PlaceOnScreen(RectTransform rect, Rect screen_rect)
        {
            Vector2 min, max;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen_rect.min, null, out min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen_rect.max, null, out max);
            Vector2 pivot_offset = Vector2.Scale(((RectTransform)transform).rect.size, ((RectTransform)transform).pivot - new Vector2(0.5f, 0.5f));
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = (min + max) / 2 + pivot_offset; // local points are from the pivot, anchors from the centre
            rect.sizeDelta = max - min;
        }

        private void Animate(float blend)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                int offset = i - focus;
                int distance = Mathf.Abs(offset);
                // Side cards start past the front card's half width plus the gap, then every Spacing
                float x = offset == 0 ? 0 : Mathf.Sign(offset) * (FrontGap + CardWidth * (1 + SideScale) / 2 + (distance - 1) * Spacing);
                float scale = offset == 0 ? 1f : SideScale;
                float alpha = offset == 0 ? 1f : distance > VisibleSide ? 0f : 0.9f - 0.18f * (distance - 1);
                CarouselCard card = cards[i];
                card.rect.anchoredPosition = Vector2.Lerp(card.rect.anchoredPosition, new Vector2(x, 0), blend);
                card.rect.localScale = Vector3.Lerp(card.rect.localScale, Vector3.one * scale, blend);
                card.group.alpha = i == hidden ? 0 : Mathf.Lerp(card.group.alpha, alpha, blend);
                card.group.blocksRaycasts = distance <= VisibleSide;
                card.glow.color = Color.Lerp(card.glow.color, UITheme.WithAlpha(UITheme.Current.goldBright, offset == 0 ? 0.85f : 0), blend);
            }
            // Drawn from the outside in, so the front card is on top (sibling 0 is the backdrop)
            List<int> order = new List<int>();
            for (int i = 0; i < cards.Count; i++)
                order.Add(i);
            order.Sort((a, b) => Mathf.Abs(b - focus).CompareTo(Mathf.Abs(a - focus)));
            for (int i = 0; i < order.Count; i++)
                if (cards[order[i]].rect.GetSiblingIndex() != i + 1)
                    cards[order[i]].rect.SetSiblingIndex(i + 1);
        }

        //----------------------------------------Input----------------------------------------//
        // A click on a card: a side card comes to the front, the front card is chosen (once it has been there a moment)
        public virtual void OnCardClicked(int index)
        {
            if (index != focus)
                SetFocus(index);
            else if (FrontIsReady)
                Choose();
        }

        // The front card is redrawn, picked...
        protected abstract void Choose();

        public void SetFocus(int index)
        {
            int next = Mathf.Clamp(index, 0, cards.Count - 1);
            if (next == focus)
                return;
            focus = next;
            GameAudio.PlaySfx("card_select");
        }

        // Enter (and other keys of the redraw or the picker): Enter chooses the front card, unless a button elsewhere has
        // keyboard focus (Enter presses that button)
        protected virtual void HandleKeys(Keyboard keyboard)
        {
            if (keyboard == null || !(keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                return;
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.activeInHierarchy && !selected.transform.IsChildOf(transform))
                return;
            keyFrame = Time.frameCount;
            Choose();
        }

        private void Navigate()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                    SetFocus(focus - 1);
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                    SetFocus(focus + 1);
            }
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                scrolled += mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scrolled) >= 1f)
                {
                    SetFocus(focus + (scrolled > 0 ? -1 : 1));
                    scrolled = 0;
                }
            }
        }
    }

    // Click on a card of a carousel (left button, or a tap)
    public class CarouselCardClick : MonoBehaviour, IPointerClickHandler
    {
        private CardCarousel carousel;
        private int index;

        public void Init(CardCarousel owner, int card_index)
        {
            carousel = owner;
            index = card_index;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                carousel.OnCardClicked(index);
        }
    }
}
