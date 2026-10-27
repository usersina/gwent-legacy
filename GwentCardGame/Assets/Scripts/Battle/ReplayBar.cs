using System.Collections.Generic;
using Gwent.Core;
using Gwent.Replays;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // A replay's controls, where the pass button is (no turn of the player's to pass in a replay), in the board's left
    // column: a round back, a move back, play or pause, a move forward, a round forward, the speed (1x, 2x, 4x). On a
    // computer, the keys too: Space plays or pauses, Left and Right go a move back or forward (with Shift, a round), Up and
    // Down change the speed. They stay put while the board is zoomed (on the battle's canvas, not in its zoomed layer), and
    // the end screen covers them. How far the battle is: a thin gold line in the line between the two halves of the board
    // (where a LAN battle's turn clock burns; under the cards and their highlights), a mark where each round starts. Right of the hand at the bottom, the switch: the other
    // side's view (S), the board turned over; it moves with the hand.
    public class ReplayBar : MonoBehaviour
    {
        private const float ButtonSize = 46;
        private const float Gap = 8;
        private const float LineHeight = 4;
        private const int Buttons = 6;
        private const float SwitchGap = 12;   // from the hand's right edge to the switch

        private ReplayPlayer replay;
        private BattleController battle;
        private Image playIcon;
        private TextMeshProUGUI speedText;
        private RectTransform progress, fill, head;
        private CanvasGroup progressGroup;
        private float width;

        public static ReplayBar Attach(BattleController battle, ReplayPlayer replay)
        {
            GameObject canvas = GameObject.Find("Main Canvas");
            if (canvas == null)
                return null;
            RectTransform root = (RectTransform)canvas.transform;
            RectTransform rect = UIKit.Rect("ReplayBar", root);
            if (battle.PostGame != null && battle.PostGame.transform.parent == root)
                rect.SetSiblingIndex(battle.PostGame.transform.GetSiblingIndex()); // under the end screen
            ReplayBar bar = rect.gameObject.AddComponent<ReplayBar>();
            bar.replay = replay;
            bar.battle = battle;
            bar.Build(rect);
            bar.PlaceBar(rect, root);
            bar.AddSwitch();
            return bar;
        }

        // The switch, right of the hand at the bottom, beside it in the board's layer (zoomed, it stays by the hand): a size
        // larger on a phone, if the room before the deck allows
        private void AddSwitch()
        {
            RectTransform hand = battle.BottomHand;
            Transform field = hand.parent;
            Button button = UIKit.RoundButton(field, "SwitchSide", null, UITheme.Current.iconSwitch, Deselecting(replay.SwitchSide), ButtonSize, true);
            button.gameObject.AddComponent<UITooltip>().text = () => "Switch sides" + KeyName("S");
            RectTransform rect = (RectTransform)button.transform;
            Vector3 hand_right = field.InverseTransformPoint(hand.TransformPoint(new Vector3(hand.rect.xMax, hand.rect.center.y, 0)));
            float scale = UIKit.PhoneScale;
            RectTransform slot = battle.BottomDeck.Find("DeckHolder") as RectTransform;
            if (slot != null)
            {
                float deck_left = field.InverseTransformPoint(slot.TransformPoint(new Vector3(slot.rect.xMin, 0, 0))).x;
                scale = Mathf.Clamp((deck_left - hand_right.x - 2 * SwitchGap) / ButtonSize, 0.75f, scale);
            }
            rect.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ButtonSize, ButtonSize));
            rect.localPosition = new Vector3(hand_right.x + SwitchGap + ButtonSize * scale / 2, hand_right.y, 0);
            rect.localScale = new Vector3(scale, scale, 1);
            UIKit.GrowTouchArea(rect, new Vector2(ButtonSize + SwitchGap, ButtonSize + 30));
        }

        private void Build(RectTransform rect)
        {
            UITheme theme = UITheme.Current;
            width = Buttons * ButtonSize + (Buttons - 1) * Gap;
            rect.sizeDelta = new Vector2(width, ButtonSize);
            string shift = GameInput.IsTouch ? "" : "Shift+";
            float x = -width / 2 + ButtonSize / 2;
            AddButton(rect, "RoundBack", theme.iconRoundBack, replay.PreviousRound, x, () => "Round back" + KeyName(shift + "Left"));
            x += ButtonSize + Gap;
            AddButton(rect, "MoveBack", theme.iconStepBack, replay.PreviousMove, x, () => "Move back" + KeyName("Left"));
            x += ButtonSize + Gap;
            Button play = AddButton(rect, "Play", theme.iconPause, replay.TogglePause, x, () => (Playing ? "Pause" : "Play") + KeyName("Space"));
            playIcon = play.transform.Find("Icon").GetComponent<Image>();
            x += ButtonSize + Gap;
            AddButton(rect, "MoveForward", theme.iconStepForward, replay.NextMove, x, () => "Next move" + KeyName("Right"));
            x += ButtonSize + Gap;
            AddButton(rect, "RoundForward", theme.iconRoundForward, replay.NextRound, x, () => "Next round" + KeyName(shift + "Right"));
            x += ButtonSize + Gap;
            Button speed = UIKit.RoundButton(rect, "Speed", "1\u00D7", null, Deselecting(replay.NextSpeed), ButtonSize, true);
            PlaceButton(speed, x);
            speedText = speed.GetComponentInChildren<TextMeshProUGUI>();
            speedText.fontSize = ButtonSize * 0.5f;
            speedText.rectTransform.Stretch(0, 2, 0, 0);
            speed.gameObject.AddComponent<UITooltip>().text = () => "Speed" + KeyName("Up/Down");
        }

        // How far: on the iron rod between the two halves of the board (in it: it moves with a zoom), the part shown in gold,
        // a soft glow at its end, a mark where each round starts
        private void BuildProgress(RectTransform divider)
        {
            UITheme theme = UITheme.Current;
            progress = UIKit.Rect("ReplayProgress", divider);
            progress.anchorMin = new Vector2(0, 0.5f);
            progress.anchorMax = new Vector2(1, 0.5f);
            progress.pivot = new Vector2(0.5f, 0.5f);
            progress.offsetMin = new Vector2(BattleController.DividerInset, -LineHeight / 2);
            progress.offsetMax = new Vector2(-BattleController.DividerInset, LineHeight / 2);
            progressGroup = progress.gameObject.AddComponent<CanvasGroup>();
            progressGroup.blocksRaycasts = false;
            progressGroup.interactable = false;
            Image line = UIKit.NewImage(progress, "Line", null, UITheme.WithAlpha(theme.gold, 0.12f));
            line.rectTransform.Stretch();
            Image shown = UIKit.NewImage(progress, "Shown", null, theme.gold);
            fill = shown.rectTransform.Stretch();
            List<MatchEvent> events = replay.Events;
            for (int i = 1; i < events.Count; i++)
            {
                if (events[i].Kind != MatchEventKind.RoundStarted)
                    continue;
                Image mark = UIKit.NewImage(progress, "Round", null, theme.goldBright);
                float at = (float)i / Mathf.Max(1, events.Count - 1);
                mark.rectTransform.Place(new Vector2(at, 0.5f), Vector2.zero, new Vector2(2, LineHeight + 8));
            }
            Image glow = UIKit.NewImage(progress, "Head", theme.radial, UITheme.WithAlpha(theme.goldBright, 0.75f));
            head = glow.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(22, 22));
            foreach (Graphic graphic in progress.GetComponentsInChildren<Graphic>())
                graphic.raycastTarget = false;
        }

        // Gone at the end (the end screen over the board)
        private void ShowProgress()
        {
            if (progress == null)
            {
                RectTransform divider = battle.Divider;
                if (divider == null)
                    return;
                BuildProgress(divider);
            }
            progressGroup.alpha = battle.IsBattleOver ? 0 : 1;
            int count = Mathf.Max(1, replay.Events.Count - 1);
            float done = Mathf.Clamp01((replay.At + (replay.Begun ? 0.5f : 0f)) / count);
            fill.anchorMax = new Vector2(done, 1);
            head.anchorMin = head.anchorMax = new Vector2(done, 0.5f);
        }

        private void OnDestroy()
        {
            if (progress != null)
                Destroy(progress.gameObject);
        }

        // Where the pass button is, as wide as the weather slot above it allows; a size larger on a phone (as the corner
        // buttons), if it fits
        private void PlaceBar(RectTransform rect, RectTransform root)
        {
            Vector2 at = Vector2.zero;
            float room = width;
            if (battle.PassBtn != null)
            {
                RectTransform pass = (RectTransform)battle.PassBtn.transform;
                at = root.InverseTransformPoint(pass.TransformPoint(pass.rect.center));
            }
            if (battle.WeatherField != null)
            {
                RectTransform weather = (RectTransform)battle.WeatherField.transform;
                Vector3 left = root.InverseTransformPoint(weather.TransformPoint(new Vector3(weather.rect.xMin, 0, 0)));
                Vector3 right = root.InverseTransformPoint(weather.TransformPoint(new Vector3(weather.rect.xMax, 0, 0)));
                room = Mathf.Abs(right.x - left.x) + 40;
            }
            float scale = Mathf.Min(UIKit.PhoneScale, room / width);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = at; // where the pass button's middle is
            rect.localScale = new Vector3(scale, scale, 1);
        }

        private Button AddButton(RectTransform parent, string name, Sprite icon, System.Action on_click, float x, System.Func<string> tooltip)
        {
            Button button = UIKit.RoundButton(parent, name, null, icon, Deselecting(on_click), ButtonSize, true);
            PlaceButton(button, x);
            button.gameObject.AddComponent<UITooltip>().text = tooltip;
            return button;
        }

        private static void PlaceButton(Button button, float x)
        {
            RectTransform rect = (RectTransform)button.transform;
            rect.Place(new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(ButtonSize, ButtonSize));
            UIKit.GrowTouchArea(rect, new Vector2(ButtonSize + Gap, 76));
        }

        // A click that leaves no button selected: the keys are the bar's own (Space would press the button again)
        private static System.Action Deselecting(System.Action on_click)
        {
            return () =>
            {
                on_click();
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            };
        }

        private static string KeyName(string key)
        {
            return GameInput.IsTouch ? "" : " (" + key + ")";
        }

        private bool Playing { get { return !replay.Paused || replay.Stepping; } }

        private void Update()
        {
            if (replay == null)
                return;
            UITheme theme = UITheme.Current;
            Sprite icon = Playing ? theme.iconPause : theme.iconPlay;
            if (playIcon.sprite != icon)
                playIcon.sprite = icon;
            string speed = replay.Speed + "\u00D7";
            if (speedText.text != speed)
                speedText.text = speed;
            ShowProgress();
            Keys();
        }

        private void Keys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || UIOverlay.HasModal || battle == null || battle.IsBattleOver)
                return;
            bool shift = keyboard.shiftKey.isPressed;
            if (keyboard.spaceKey.wasPressedThisFrame)
                replay.TogglePause();
            else if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                if (shift)
                    replay.NextRound();
                else
                    replay.NextMove();
            }
            else if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                if (shift)
                    replay.PreviousRound();
                else
                    replay.PreviousMove();
            }
            else if (keyboard.sKey.wasPressedThisFrame)
                replay.SwitchSide();
            else if (keyboard.upArrowKey.wasPressedThisFrame)
                replay.SetSpeed(replay.SpeedIndex + 1);
            else if (keyboard.downArrowKey.wasPressedThisFrame)
                replay.SetSpeed(replay.SpeedIndex - 1);
        }
    }
}
