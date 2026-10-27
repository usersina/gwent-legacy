using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;

namespace Gwent.Battle
{
    // The battle's choices: who goes first (the Scoia'tael perk), panels of cards to choose from (a medic's revival, the
    // leaders' picks, Emhyr's look), and Eredin's discard. The match asks them (ChoiceAsked); the answers go back to it
    // as commands.
    public partial class BattleController
    {
        private BattlePickers OpenPanel()
        {
            pendingAction = PendingAction.PanelChoice;
            Panel.SetActive(true);
            return Panel.GetComponent<BattlePickers>();
        }

        // The match asks a side to choose: the player's choice opens (the opponent's is its own business; in a replay, what
        // the player chose follows: nothing opens)
        private async UniTask ShowChoice(MatchEvent e, MatchState all, CancellationToken ct)
        {
            ApplyView(e.State, all);
            Choice choice = e.State.Choice;
            if (choice == null || !choice.Seat || replay != null)
            {
                // A LAN battle's other player choosing who goes first takes its time (the AI answers at once)
                if (choice != null && choice.Kind == ChoiceKind.FirstPlayer && lan != null)
                    OpponentsMove("Your opponent is choosing who goes first");
                return;
            }
            switch (choice.Kind)
            {
                case ChoiceKind.FirstPlayer:
                    await While(() => BattleBanner.Busy, ct);
                    TurnPicker.SetActive(true); // the player's choice comes back through ChooseFirstPlayer
                    break;
                case ChoiceKind.Revive:
                    // The medic's unit, chosen from the discard pile, then placed (on a row: an agile one's)
                    OpenPanel().ShowToPlay(PickSource.Discard, choice.Options, true);
                    SetHider(true);
                    break;
                case ChoiceKind.WeatherFromDeck:
                    OpenPanel().ShowToPlay(PickSource.Deck, choice.Options, true);
                    break;
                case ChoiceKind.RestoreFromDiscard:
                    OpenPanel().ShowToDraw(PickSource.Discard, choice.Options, true);
                    break;
                case ChoiceKind.RestoreFromOpponentDiscard:
                    OpenPanel().ShowToDraw(PickSource.OpponentDiscard, choice.Options, true);
                    break;
                case ChoiceKind.Discard:
                    pendingAction = PendingAction.DiscardCards;
                    UIOverlay.Toast("Discard two cards from your hand: " + (GameInput.IsTouch ? "double-tap" : "double-click") + " each", 4f);
                    break;
                case ChoiceKind.DeckPick:
                    OpenPanel().ShowToDraw(PickSource.Deck, choice.Options, true);
                    break;
                case ChoiceKind.LookAtHand:
                    OpenPanel().ShowToView(choice.Options, true);
                    break;
            }
        }

        // A card was chosen in a picker to play (medic / weather from deck): it leaves its pile for the hand on screen
        // (until the match places it) and is placed on the board next (DirectlyPlaceCard tells the match where)
        public void TakePickedCardToPlay(PickSource field, int card_id, bool is_player_turn)
        {
            BattleSide info = Info(is_player_turn);
            switch (field)
            {
                case PickSource.Deck:
                    info.DeckList.Remove(card_id);
                    SetDeckObject(info, is_player_turn);
                    break;
                case PickSource.Discard:
                    info.DiscardList.Remove(card_id);
                    SetDiscardObject(info.DiscardList, is_player_turn);
                    break;
            }
            info.HandList.Add(card_id);
            pendingAction = PendingAction.PlacePickedCard;
        }

        // A card was chosen in a picker to take (leader abilities): it goes to the hand and the turn ends
        public void PickCardToHand(PickSource field, int card_id, bool is_player_turn)
        {
            if (is_player_turn)
                Submit(MatchCommand.Choose(card_id));
        }

        // Emhyr's look at the opponent's hand, closed: the turn ends
        public void EndLook()
        {
            Submit(MatchCommand.Choose(-1));
        }

        // Panel "Skip" button: give up the choice and end the turn (nothing is ever left half done)
        public void CancelPanel()
        {
            Panel.SetActive(false);
            GameAudio.PlaySfx("ui_click");
            DeselectCard();
            RefreshBoard();
            if (latest != null && latest.Phase == MatchPhase.Choice && latest.Choice != null && latest.Choice.Seat)
                Submit(MatchCommand.Skip());
            else
                pendingAction = PendingAction.None;
        }

        // Discard (Eredin, Destroyer of Worlds: discard 2, then pick 1 from the deck: the match asks it after the second)
        public void DiscardCard(int card_id, bool is_player_card, int hand_index = -1)
        {
            if (is_player_card && PlayerInfo.CanDiscard && !IsPresenting)
                Submit(MatchCommand.Discard(card_id, hand_index));
        }
    }
}
