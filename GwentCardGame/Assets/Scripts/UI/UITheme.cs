using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.UI
{
    // Fonts, colors and sprites of the uGUI overlays and the battle's generated art, the cursors, and the UI Toolkit
    // documents and panel settings (the menus' and dialogs' look itself is Assets/UI/Toolkit/Gwent.uss).
    // The asset lives in Resources/UITheme.asset; Gwent > UI > Regenerate Sprites and Theme rebuilds its sprites and fonts.
    [CreateAssetMenu(menuName = "Gwent/UI Theme", fileName = "UITheme")]
    public class UITheme : ScriptableObject
    {
        [Header("Fonts")]
        public TMP_FontAsset headingFont;
        public TMP_FontAsset bodyFont;
        public TMP_FontAsset bodyMediumFont;
        public Material headingShadow; // the heading font with a dark outline and a soft shadow under the letters

        [Header("Colors")]
        public Color background = new Color32(0x16, 0x12, 0x0E, 0xFF);
        public Color panel = new Color32(0x20, 0x1A, 0x15, 0xF5);
        public Color panelRaised = new Color32(0x2A, 0x22, 0x19, 0xFF);
        public Color line = new Color32(0x4D, 0x3F, 0x2C, 0xFF);
        public Color lineSoft = new Color32(0x3A, 0x2F, 0x22, 0xFF);
        public Color gold = new Color32(0xC9, 0xA4, 0x5C, 0xFF);
        public Color goldBright = new Color32(0xE3, 0xC2, 0x7E, 0xFF);
        public Color text = new Color32(0xEC, 0xE1, 0xC9, 0xFF);
        public Color textBright = new Color32(0xF6, 0xEC, 0xD4, 0xFF);
        public Color textSecondary = new Color32(0xB9, 0xAD, 0x95, 0xFF);
        public Color textMuted = new Color32(0x8F, 0x82, 0x6C, 0xFF);
        public Color danger = new Color32(0xC9, 0x5E, 0x4E, 0xFF);

        [Header("Sprites")]
        public Sprite panelSprite;
        public Sprite panelOutline;
        public Sprite pill;
        public Sprite circle;
        public Sprite ring;
        public Sprite diamond;
        public Sprite gradientH;
        public Sprite gradientV;
        public Sprite radial;
        public Sprite rule;
        public Sprite chevronLeft;
        public Sprite chevronRight;
        public Sprite iconSpeaker;   // sounding (the LAN talk window's mute)
        public Sprite iconMuted;     // silenced
        public Sprite iconCheck;     // keep (the deck builder's pencil while naming a deck)
        public Sprite iconClose;     // leave, nothing changed (its trash while naming)
        public Sprite iconZoomOut;   // the battle's board back whole, after a pinch zoomed it in
        public Sprite iconTalk;      // the quick chat's mark on your portrait (a LAN battle)
        public Sprite iconLock;      // the zoomed board's lock, shut: taps on the board do nothing
        public Sprite iconUnlock;    // the same, open
        public Sprite iconPlay;     // a replay's play
        public Sprite iconPause;    // and pause
        public Sprite iconStepBack; // a move back
        public Sprite iconStepForward; // a move forward
        public Sprite iconRoundBack; // a round back
        public Sprite iconRoundForward; // a round forward
        public Sprite iconSwitch;   // a replay seen from the other side
        public Sprite iconCards;    // the story's map: a Gwent player; the collection
        public Sprite iconCoin;     // crowns; a shop
        public Sprite iconJournal;  // the story's quests
        public Sprite iconTravel;   // another region
        public Sprite iconChest;    // a card to find
        public Sprite iconQuest;    // a card or deck given
        public Sprite iconGoblet;   // a tournament
        public Sprite iconDeck;     // the story's decks
        public Sprite iconAgain;    // the end screen: play again, rematch, watch again
        public Sprite iconFilm;     // save replay; the replays
        public Sprite iconHome;     // the main menu
        public Sprite iconMap;      // the story's map
        public Sprite softShadow; // soft blurred rounded rectangle (card shadows, glows)
        public Sprite glowRing;   // a glow around a rectangle, clear inside (nine-sliced, reaching 32 px out)
        public Sprite leaderReady; // the leader ability's crown, glowing: the leader is lifted and the crown uses it

        [Header("Board pieces (shown on the rules page)")]
        public Sprite[] rowEmblems;   // close combat, ranged, siege
        public Sprite gem;            // a life
        public Sprite scoreDisc;      // a row's score
        public Sprite scoreTotal;     // a side's total

        [Header("Battle")]
        public Sprite table;          // the field's wooden table (its loading screen shows it, dimmed)

        [Header("UI Toolkit screens (Assets/UI/Toolkit)")]
        public PanelSettings panelSettings; // the screens' and dialogs' panels are copies of it (UIScreens)
        public VisualTreeAsset mainMenu;
        public VisualTreeAsset opponentPicker;
        public VisualTreeAsset opponentEntry;
        public VisualTreeAsset multiplayerPicker;
        public VisualTreeAsset lanLobby;
        public VisualTreeAsset settings;
        public VisualTreeAsset pauseMenu;
        public VisualTreeAsset confirmDialog;
        public VisualTreeAsset replaysPage;       // the replays saved: watch, delete
        public VisualTreeAsset updateDialog;      // a newer build is out: what's new, Update
        public VisualTreeAsset toast;
        public VisualTreeAsset rulesPage;
        public VisualTreeAsset storyMap;          // the story's map (StoryScreen)
        public VisualTreeAsset storySheet;        // a place of the map: who, what they give and sell, Play
        public VisualTreeAsset storyPage;         // the journal, the collection, travel, the story's start

        [Header("Cursors (tip at 5, 4)")]
        public Texture2D cursorDefault;
        public Texture2D cursorInteract;
        public Texture2D cursorInvalid;
        public Texture2D cursorGiveUp; // a hand laid on the deck (hotspot at the palm: 30, 40)

        private static UITheme current;

        public static UITheme Current
        {
            get
            {
                if (current == null)
                {
                    current = Resources.Load<UITheme>("UITheme");
                    if (current == null)
                        current = CreateInstance<UITheme>(); // default colors, Unity's default sprites and fonts
                }
                return current;
            }
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // Darker (factor < 1) or lighter (> 1) version of a color, same alpha
        public static Color Shade(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }
    }
}
