using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

namespace Gwent.Menus
{
    // The main menu: Witcher-style entries on the left of the key art, the version and build in the bottom right corner
    // (BuildInfo: two LAN players need the same). The entries are a UI Toolkit screen (Assets/UI/Toolkit/MainMenu.uxml);
    // the key art and its "Legacy" title are the scene's.
    public class MainMenu : MonoBehaviour
    {
        private readonly List<MenuEntry> items = new List<MenuEntry>();
        private Image keyArt;

        public IList<MenuEntry> Items { get { return items; } }

        private void Start()
        {
            Gwent.Net.LanSession.ForgetBattle(); // a LAN battle that closed is no battle to load any more
            GameAudio.PlayMenuMusic();
            UIScreens.Show("MainMenu", UITheme.Current.mainMenu, Bind);
            UIOverlay.SetEscapeAction(AskQuit);
            CoverWithKeyArt();
            // The leaders' faces, for the opponent picker, the LAN lobby and How to play: loading while the menu is on,
            // they are there when those open (released with the menu)
            CardArt.Preload(Gwent.Rules.LeaderIds.ByFaction.Values.SelectMany(leaders => leaders), "menu leaders", destroyCancellationToken).Forget();
            if (LanLobby.Reopen()) // back from the deck builder the lobby sent the player to
                OpenMultiplayer(true);
            else if (!GameSettings.IsWeb && Gwent.Net.LanSession.Current == null && Gwent.Net.LanSave.Load() != null)
                UIOverlay.Toast("A LAN battle is still on: Multiplayer, then Local network, to rejoin it", 4f);
        }

        private void Bind(VisualElement root)
        {
            items.Clear();
            root.Q<Label>("Build").text = "Version " + Application.version + " \u00B7 Build " + BuildInfo.Build;
            AddItem(root, "Item Play", PlayGame);
            AddItem(root, "Item Multiplayer", () => OpenMultiplayer());
            AddItem(root, "Item Deck builder", EditDeck);
            AddItem(root, "Item How to play", OpenRules);
            AddItem(root, "Item Settings", OpenSettings);
            MenuEntry quit = root.Q<MenuEntry>("Item Quit");
            if (GameSettings.IsWeb) // a browser tab is closed, not quit
                quit.RemoveFromHierarchy();
            else
                AddItem(root, "Item Quit", QuitGame);
            // Once the panel has laid it out
            root.schedule.Execute(() => Focus(0));
        }

        private void AddItem(VisualElement root, string name, System.Action on_click)
        {
            MenuEntry item = root.Q<MenuEntry>(name);
            item.clicked += on_click;
            items.Add(item);
        }

        private void Focus(int index)
        {
            if (index >= 0 && index < items.Count)
                UIScreens.Focus(items[index]);
        }

        // The entry of that name, focused (a dialog closed gives it the keys back)
        private void Focus(string name)
        {
            Focus(items.FindIndex(item => item.name == name));
        }

        private void CoverWithKeyArt()
        {
            // The key art covers the screen whatever its shape (its edges are cropped rather than bars shown), "Legacy" under
            // its logo
            Transform background = transform.parent != null ? transform.parent.Find("Background") : null;
            Image art = background != null ? background.GetComponent<Image>() : null;
            if (art != null && art.sprite != null)
            {
                // Its censored version when the setting is on (before the first frame: the other never shows), and the
                // other one at once when the setting changes
                keyArt = art;
                ShowKeyArt();
                CensoredArt.Changed += ShowKeyArt;
                art.preserveAspect = false;
                art.rectTransform.Cover(art.sprite.rect.width / art.sprite.rect.height);
                LegacyTitle.Create(art);
            }
        }

        private void ShowKeyArt()
        {
            Sprite sprite = CensoredArt.KeyArt();
            if (keyArt != null && sprite != null)
                keyArt.sprite = sprite;
        }

        private void OnDestroy()
        {
            CensoredArt.Changed -= ShowKeyArt;
        }

        // Play: choose the opponent first
        public void PlayGame()
        {
            OpponentPicker.Open(() => Focus(0));
        }

        // Multiplayer: on the local network or online (MultiplayerPicker), in a browser too (where it says neither can be
        // had there yet); lobby: the LAN lobby over it at once
        public void OpenMultiplayer(bool lobby = false)
        {
            MultiplayerPicker.Open(() => Focus("Item Multiplayer"), lobby);
        }

        public void EditDeck()
        {
            LoadingScreen.Load(SceneNames.Deck);
        }

        public void OpenRules()
        {
            RulesPage.Open(() => Focus("Item How to play"));
        }

        public void OpenSettings()
        {
            SettingsPanel.Open(() => Focus("Item Settings"));
        }

        // Escape (Android's Back) with nothing over the menu: the last step back is leaving the game, asked first (a second
        // press answers Cancel). Not in a browser: its tab is closed, not quit
        private void AskQuit()
        {
            if (!GameSettings.IsWeb)
                UIOverlay.Confirm("Quit the game?", "Quit", "Cancel", QuitGame);
        }

        public void QuitGame()
        {
            GameLog.Event(LogCategory.Ui, "quit");
            Application.Quit();
        }
    }
}
