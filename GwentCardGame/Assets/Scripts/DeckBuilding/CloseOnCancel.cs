using Gwent.Core;
using Gwent.UI;
using UnityEngine;

namespace Gwent.DeckBuilding
{
    public class CloseOnCancel : MonoBehaviour
    {
        // Closed by a right click (unless a dialog over it takes it). Escape closes it through the deck builder's own
        // Escape (DeckBuilder.OnEscape): both here, the order of the two scripts decided which acted
        void Update()
        {
            if (GameInput.SecondaryPressed() && !UIOverlay.BlocksCancel)
                gameObject.SetActive(false);
        }
    }
}
