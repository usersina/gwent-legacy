using System;
using Gwent.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gwent.UI
{
    // A small themed hint above a control while the pointer rests on it (with touch controls: while a finger is held on it)
    public class UITooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float Delay = 0.3f;
        private const float Gap = 10f;

        // Asked each time the tooltip opens, so it can describe the control's current state
        public Func<string> text;

        private float enteredAt = -1;
        private GameObject shown;

        private void Awake()
        {
            LongPress.On(gameObject, () => { Show(); return shown != null; }, Hide);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // A finger also "enters" what it touches: its tooltip waits for the long press, which is no tap
            if (!GameInput.IsTouch)
                enteredAt = Time.unscaledTime;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            enteredAt = -1;
            Hide();
        }

        private void Update()
        {
            if (enteredAt >= 0 && shown == null && Time.unscaledTime - enteredAt >= Delay)
                Show();
        }

        private void OnDisable()
        {
            enteredAt = -1;
            Hide();
        }

        private void OnDestroy()
        {
            Hide();
        }

        private void Show()
        {
            string message = text != null ? text() : null;
            if (string.IsNullOrEmpty(message) || shown != null)
                return;

            RectTransform root = (RectTransform)UIOverlay.Root;
            RectTransform panel = UIKit.Panel(root, "Tooltip", true);
            TextMeshProUGUI label = UIKit.Text(panel, message, UIKit.TextStyle.Label, TextAlignmentOptions.Center);
            label.fontSize = 26;
            label.color = UITheme.Current.text;
            Vector2 size = label.GetPreferredValues(message) + new Vector2(36, 18);
            label.rectTransform.Stretch(18, 18, 0, 2);
            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>())
                graphic.raycastTarget = false; // never steals the hover from the control

            // Above the control's top edge, kept on screen; below its bottom edge when there is no room above (a control at
            // the top of the screen: the battle's menu button and "?")
            RectTransform target = (RectTransform)transform;
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Canvas canvas = target.GetComponentInParent<Canvas>().rootCanvas;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 top, bottom;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, RectTransformUtility.WorldToScreenPoint(camera, (corners[1] + corners[2]) / 2), null, out top);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, RectTransformUtility.WorldToScreenPoint(camera, (corners[0] + corners[3]) / 2), null, out bottom);
            float half_width = root.rect.width / 2;
            bool above = top.y + Gap + size.y <= root.rect.height / 2 - 8;
            Vector2 local = above ? top + new Vector2(0, Gap) : bottom - new Vector2(0, Gap);
            local.x = Mathf.Clamp(local.x, -half_width + size.x / 2 + 8, half_width - size.x / 2 - 8);
            panel.Place(new Vector2(0.5f, 0.5f), local, size, new Vector2(0.5f, above ? 0f : 1f));
            shown = panel.gameObject;
        }

        private void Hide()
        {
            if (shown != null)
                Destroy(shown);
            shown = null;
        }
    }
}
