using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.UI;
using Gwent.Updates;
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
            UpdateCheck.Found += ShowUpdate;
            UpdateCheck.Begin(); // once a session: a newer build shows a notice here
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
            ShowBuild(root);
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
            update = root.Q<UnityEngine.UIElements.Button>("Button Update");
            update.clicked += () => UpdateDialog.Open(UpdateCheck.Available, () => UIScreens.Focus(update));
            ShowUpdate();
            // Once the panel has laid it out
            root.schedule.Execute(() => Focus(0));
        }

        // "Version 1.0.0 · Build 332 (1a2b3c4d)", the commit a link to it on GitHub (and its part of a local build's changes
        // after it, as is); without a number or a commit known, the build as it is
        private static void ShowBuild(VisualElement root)
        {
            Label start = root.Q<Label>("Build");
            Label commit = root.Q<Label>("Commit");
            Label end = root.Q<Label>("Build end");
            string version = "Version " + Application.version + " \u00B7 ";
            string url = BuildInfo.CommitUrl;
            if (BuildInfo.Number <= 0 || url == null)
            {
                start.text = version + BuildInfo.Label;
                commit.style.display = DisplayStyle.None;
                end.style.display = DisplayStyle.None;
                return;
            }
            start.text = version + "Build " + BuildInfo.Number + " (";
            commit.text = BuildInfo.Commit;
            end.text = BuildInfo.Build.Substring(BuildInfo.Commit.Length) + ")";
            commit.tooltip = "This build's source on GitHub";
            commit.RegisterCallback<ClickEvent>(_ =>
            {
                GameAudio.PlaySfx("ui_click");
                Application.OpenURL(url);
            });
        }

        // The notice of a newer build (UpdateCheck), in the top right corner: it opens what's new and Update
        private UnityEngine.UIElements.Button update;

        private void ShowUpdate()
        {
            UpdateFeed feed = UpdateCheck.Available;
            if (update == null)
                return;
            update.style.display = feed != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (feed != null)
                update.text = "New version available \u00B7 build " + feed.build;
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
            UpdateCheck.Found -= ShowUpdate;
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
