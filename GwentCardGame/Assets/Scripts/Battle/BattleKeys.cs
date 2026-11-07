using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Gwent.Battle
{
    // The battle's keyboard controls (desktop), as in The Witcher 3: Left/Right (A/D) go through the hand and Enter takes
    // the card; Up/Down (W/S) go through the places it can go and Enter plays it there (or where it fits, when only one
    // place does); Escape or a right click puts it back. X chooses the leader (as a click on it does), X again or Enter
    // uses it. Space (hold to pass) is the pass button's; the redraw and the card picker have their own keys. In a LAN
    // battle, T opens the chat window to type, E the quick lines beside your portrait (ChatView: their own keys while open).
    public class BattleKeys : MonoBehaviour
    {
        private const string PlaceMark = "KeyPlace";
        private GameObject focused;  // the hand card the keys are on: raised, shown on the right
        private GameObject place;    // the place the keys chose for the selected card: framed
        private GameObject placeFor; // the selected card it was chosen for

        private static BattleKeys instance;

        // The hand card the keys are on, if any
        public GameObject Focused { get { return focused; } }
        public GameObject Place { get { return place; } }

        private void Awake()
        {
            instance = this;
        }

        // The mouse went onto a card: it takes over from the keys (a nudge of the mouse elsewhere does not)
        public static void PointerEntered(GameObject card)
        {
            if (instance == null || instance.focused == null)
                return;
            if (instance.focused != card)
                instance.Unfocus();
            instance.focused = null;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            BattleController battle = BattleController.Instance;
            if (keyboard == null || battle == null || UIOverlay.HasModal || battle.OpponentAway)
                return; // a dialog's keys; or the board waiting under the LAN notice (its button has the keys)

            // Right-click puts a selected card back in the hand (The Witcher 3), and the chosen leader back in its place
            if (GameInput.SecondaryPressed() && (battle.selectedCard != null || battle.LeaderSelected) && battle.IsPlayerActionAllowed)
                battle.PutBackSelectedCard();

            // A chosen place is only for the card it was chosen for
            if (place != null && (battle.selectedCard != placeFor || !Places(battle).Contains(place)))
                MarkPlace(null, null);

            if (keyboard.xKey.wasPressedThisFrame)
                LeaderKey(battle);
            if (keyboard.tKey.wasPressedThisFrame)
                ChatView.KeyPressed(true);
            if (keyboard.eKey.wasPressedThisFrame)
                ChatView.KeyPressed(false);

            // The redraw, the card picker and the Scoia'tael choice have their own keys
            if (RedrawPicker.IsOpen || battle.Panel.activeInHierarchy || battle.TurnPicker.activeInHierarchy || CardCarousel.UsedKeyThisFrame)
                return;
            if (Pressed(keyboard.leftArrowKey, keyboard.aKey))
                StepHand(battle, -1);
            if (Pressed(keyboard.rightArrowKey, keyboard.dKey))
                StepHand(battle, 1);
            if (Pressed(keyboard.upArrowKey, keyboard.wKey))
                StepPlace(battle, -1);
            if (Pressed(keyboard.downArrowKey, keyboard.sKey))
                StepPlace(battle, 1);

            // Enter on a focused button is that button's (dialogs, the leader after X)
            GameObject button = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool button_focused = button != null && button.activeInHierarchy;
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && !button_focused)
                EnterKey(battle);
        }

        private static bool Pressed(KeyControl arrow, KeyControl letter)
        {
            return arrow.wasPressedThisFrame || letter.wasPressedThisFrame;
        }

        // The first lifts the leader, the second uses it
        private void LeaderKey(BattleController battle)
        {
            Unfocus();
            battle.LeaderKeyPressed();
            if (battle.LeaderSelected)
                UIOverlay.Toast(Loc.T(Loc.UI, "battle.leader_keys"));
        }

        //----------------------------------------The hand----------------------------------------//
        // The hand's cards from left to right (not the room kept for a card that just left)
        private static List<GameObject> HandCards(BattleController battle)
        {
            List<GameObject> hand = new List<GameObject>();
            foreach (Transform child in battle.PlayerField.transform.Find("Hand"))
                if (child.GetComponent<CardFace>() != null && child.gameObject.activeSelf)
                    hand.Add(child.gameObject);
            return hand;
        }

        // The next card to the left or right (from the selected card, else the one the keys are on, else an end)
        private void StepHand(BattleController battle, int step)
        {
            if (battle.pendingAction != PendingAction.None && battle.pendingAction != PendingAction.DiscardCards)
                return;
            List<GameObject> hand = HandCards(battle);
            if (hand.Count == 0)
                return;
            GameObject from = battle.selectedCard != null && hand.Contains(battle.selectedCard) ? battle.selectedCard : hand.Contains(focused) ? focused : null;
            GameObject next = from != null ? hand[Mathf.Clamp(hand.IndexOf(from) + step, 0, hand.Count - 1)] : hand[step > 0 ? 0 : hand.Count - 1];
            if (next == from && from == focused && !battle.LeaderSelected)
                return; // at an end of the hand
            if ((from != null && from == battle.selectedCard) || battle.LeaderSelected)
                battle.PutBackSelectedCard();
            Focus(next);
        }

        private void Focus(GameObject card)
        {
            Unfocus();
            focused = card;
            CardHover hover = card.GetComponent<CardHover>();
            hover.ShowPreview();
            if (hover.isHoverable)
                hover.TranslateUp();
        }

        private void Unfocus()
        {
            if (focused != null)
            {
                CardHover hover = focused.GetComponent<CardHover>();
                if (hover.isHoverable) // a selected card stays up
                    hover.TranslateDown();
                hover.DestroyEffect();
            }
            focused = null;
        }

        private void EnterKey(BattleController battle)
        {
            if (battle.LeaderSelected)
            {
                battle.UseSelectedLeader();
                return;
            }
            if (battle.selectedCard != null)
            {
                if (place != null)
                    PlayOn(battle, place);
                else
                    PlayWhereItFits(battle);
                return;
            }
            if (focused == null || !HandCards(battle).Contains(focused))
                return;
            // Eredin, Destroyer of Worlds: Enter discards the card, as a double-click does
            if (battle.PlayerInfo.CanDiscard)
                focused.GetComponent<CardDiscard>().Discard();
            else
                focused.GetComponent<CardSelect>().OnCardSelect();
        }

        //----------------------------------------Places----------------------------------------//
        // Where the selected card can go, from the top of the board down: the lit rows (both sides: spies), the lit
        // weather board, the units framed for a decoy
        private static List<GameObject> Places(BattleController battle)
        {
            List<GameObject> places = new List<GameObject>();
            if (!battle.IsSelectedCardPlayable())
                return places;
            places.AddRange(battle.PlayerField.GetComponent<RowPicker>().highLightedRows);
            places.AddRange(battle.EnemyField.GetComponent<RowPicker>().highLightedRows);
            if (battle.WeatherField.GetComponent<WeatherSlot>().isWeatherCard)
                places.Add(battle.WeatherField);
            places.AddRange(battle.PlayerField.GetComponentsInChildren<Transform>()
                .Where(t => t.name == BoardView.DecoyFrame).Select(t => t.parent.gameObject));
            return places.OrderByDescending(p => CardMotion.ScreenRect((RectTransform)p.transform).center.y)
                .ThenBy(p => CardMotion.ScreenRect((RectTransform)p.transform).center.x).ToList();
        }

        private void StepPlace(BattleController battle, int step)
        {
            List<GameObject> places = Places(battle);
            if (places.Count == 0)
            {
                if (battle.selectedCard != null)
                    GameAudio.PlaySfx("invalid");
                return;
            }
            int index = places.IndexOf(place);
            index = index < 0 ? (step > 0 ? 0 : places.Count - 1) : Mathf.Clamp(index + step, 0, places.Count - 1);
            if (places[index] == place)
                return;
            GameAudio.PlaySfx("card_select");
            MarkPlace(places[index], battle.selectedCard);
        }

        private void MarkPlace(GameObject target, GameObject card)
        {
            if (place != null)
            {
                Transform mark = place.transform.Find(PlaceMark);
                if (mark != null)
                {
                    mark.SetParent(null, false);
                    Destroy(mark.gameObject);
                }
            }
            place = target;
            placeFor = card;
            if (target != null)
                foreach (UnityEngine.UI.Image side in UIKit.Frame(target.transform, PlaceMark, UITheme.Current.goldBright, 4, 3).GetComponentsInChildren<UnityEngine.UI.Image>())
                    UIMotion.Pulse(side);
        }

        private static void PlayOn(BattleController battle, GameObject target)
        {
            RowClick row = target.GetComponent<RowClick>();
            if (row != null)
                row.OnRowClick();
            else if (target == battle.WeatherField)
                target.GetComponent<WeatherSlot>().OnWeatherClick();
            else
                battle.TryPlaySelectedDecoyOnCard(target);
        }

        private static void PlayWhereItFits(BattleController battle)
        {
            if (!battle.IsSelectedCardPlayable())
                return;
            List<GameObject> places = Places(battle);
            if (places.Count != 1)
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast(Loc.T(Loc.UI, places.Count == 0 ? "battle.keys_nowhere" : "battle.keys_choose"));
                return;
            }
            PlayOn(battle, places[0]);
        }
    }
}
