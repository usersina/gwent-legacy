using System;
using Gwent.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gwent.UI
{
    // Touch controls have no hover: a finger held still on an element shows what hovering shows on desktop (a card on
    // the right, a tooltip) until it lifts. A press that showed something is no tap: lifting the finger does not select,
    // play or press anything. Does nothing with the mouse (desktop has hover).
    public class LongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public const float Seconds = 0.4f;

        private Func<bool> show; // true if it showed something
        private Action hide;

        private PointerEventData press;
        private Vector2 pressedOn; // the event's own press position is the last button's the input module went through
        private float pressedAt;
        private bool showing;

        public static LongPress On(GameObject target, Func<bool> show, Action hide)
        {
            LongPress press = target.GetComponent<LongPress>();
            if (press == null)
                press = target.AddComponent<LongPress>();
            press.show = show;
            press.hide = hide;
            return press;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!GameInput.IsTouch || eventData.button != PointerEventData.InputButton.Left)
                return;
            End();
            press = eventData;
            pressedOn = eventData.position;
            pressedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == (press != null ? press.pointerId : int.MinValue))
                End();
        }

        private void Update()
        {
            if (press == null || showing)
                return;
            // The board is being zoomed or moved: no hold
            if (GameInput.MovingBoard)
            {
                press = null;
                return;
            }
            // A finger that moves is scrolling or dragging, not holding
            if ((press.position - pressedOn).magnitude > Slop())
            {
                press = null;
                return;
            }
            if (Time.unscaledTime - pressedAt < Seconds)
                return;
            showing = show != null && show();
            if (showing)
                press.eligibleForClick = false;
            else
                press = null; // nothing to show here now: lifting the finger is a tap as usual
        }

        // How far a held finger may wander: the drag threshold, at least about 2.5 mm
        private static float Slop()
        {
            float threshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10;
            return Mathf.Max(threshold, Screen.dpi > 0 ? Screen.dpi * 0.1f : 20);
        }

        private void OnDisable()
        {
            End();
        }

        private void End()
        {
            bool shown = showing;
            press = null;
            showing = false;
            if (shown && hide != null)
                hide();
        }
    }
}
