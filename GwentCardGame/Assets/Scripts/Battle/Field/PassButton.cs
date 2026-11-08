using System;
using System.Collections;
using System.Collections.Generic;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // Hold to pass: the left button (or a finger) on the button, or the Space key as in The Witcher 3
    public class PassButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private bool pointerDown;
        private float pointerDownTimer;
        private bool keyHeld;

        public float requiredHoldTime;
        public UnityEvent onLongClick;
        public Image fillImage;

        // Held with the left button (or a finger), like every other button
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                pointerDown = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Reset();
        }

        // Sliding off the button cancels a press (not a held Space)
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!keyHeld)
                Reset();
        }

        // The button is hidden when the turn changes: a press in progress must not carry over to the next turn
        private void OnDisable()
        {
            Reset();
        }

        private void Start()
        {
            // A thin bar: a finger holds it over a taller strip
            UIKit.GrowTouchArea((RectTransform)transform, new Vector2(280, 76));
            // The prefab says "Press to pass" in English; a keyboard replaces it below, with its key
            TMPro.TextMeshProUGUI hint = GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (hint != null)
                hint.text = Loc.T(Loc.UI, "battle.press_to_pass");
            // The Witcher 3's "[SPACE] [HOLD] Pass" hint, on keyboards: its key in a cap before the words, both centred
            if (GameInput.IsTouch)
                return;
            TMPro.TextMeshProUGUI label = GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (label == null)
                return;
            label.text = Loc.T(Loc.UI, "battle.hold_to_pass");
            RectTransform cap = UIKit.KeyCap(transform, "Space", 26);
            float words = label.GetPreferredValues(label.text).x;
            float together = cap.sizeDelta.x + 12 + words;
            RectTransform rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, together + 48), rect.sizeDelta.y);
            cap.Place(new Vector2(0.5f, 0.5f), new Vector2((cap.sizeDelta.x - together) / 2, 0), cap.sizeDelta);
            label.alignment = TMPro.TextAlignmentOptions.Midline;
            label.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2((together - words) / 2, label.rectTransform.anchoredPosition.y), new Vector2(words + 4, label.rectTransform.rect.height));
        }

        private void Update()
        {
            // A dialog (the pause menu) cancels any hold: the turn must not pass behind it
            if (UIOverlay.HasModal)
            {
                if (pointerDown)
                    Reset();
                return;
            }

            // Space counts while it stays down; releasing it cancels the hold
            Keyboard keyboard = Keyboard.current;
            bool key_down = keyboard != null && keyboard.spaceKey.isPressed && !UIOverlay.HasModal;
            if (key_down && !keyHeld && keyboard.spaceKey.wasPressedThisFrame)
            {
                keyHeld = true;
                pointerDown = true;
            }
            else if (keyHeld && !key_down)
            {
                Reset();
            }

            if (pointerDown)
            {
                // Real time: a hold is a gesture, and a hitch (game time is capped per frame) must not stretch it
                pointerDownTimer += Time.unscaledDeltaTime;
                if (pointerDownTimer >= requiredHoldTime)
                {
                    if (onLongClick != null)
                        onLongClick.Invoke();

                    Reset();
                }
                fillImage.fillAmount = pointerDownTimer / requiredHoldTime;
            }
        }

        private void Reset()
        {
            pointerDown = false;
            keyHeld = false;
            pointerDownTimer = 0;
            fillImage.fillAmount = 0;
        }
    }
}
