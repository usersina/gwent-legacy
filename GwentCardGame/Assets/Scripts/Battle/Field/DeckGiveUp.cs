using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gwent.Battle
{
    // Giving up as in Yu-Gi-Oh: a hand laid on your own deck. In your turn, the cursor over your deck turns into a hand
    // (GameCursor) and a click asks whether to give up; with touch controls, a finger held on the deck asks (a tap does
    // nothing). It counts as a loss. On the player's deck only (BattleController adds it).
    public class DeckGiveUp : MonoBehaviour, IPointerClickHandler
    {
        // The deck takes a hand now: the player's turn, nothing pending or being shown
        public bool Available
        {
            get
            {
                BattleController battle = BattleController.Instance;
                return battle != null && battle.CanGiveUp;
            }
        }

        private void Awake()
        {
            LongPress.On(gameObject, () =>
            {
                if (!Available)
                    return false;
                Ask();
                return true;
            }, null);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || GameInput.IsTouch || !Available)
                return;
            Ask();
        }

        private static void Ask()
        {
            GameLog.Event(LogCategory.Ui, "give_up_asked");
            UIOverlay.Confirm(Loc.T(Loc.UI, "battle.give_up_ask"), Loc.T(Loc.UI, "battle.give_up"), Loc.T(Loc.UI, "battle.keep_playing"), () =>
            {
                if (BattleController.Instance != null)
                    BattleController.Instance.GiveUp();
            });
        }
    }
}
