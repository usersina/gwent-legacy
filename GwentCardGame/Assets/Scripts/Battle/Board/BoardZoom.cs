using System.Collections.Generic;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // Two fingers zoom the battle's board in and move it about, as a photo is (up to MaxZoom); zoomed in, one finger drags
    // it too (cards are tapped, never dragged, so a drag is free: a finger that moves is never a tap), and a double tap
    // on the table (not on a card, a row or a button) zooms in there, or back out. Zoomed in, a magnifier under the menu
    // button (or Back, Escape) brings the whole board back, and a padlock under it locks the board: taps on it do nothing,
    // to look around safely; it unlocks as the board zooms out. The board's pieces (the table, the rows, the hands, the
    // decks, the weather, the side panels) move together in a layer of the battle's canvas; the menu button, the pickers
    // and the end screen stay where they are, as do the card previews (on the canvas, after it) and the overlays. A pinch
    // or a drag is no tap: a cover over the board takes its fingers' release (nothing is selected, played or pressed) and
    // no card shows (GameInput.MovingBoard).
    public class BoardZoom : MonoBehaviour
    {
        public const float MaxZoom = 2.5f;
        private const float DoubleTapZoom = 2f;
        private const float GlideSpeed = 12f;          // a glide (back to the whole board, a double tap's zoom) closes this share a second
        private const float ButtonSize = 60;           // as the menu button
        private const float ButtonGap = 80;            // under it, centre to centre
        private const float DragMm = 3f;               // a finger that moves this far drags (and taps no more)
        private const double TapSeconds = 0.35;        // a tap: down and up within this
        private const double DoubleTapSeconds = 0.4;   // the second tap within this of the first
        private const float DoubleTapMm = 8f;          // and this close to it
        private static readonly HashSet<string> Fixed = new HashSet<string> { "PauseButton", "Panel", "TurnPicker", "PostGame" };

        private static BoardZoom current;

        private RectTransform layer;
        private RectTransform canvasRect;
        private Image cover;
        private Image lockCover;
        private Button button;
        private Button lockButton;
        private Image lockIcon;
        private CanvasGroup buttonFade;
        private CanvasGroup lockFade;
        private float size = 1;                        // the buttons' scale
        private float zoom = 1;
        private Vector2 pan;
        private bool locked;

        private bool gliding;
        private float glideZoom = 1;
        private Vector2 glidePan;

        private bool pinching;
        private int fingerA = -1, fingerB = -1;
        private float startDistance, startZoom;
        private Vector2 startPoint;                    // the board's point under the fingers' middle as the pinch began
        private bool covering;                         // fingers of a pinch or a drag are still down
        private int coverUntil = -1;                   // the cover stays a frame or two after they lift

        private int finger = -1;                       // one finger down alone: perhaps a drag
        private Vector2 fingerFrom;
        private Vector2 fingerPan;
        private bool dragging;
        private double lastEnded;                      // the start of the last touch seen ended (a tap's are looked at once)
        private double noTapsBefore;                   // touches begun before this were part of a pinch or a drag: no taps
        private readonly List<TouchControl> ended = new List<TouchControl>();
        private double lastTap = double.NegativeInfinity;
        private Vector2 lastTapAt;

        // The board is zoomed in (the zoom-out button shows)
        public static bool Zoomed { get { return current != null && current.zoom > 1.01f; } }

        public static float Zoom { get { return current != null ? current.zoom : 1; } }

        // The board is locked: taps on it do nothing
        public static bool Locked { get { return current != null && current.locked; } }

        // The zoom-out and lock buttons (for the tools)
        public static Button ZoomOutButton { get { return current != null ? current.button : null; } }
        public static Button LockButton { get { return current != null ? current.lockButton : null; } }

        // Where the board is moved to, in the canvas (for the tools)
        public static Vector2 Pan { get { return current != null ? current.pan : Vector2.zero; } }

        // Back to the whole board, gliding
        public static void ZoomOut()
        {
            if (current != null && current.zoom > 1)
                current.GlideTo(1, Vector2.zero);
        }

        // The board's pieces of the battle's canvas into the zoomed layer (in their order), and the zoom-out and lock
        // buttons under the menu button
        public static void Attach(Canvas canvas, RectTransform menu_button)
        {
            if (canvas == null || current != null && current.canvasRect == canvas.transform)
                return;
            RectTransform root = (RectTransform)canvas.transform;
            List<Transform> pieces = new List<Transform>();
            foreach (Transform child in root)
                if (!Fixed.Contains(child.name))
                    pieces.Add(child);
            RectTransform layer = UIKit.Rect("BoardZoom", root).Stretch();
            layer.SetAsFirstSibling();
            foreach (Transform piece in pieces)
                piece.SetParent(layer, false);

            BoardZoom zoom = canvas.gameObject.AddComponent<BoardZoom>();
            zoom.layer = layer;
            zoom.canvasRect = root;
            zoom.lastEnded = InputState.currentTime; // the touches before the battle are no taps of its
            current = zoom;

            // Over the board while it's locked: taps land on it
            zoom.lockCover = UIKit.NewImage(layer, "LockCover", null, Color.clear, true);
            zoom.lockCover.rectTransform.Stretch();
            zoom.lockCover.enabled = false;

            // Over the board (and its fixed pieces) while a pinch's or a drag's fingers are down: their release lands on it
            zoom.cover = UIKit.NewImage(root, "PinchCover", null, Color.clear, true);
            zoom.cover.rectTransform.Stretch();
            zoom.cover.enabled = false;

            UITheme theme = UITheme.Current;
            zoom.size = menu_button != null ? menu_button.localScale.x : 1; // a phone's larger, as the menu button
            float gap = ButtonGap * zoom.size;
            Vector2 at = menu_button != null ? menu_button.anchoredPosition - new Vector2(0, gap) : new Vector2(-920, 420);
            zoom.button = zoom.Corner("ZoomOut", theme.iconZoomOut, ZoomOut, at, menu_button, 1, out zoom.buttonFade);
            zoom.button.gameObject.AddComponent<UITooltip>().text = () => "Zoom out: the whole board" + (GameInput.IsTouch ? "" : " (Esc)");
            zoom.lockButton = zoom.Corner("LockBoard", theme.iconUnlock, zoom.ToggleLock, at - new Vector2(0, gap), menu_button, 2, out zoom.lockFade);
            zoom.lockIcon = zoom.lockButton.transform.Find("Icon").GetComponent<Image>();
            zoom.lockButton.gameObject.AddComponent<UITooltip>().text = () => zoom.locked
                ? "Unlock the board: taps play again"
                : "Lock the board: look and move around, taps do nothing";
            zoom.ShowButtons(false, true);
        }

        // A round button of the corner, under the menu button (and under the pickers and the end screen)
        private Button Corner(string name, Sprite icon, System.Action on_click, Vector2 at, RectTransform menu_button, int after, out CanvasGroup fade)
        {
            Button made = UIKit.RoundButton(canvasRect, name, null, icon, on_click, ButtonSize, true);
            RectTransform rect = (RectTransform)made.transform;
            rect.Place(new Vector2(0.5f, 0.5f), at, new Vector2(ButtonSize, ButtonSize));
            rect.localScale = new Vector3(size, size, 1);
            if (menu_button != null)
                rect.SetSiblingIndex(menu_button.GetSiblingIndex() + after);
            UIKit.GrowTouchArea(rect, new Vector2(110, ButtonGap)); // not over the button above's
            fade = made.gameObject.AddComponent<CanvasGroup>();
            return made;
        }

        private void OnDestroy()
        {
            if (current == this)
                current = null;
            GameInput.MovingBoard = false;
        }

        private void Update()
        {
            Touchscreen touchscreen = Touchscreen.current;
            int down = 0;
            TouchControl a = null, b = null;
            ended.Clear();
            if (touchscreen != null)
                foreach (TouchControl touch in touchscreen.touches)
                {
                    if (!touch.isInProgress)
                    {
                        // Ended since the last look (a quick tap may begin and end between two frames)
                        if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Ended && touch.startTime.ReadValue() > lastEnded)
                            ended.Add(touch);
                        continue;
                    }
                    down++;
                    int id = touch.touchId.ReadValue();
                    if (pinching ? id == fingerA : a == null)
                        a = touch;
                    else if (pinching ? id == fingerB : b == null)
                        b = touch;
                }

            if (pinching && (a == null || b == null))
                EndPinch();
            else if (pinching)
                Pinch(a, b);
            else if (a != null && b != null && MayPinch(a, b))
                BeginPinch(a, b);
            else if (down == 1 && a != null)
                OneFinger(a);
            if (down > 1 || pinching || dragging)
                noTapsBefore = InputState.currentTime; // the fingers down now tap nothing
            foreach (TouchControl touch in ended)
                Ended(touch);

            if (down == 0)
            {
                finger = -1;
                dragging = false;
            }

            // The cover takes the releases of a pinch's or a drag's fingers, all of them, and a frame or two more for the UI's
            if (covering && down == 0)
            {
                covering = false;
                coverUntil = Time.frameCount + 2;
            }
            cover.enabled = covering || Time.frameCount <= coverUntil;
            GameInput.MovingBoard = pinching || dragging;

            if (gliding && !pinching && !dragging)
            {
                float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * GlideSpeed);
                zoom = Mathf.Lerp(zoom, glideZoom, t);
                pan = Vector2.Lerp(pan, glidePan, t);
                if (Mathf.Abs(zoom - glideZoom) < 0.002f && (pan - glidePan).sqrMagnitude < 0.25f)
                {
                    zoom = glideZoom;
                    pan = glidePan;
                    gliding = false;
                }
                Apply();
            }
            if (zoom <= 1.01f && !gliding && locked)
                SetLocked(false); // the whole board again: unlocked
            bool shown = zoom > 1.01f && !(gliding && glideZoom <= 1);
            ShowButtons(shown, false);
        }

        // Only on the board itself, its fingers on the board or on nothing: not over a dialog, a picker or the redraw, nor
        // on the menu button or the zoom-out button
        private bool MayPinch(TouchControl a, TouchControl b)
        {
            return Free() && OnBoard(a.position.ReadValue()) && OnBoard(b.position.ReadValue());
        }

        private static bool Free()
        {
            BattleController battle = BattleController.Instance;
            return !(UIOverlay.HasModal || RedrawPicker.IsOpen || DiscardViewer.IsOpen || Gwent.Menus.PauseMenu.IsOpen || battle == null || battle.IsBattleOver
                || battle.Panel != null && battle.Panel.activeInHierarchy || battle.TurnPicker != null && battle.TurnPicker.activeInHierarchy);
        }

        private static readonly List<RaycastResult> hits = new List<RaycastResult>();

        // What a press there would land on first (null: nothing)
        private static GameObject Under(Vector2 screen_point)
        {
            if (EventSystem.current == null)
                return null;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen_point }, hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        private bool OnBoard(Vector2 screen_point)
        {
            GameObject hit = Under(screen_point);
            return hit == null || hit.transform.IsChildOf(layer) || hit == cover.gameObject;
        }

        // On the table itself: nothing there answers a tap (a card, a row, a pile, a button), or the board is locked
        private bool OnTable(Vector2 screen_point)
        {
            GameObject hit = Under(screen_point);
            if (hit == null || hit == lockCover.gameObject)
                return true;
            return hit.transform.IsChildOf(layer) && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) == null
                && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit) == null;
        }

        private void BeginPinch(TouchControl a, TouchControl b)
        {
            pinching = true;
            covering = true;
            gliding = false;
            dragging = false;
            finger = -1;
            fingerA = a.touchId.ReadValue();
            fingerB = b.touchId.ReadValue();
            Vector2 pa = ToCanvas(a.position.ReadValue()), pb = ToCanvas(b.position.ReadValue());
            startDistance = Mathf.Max(1, (pa - pb).magnitude);
            startZoom = zoom;
            startPoint = ((pa + pb) / 2 - pan) / zoom;
            Cover();
            GameInput.MovingBoard = true;
        }

        // The board under the fingers follows them: it grows or shrinks as they part or close, and moves with their middle
        private void Pinch(TouchControl a, TouchControl b)
        {
            Vector2 pa = ToCanvas(a.position.ReadValue()), pb = ToCanvas(b.position.ReadValue());
            zoom = Mathf.Clamp(startZoom * (pa - pb).magnitude / startDistance, 1, MaxZoom);
            pan = (pa + pb) / 2 - zoom * startPoint;
            Apply();
        }

        private void EndPinch()
        {
            pinching = false;
            fingerA = fingerB = -1;
            GameInput.MovingBoard = false;
        }

        // One finger down alone (perhaps one a pinch left): zoomed in, once it has moved a few millimetres, the board
        // follows it
        private void OneFinger(TouchControl touch)
        {
            int id = touch.touchId.ReadValue();
            Vector2 at = touch.position.ReadValue();
            if (finger != id)
            {
                finger = id;
                fingerFrom = at;
                fingerPan = pan;
                dragging = false;
            }
            bool moved = (at - fingerFrom).magnitude > DragMm * GameInput.PixelsPerMm;
            if (!dragging && moved && zoom > 1.01f && Free() && OnBoard(fingerFrom))
            {
                dragging = true;
                gliding = false;
                fingerFrom = at; // from here on: no jump
                fingerPan = pan;
                Cover();
            }
            if (dragging)
            {
                pan = fingerPan + ToCanvas(at) - ToCanvas(fingerFrom);
                Apply();
            }
        }

        // A touch ended: a tap if it was short, stayed where it began and was alone (no pinch, no drag), on the board
        private void Ended(TouchControl touch)
        {
            double start = touch.startTime.ReadValue();
            lastEnded = System.Math.Max(lastEnded, start);
            Vector2 from = touch.startPosition.ReadValue();
            bool tap = start > noTapsBefore && InputState.currentTime - start <= TapSeconds
                && (touch.position.ReadValue() - from).magnitude <= DragMm * GameInput.PixelsPerMm;
            if (tap && Free() && OnBoard(from))
                Tapped(from);
        }

        // A tap on the table: the second of two, close together, zooms in there (keeping that point under the finger) or
        // back out
        private void Tapped(Vector2 screen_point)
        {
            if (!OnTable(screen_point))
            {
                lastTap = double.NegativeInfinity;
                return;
            }
            double now = InputState.currentTime;
            if (now - lastTap > DoubleTapSeconds || (screen_point - lastTapAt).magnitude > DoubleTapMm * GameInput.PixelsPerMm)
            {
                lastTap = now;
                lastTapAt = screen_point;
                return;
            }
            lastTap = double.NegativeInfinity;
            if (zoom > 1.01f)
            {
                ZoomOut();
                return;
            }
            Vector2 p = ToCanvas(screen_point);
            Vector2 point = (p - pan) / zoom;
            GlideTo(DoubleTapZoom, p - DoubleTapZoom * point);
        }

        private void GlideTo(float to_zoom, Vector2 to_pan)
        {
            glideZoom = to_zoom;
            Vector2 limit = (to_zoom - 1) * canvasRect.rect.size / 2;
            glidePan = new Vector2(Mathf.Clamp(to_pan.x, -limit.x, limit.x), Mathf.Clamp(to_pan.y, -limit.y, limit.y));
            gliding = true;
        }

        private void Cover()
        {
            covering = true;
            cover.enabled = true;
            cover.transform.SetAsLastSibling();
        }

        private void ToggleLock()
        {
            SetLocked(!locked);
            UIOverlay.Toast(locked ? "Board locked: taps do nothing" : "Board unlocked", 1.5f);
        }

        private void SetLocked(bool on)
        {
            locked = on;
            lockCover.enabled = on;
            if (on)
                lockCover.transform.SetAsLastSibling(); // over all the board's pieces
            UITheme theme = UITheme.Current;
            lockIcon.sprite = on ? theme.iconLock : theme.iconUnlock;
        }

        // The layer at its zoom, moved no further than its edges: the board always covers the screen
        private void Apply()
        {
            Vector2 limit = (zoom - 1) * canvasRect.rect.size / 2;
            pan = new Vector2(Mathf.Clamp(pan.x, -limit.x, limit.x), Mathf.Clamp(pan.y, -limit.y, limit.y));
            layer.localScale = new Vector3(zoom, zoom, 1);
            layer.anchoredPosition = pan;
        }

        private Vector2 ToCanvas(Vector2 screen_point)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen_point, null, out local);
            return local;
        }

        private void ShowButtons(bool shown, bool at_once)
        {
            Fade(buttonFade, shown, at_once);
            Fade(lockFade, shown, at_once);
        }

        private static void Fade(CanvasGroup fade, bool shown, bool at_once)
        {
            float target = shown ? 1 : 0;
            fade.alpha = at_once ? target : Mathf.MoveTowards(fade.alpha, target, Time.unscaledDeltaTime * 6);
            fade.interactable = fade.blocksRaycasts = shown;
        }
    }
}
