using System;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.UI
{
    // A card shown large over a UI Toolkit dialog, with its description box when it has one (CardDescription: the same
    // words as DescriptionBox, drawn here in UI Toolkit). The carousel and the deck builder's big card are uGUI, on a layer
    // under the dialogs (UIScreens), so a dialog that wants to show a card large has to draw it itself.
    //
    // Attach gives an element the gesture: a right click on it opens the preview, a finger held on it does until it lifts.
    // A preview closes on a click, Escape or a right click anywhere. Only one is open at a time.
    public static class CardPreview
    {
        private static VisualElement open;  // the overlay on show
        private static int openedFrame = -1;
        private static int hovered;          // elements with the pointer on them (Attach'ed)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            open = null;
            openedFrame = -1;
            hovered = 0;
        }

        public static bool Showing { get { return open != null && open.panel != null; } }

        // The dialog's cancel handling (UIScreens.ShowDialog's may_close) asks this first: true when the cancel was the
        // preview's (a preview closes, or the right click that opens one: it is no dialog's close), false for the dialog
        public static bool TakesCancel()
        {
            if (Showing)
            {
                // The right click that opened it arrives in this same frame, as the pointer's press and as the dialog's
                // right click, in either order: it must not close what it opened
                if (openedFrame != Time.frameCount)
                    Close();
                return true;
            }
            return hovered > 0 && !GameInput.EscapePressed();
        }

        public static void Close()
        {
            if (open != null)
                open.RemoveFromHierarchy();
            open = null;
        }

        // Right click or a held finger on the element shows the card (in root, the dialog's document). Whatever it does on
        // a click should check OpenedByHold first.
        public static void Attach(VisualElement element, Func<Card> card, VisualElement root)
        {
            bool in_element = false;
            element.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!in_element)
                    hovered++;
                in_element = true;
            });
            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (in_element)
                    hovered--;
                in_element = false;
            });
            element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (in_element)
                    hovered--;
                in_element = false;
            });

            IVisualElementScheduledItem pending = null;
            element.RegisterCallback<PointerDownEvent>(down =>
            {
                OpenedByHold = false;
                if (down.button == 1)
                {
                    GameAudio.PlaySfx("card_select");
                    Show(root, card());
                }
                else if (down.button == 0 && GameInput.IsTouch)
                {
                    // A finger has no right click: held still it shows the card (ElementTooltip's rhythm)
                    pending = element.schedule.Execute(() => Show(root, card())).StartingIn((long)(LongPress.Seconds * 1000));
                }
            });
            element.RegisterCallback<PointerUpEvent>(up =>
            {
                if (pending != null)
                    pending.Pause();
                if (up.button == 0 && GameInput.IsTouch)
                    Close(); // lifted: it goes
            });
            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (pending != null)
                    pending.Pause();
            });
        }

        // Whether the press that is ending showed the card by a held finger: the click that follows is no click. Set when
        // a hold shows it, cleared by the next press.
        public static bool OpenedByHold { get; private set; }

        private static void Show(VisualElement root, Card card)
        {
            if (card == null || root.panel == null)
                return;
            Close();
            OpenedByHold = GameInput.IsTouch;

            VisualElement overlay = new VisualElement { name = "CardPreview" };
            overlay.AddToClassList("card-preview");
            // A click anywhere puts it away (a held finger lifts instead); nothing under it is touched
            overlay.RegisterCallback<PointerDownEvent>(down =>
            {
                down.StopPropagation();
                if (!GameInput.IsTouch)
                    Close();
            });

            VisualElement art = new VisualElement { name = "CardPreview Art", pickingMode = PickingMode.Ignore };
            art.AddToClassList("card-preview__art");
            CardArt.Show(art, card._id);
            overlay.Add(art);

            CardDescription description = CardDescription.For(card);
            if (description != null)
                overlay.Add(Box(description));

            root.Add(overlay);
            open = overlay;
            openedFrame = Time.frameCount;
        }

        // The description box: the ability's icon on a cream disc, its title and its text
        private static VisualElement Box(CardDescription description)
        {
            VisualElement box = new VisualElement { name = "CardPreview Description", pickingMode = PickingMode.Ignore };
            box.AddToClassList("card-preview__box");

            VisualElement head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("card-preview__head");
            Sprite sprite = description.Icon != null ? SpriteCache.Load("Cards/DescriptionIcon/" + description.Icon) : null;
            if (sprite != null)
            {
                VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("card-preview__icon");
                icon.style.backgroundImage = new StyleBackground(sprite);
                head.Add(icon);
            }
            Label title = new Label(description.Title) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("card-preview__title");
            head.Add(title);
            box.Add(head);

            Label text = new Label(description.Text) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("card-preview__text");
            box.Add(text);
            return box;
        }
    }
}
