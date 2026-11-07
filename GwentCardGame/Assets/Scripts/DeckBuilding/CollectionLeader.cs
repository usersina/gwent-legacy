using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gwent.DeckBuilding
{
    // The deck's leader in the middle column: a click (a tap) opens the faction's leaders in the carousel (LeaderChoice)
    public class CollectionLeader : MonoBehaviour, IPointerClickHandler
    {
        public DeckBuilder deckBuilder;

        private void Awake()
        {
            gameObject.AddComponent<UITooltip>().text = () => Loc.T(Loc.UI, GameInput.IsTouch ? "deck.leader_tap" : "deck.leader_click");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                OpenLeaders();
        }

        private void OpenLeaders()
        {
            if (deckBuilder.my_deck.Name == Gwent.Rules.DeckRules.EmptyDeckName)
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast(Loc.T(Loc.UI, "deck.make_first", DeckSummary.NewDeckOption));
                return;
            }
            LeaderChoice.Open(deckBuilder);
        }
    }
}
