using UnityEngine.InputSystem;

namespace Gwent.Core
{
    // The few direct input checks of the game, on the Input System package (UI clicks go through the EventSystem)
    public static class GameInput
    {
        // Touch controls (a phone): a tap does what a click or a double-click does, and a finger held on a card shows
        // what hovering shows. In the editor, Gwent > Simulate Touch Controls tries them with the mouse.
        public static bool IsTouch { get { return UnityEngine.Application.isMobilePlatform || SimulateTouch; } }

    #if UNITY_EDITOR
        // The editor's switch (the autoplay also turns it on while it checks the touch controls)
        public static bool SimulateTouch;
    #else
        private const bool SimulateTouch = false;
    #endif

        // Fingers are zooming the battle's board or moving it about (BoardZoom): their presses are no taps, nor long presses
        public static bool MovingBoard;

        // Screen pixels to a millimetre (Screen.dpi, or a phone's usual 160 dpi where the platform doesn't say)
        public static float PixelsPerMm { get { float dpi = UnityEngine.Screen.dpi; return (dpi > 0 ? dpi : 160) / 25.4f; } }

        // Right click or Escape: close popups
        public static bool CancelPressed()
        {
            return EscapePressed() || SecondaryPressed();
        }

        // Escape (the Android back button arrives as Escape): close the top dialog, otherwise open the pause menu
        public static bool EscapePressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        }

        // Right click: close popups, skip the redraw phase
        public static bool SecondaryPressed()
        {
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
        }

        // A second finger touching the screen (mobile equivalent of the right click)
        public static bool SecondFingerTapped()
        {
            Touchscreen touchscreen = Touchscreen.current;
            return touchscreen != null && touchscreen.touches.Count > 1 && touchscreen.touches[1].press.wasPressedThisFrame;
        }
    }
}
