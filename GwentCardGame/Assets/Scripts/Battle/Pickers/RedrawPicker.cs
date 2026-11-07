using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // The redraw phase as in The Witcher 3: the hand in the carousel. A side card (or the arrow keys, A/D, the mouse
    // wheel) comes to the front; the front card (or Enter) is redrawn; Done (or Space) keeps the hand. Double-clicking a
    // card in the hand still works, and the carousel follows the hand.
    public class RedrawPicker : CardCarousel
    {
        private const float RedrawCooldown = 0.35f;
        private const float RedrawFlightSeconds = 0.3f;

        private static RedrawPicker current;

        private BattleController controller;
        private TextMeshProUGUI counter;
        private int counterShows = -1;
        private float lastRedraw = float.NegativeInfinity;
        private int flights; // redraws still on their way

        // While open, hovering hand cards shows no big preview on the right
        public static bool IsOpen { get { return current != null; } }

        // A card is redrawn only once it has been at the front for a moment: a quick double-click on a card further away
        // brings it forward and does not swap it by accident. hand_index: its place in the hand, which the carousel shows
        // in order (with two alike, the one clicked), when known.
        public static bool IsReadyToRedraw(int card_id, int hand_index = -1)
        {
            return current != null && current.FocusedCardId == card_id && (hand_index < 0 || current.focus == hand_index) && current.FrontIsReady;
        }

        // Brings a card of the hand to the front (a click on it in the hand)
        public static void Bring(int card_id, int hand_index = -1)
        {
            if (current == null)
                return;
            if (hand_index >= 0 && hand_index < current.shown.Count && current.shown[hand_index] == card_id)
                current.SetFocus(hand_index);
            else if (current.shown.Contains(card_id))
                current.SetFocus(current.shown.IndexOf(card_id));
        }

        // A redrawn card is still on its way (the phase ends once it has landed)
        public static bool IsAnimating { get { return current != null && current.flights > 0; } }

        // The whole redraw phase UI: a dark band "Choose a card to redraw. 0/2" (with the Done button, Space in its cap)
        // above the carousel. Returns its root, which the battle destroys when the phase ends.
        public static GameObject ShowPhase(BattleController controller)
        {
            RectTransform root = UIKit.Rect("MulliganPrompt", UIOverlay.Root).Stretch();
            // Space is Done too (as it passes in the battle: "I'm done")
            TextMeshProUGUI counter;
            Band(root, "", Loc.T(Loc.UI, "redraw.done"), GameInput.IsTouch ? null : "Space", controller.FinishCardExchange, out counter);

            RedrawPicker picker = Create(root, controller, new Vector2(0, 50));
            picker.counter = counter;
            picker.UpdateCounter();
            return root.gameObject;
        }

        public static RedrawPicker Create(Transform parent, BattleController controller, Vector2 center)
        {
            RedrawPicker picker = Create<RedrawPicker>(parent, center);
            picker.name = "RedrawPicker";
            picker.controller = controller;
            picker.Rebuild(controller.PlayerInfo.HandList);
            current = picker;
            return picker;
        }

        private void OnDestroy()
        {
            if (current == this)
                current = null;
        }

        protected override void Update()
        {
            if (!SameHand())
                FollowHand();
            base.Update();
            UpdateCounter();
        }

        protected override bool TakesInput()
        {
            return base.TakesInput() && controller.PlayerInfo.CanExchange;
        }

        private void UpdateCounter()
        {
            int redrawn = controller.PlayerInfo.CardsExchanged;
            if (counter == null || counterShows == redrawn)
                return;
            counterShows = redrawn;
            counter.text = Loc.T(Loc.UI, "redraw.choose", redrawn);
        }

        //----------------------------------------Cards----------------------------------------//
        private bool SameHand()
        {
            List<int> hand = controller.PlayerInfo.HandList;
            if (hand.Count != shown.Count)
                return false;
            for (int i = 0; i < hand.Count; i++)
                if (hand[i] != shown[i])
                    return false;
            return true;
        }

        // A redraw replaces one card in place: as in The Witcher 3, it goes down into the deck and the new card comes up
        // from the deck into its place
        private void FollowHand()
        {
            int replaced = ReplacedIndex();
            Rect slot = replaced >= 0 ? CardMotion.ScreenRect(CardAt(replaced)) : new Rect();
            int redrawn = replaced >= 0 ? shown[replaced] : -1;
            Rebuild(controller.PlayerInfo.HandList);
            if (replaced >= 0)
                RedrawFlight(redrawn, slot, replaced, destroyCancellationToken).Forget();
        }

        private int ReplacedIndex()
        {
            List<int> hand = controller.PlayerInfo.HandList;
            if (hand.Count != shown.Count || CardCount != shown.Count)
                return -1;
            int changed = -1;
            for (int i = 0; i < hand.Count; i++)
                if (hand[i] != shown[i])
                {
                    if (changed >= 0)
                        return -1;
                    changed = i;
                }
            return changed;
        }

        private async UniTaskVoid RedrawFlight(int redrawn, Rect slot, int index, CancellationToken ct)
        {
            flights++;
            hidden = index;
            Rect deck = controller.DeckScreenRect(true);
            await Glide(Ghost(redrawn, slot), () => deck, ct);
            if (index < CardCount)
            {
                RectTransform target = CardAt(index);
                await Glide(Ghost(shown[index], deck), () => target != null ? CardMotion.ScreenRect(target) : deck, ct);
            }
            if (hidden == index)
                hidden = -1;
            flights--;
        }

        // An image of a card on the carousel's canvas (above the board), over a screen rectangle
        private RectTransform Ghost(int card_id, Rect screen_rect)
        {
            Image art = UIKit.NewImage(transform, "RedrawGhost", null, Color.white);
            CardArt.Show(art, card_id);
            art.preserveAspect = true;
            PlaceOnScreen(art.rectTransform, screen_rect);
            return art.rectTransform;
        }

        // Glides a ghost to a screen rectangle (easing out), then removes it
        private async UniTask Glide(RectTransform ghost, Func<Rect> to, CancellationToken ct)
        {
            Rect from = CardMotion.ScreenRect(ghost);
            float age = 0;
            while (age < RedrawFlightSeconds && ghost != null)
            {
                age += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(age / RedrawFlightSeconds);
                float eased = 1 - (1 - t) * (1 - t) * (1 - t);
                Rect target = to();
                PlaceOnScreen(ghost, new Rect(Vector2.LerpUnclamped(from.position, target.position, eased), Vector2.LerpUnclamped(from.size, target.size, eased)));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (ghost != null)
                Destroy(ghost.gameObject);
        }

        //----------------------------------------Input----------------------------------------//
        protected override void Choose()
        {
            RedrawFocused();
        }

        public void RedrawFocused()
        {
            // Not a card still on its way or just brought forward (the same guards as a click)
            if (Time.unscaledTime - lastRedraw < RedrawCooldown || focus >= shown.Count || IsAnimating || !IsReadyToRedraw(FocusedCardId))
                return;
            lastRedraw = Time.unscaledTime;
            controller.RedrawPlayerCard(shown[focus], focus);
        }

        protected override void HandleKeys(Keyboard keyboard)
        {
            base.HandleKeys(keyboard);
            // Space keeps the hand, like the Done button
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                controller.FinishCardExchange();
        }
    }
}
