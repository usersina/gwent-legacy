using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;

namespace Gwent.Battle
{
    // The leaders' abilities: lifting the player's leader, its targets, using it; a side's leader used, shown
    public partial class BattleController
    {
        //----------------------------------------------LEADERS---------------------------------------------------//
        // The player's leader is lifted like a card about to be played (The Witcher 3): a click on it takes it out of its
        // slot to the right, the slot stays empty and the places its ability acts on light up; its crown glows, and the
        // crown, only the crown, uses it (or X again, Enter). A click on the empty slot, Escape, a right click, another
        // card or passing puts it back. A click on the card alone never spends the ability.
        public bool LeaderSelected { get; private set; }
        private readonly List<GameObject> leaderTargets = new List<GameObject>();

        // The places the lifted leader's ability acts on (lit; they show it, the crown uses it)
        public IList<GameObject> LeaderTargets { get { return leaderTargets.AsReadOnly(); } }

        // Why the player's leader ability would do nothing now (null when it would act)
        public string LeaderProblem()
        {
            return LeaderRules.Unavailable(CurrentBoard(), PlayerInfo.Leader, true);
        }

        // A click (a tap) on the player's leader card, or on its empty slot once it is lifted: lifts it, or puts it back
        public void LeaderCardClicked()
        {
            if (LeaderSelected)
                PutBackSelectedCard();
            else
                SelectLeader();
        }

        // A click on the crown: it uses the lifted leader (the crown is not a button before)
        public void LeaderCrownClicked()
        {
            if (LeaderSelected)
                UseSelectedLeader();
            else
                GameAudio.PlaySfx("invalid");
        }

        // X: lifts the leader, then uses it
        public void LeaderKeyPressed()
        {
            if (LeaderSelected)
                UseSelectedLeader();
            else
                SelectLeader();
        }

        // A click on the board while the leader is lifted: the crown is what uses it
        public void HintLeaderCrown()
        {
            GameAudio.PlaySfx("invalid");
            UIOverlay.Toast(GameInput.IsTouch ? "Tap the glowing crown to use your leader ability" : "Click the glowing crown or press {Enter} to use your leader ability");
        }

        private void SelectLeader()
        {
            if (!CanUseLeader(true))
            {
                GameAudio.PlaySfx("invalid");
                if (PlayerInfo.leaderDisabled && replay == null) // a replay takes no input: nothing to say
                    UIOverlay.Toast("Your leader ability was cancelled by the opponent's leader");
                return;
            }
            // An ability that would do nothing is not chosen: why, at once
            string reason = LeaderProblem();
            if (reason != null)
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast(reason);
                return;
            }
            DeselectCard(); // a selected card goes back to the hand
            GameAudio.PlaySfx("card_select");
            LeaderSelected = true;
            GameObject card = LeaderCard();
            if (card != null)
            {
                // Out of its slot to the right; the empty slot puts it back when clicked
                CardHover hover = card.GetComponent<CardHover>();
                hover.isHoverable = false;
                hover.DestroyEffect();
                CardShowcase.Select(card, Db.Get(PlayerInfo.Leader));
            }
            View(true).Leader.UISetLifted(true);
            leaderTargets.AddRange(board.MarkLeaderTargets(PlayerInfo.Leader));
        }

        // Uses the chosen leader
        public void UseSelectedLeader()
        {
            if (LeaderSelected)
                ActivateLeader(PlayerInfo.Leader, true);
        }

        // The leader goes back to its slot (the showcase's glide back is the caller's), its crown and places lit off
        private void ReleaseLeader()
        {
            if (!LeaderSelected)
                return;
            LeaderSelected = false;
            GameObject card = LeaderCard();
            if (card != null)
                card.GetComponent<CardHover>().isHoverable = true;
            View(true).Leader.UISetLifted(false);
            board.UnmarkLeaderTargets(leaderTargets);
            leaderTargets.Clear();
            LightOffField(true);
            LightOffField(false);
        }

        // The player's leader card on the board
        private GameObject LeaderCard()
        {
            CardHover card = View(true).LeaderHolder.GetComponentInChildren<CardHover>();
            return card != null ? card.gameObject : null;
        }

        public bool CanUseLeader(bool is_player)
        {
            BattleSide info = Info(is_player);
            if (!info.canLeader || info.leaderDisabled || Db.Get(info.Leader).row != Row.LeaderActive)
                return false;
            return is_player ? IsPlayerActionAllowed : battleState == BattleState.EnemyTurn;
        }

        // Uses the player's leader ability (the match resolves it: at once, or with a choice to make). Not one that would
        // do nothing: the leader stays available, and why is said.
        public void ActivateLeader(int leader_id, bool is_player_turn)
        {
            // Only the player's own leader: clicking the opponent's crown must not apply their ability for the player
            if (!is_player_turn || leader_id != PlayerInfo.Leader)
            {
                GameAudio.PlaySfx("invalid");
                return;
            }
            if (!CanUseLeader(true))
            {
                GameAudio.PlaySfx("invalid");
                if (PlayerInfo.leaderDisabled && replay == null) // a replay takes no input: nothing to say
                    UIOverlay.Toast("Your leader ability was cancelled by the opponent's leader");
                return;
            }
            string reason = LeaderProblem();
            if (reason != null)
            {
                GameLog.Event(LogCategory.Battle, "leader_refused", "side", "player", "reason", reason);
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast(reason);
                return;
            }
            DeselectCard(false); // the chosen leader (or a selected card) leaves the showcase: the ability takes over
            CardShowcase.Clear();
            Submit(MatchCommand.Leader());
        }

        // A side used its leader: the opponent's comes in from above the board to the showcase first. Spent, its crown goes
        // dark; an ability that resolved at once shows its change (a horn, a scorch, the weather cleared...), one that asks
        // a choice waits for it (ShowChoice).
        private async UniTask ShowLeader(MatchEvent e, MatchState all, CancellationToken ct)
        {
            bool is_player = e.Seat;
            if (!is_player)
                await ShowcaseOpponent(Db.Get(e.Card), ct);
            else if (replay != null)
                await ShowcaseMine(e.Card, true, ct); // a replay: the player's leader shows as the opponent's does
            CardShowcase.Clear();
            BattleLog.BeforeChange(this);
            GameAudio.PlaySfx("leader");
            View(is_player).Leader.MarkUsed();
            board.BeginChange();
            ApplyView(e.State, all);
            RefreshBoard();
            BattleLog.LeaderUsed(this, is_player, e.Card, e.Flag ? "choice_pending" : "done");
            BoardChange change = board.EndChange();
            if (e.Flag)
                change.Flush();
            else
                await ShowChange(change, show => presenter.Play(change, -1, is_player, false, show), ct);
        }
    }
}
