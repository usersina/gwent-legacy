using Gwent.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gwent.Battle
{
    // On a side's discard pile: a click opens the pile in the discard viewer (the cards lying on the pile forward their
    // clicks here too). With the leader lifted, a click on the pile its ability takes a unit from says to use the crown.
    public class DiscardPileBrowser : MonoBehaviour, IPointerClickHandler
    {
        public bool isPlayer;

        public bool HasCards
        {
            get
            {
                BattleController battle = BattleController.Instance;
                return battle != null && (isPlayer ? battle.PlayerInfo : battle.EnemyInfo).DiscardList.Count > 0;
            }
        }

        private void Awake()
        {
            gameObject.AddComponent<Gwent.UI.UITooltip>().text = Tooltip;
        }

        // Whose pile it is and what a click does
        private string Tooltip()
        {
            BattleController battle = BattleController.Instance;
            if (battle == null)
                return null;
            int count = (isPlayer ? battle.PlayerInfo : battle.EnemyInfo).DiscardList.Count;
            string pile = Loc.T(Loc.UI, isPlayer ? "pick.your_discard" : "pick.opp_discard");
            if (count == 0)
                return Loc.T(Loc.UI, "pile.empty", pile);
            return Loc.T(Loc.UI, "pile.tooltip", pile, count == 1 ? Loc.T(Loc.UI, "pile.card_one") : Loc.T(Loc.UI, "end.cards", count),
                Loc.T(Loc.UI, GameInput.IsTouch ? "pile.look_tap" : "pile.look_click"));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Clicked();
        }

        public void Clicked()
        {
            // The lifted leader (a leader that takes from a pile lights it): its crown uses it, not the pile
            BattleController battle = BattleController.Instance;
            if (battle != null && battle.LeaderSelected)
            {
                battle.HintLeaderCrown();
                return;
            }
            Browse();
        }

        public void Browse()
        {
            BattleController battle = BattleController.Instance;
            if (battle == null || !HasCards)
                return;
            GameAudio.PlaySfx("card_select");
            DiscardViewer.Open((isPlayer ? battle.PlayerInfo : battle.EnemyInfo).DiscardList, isPlayer);
        }
    }
}
