using Gwent.Battle;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // The battle's pause menu (Escape, the Android back button or the menu button at the top left). Time stops while it
    // is open, so the AI waits too; not in a LAN battle, which goes on on the other device (the menu is called so there,
    // and the board keeps up with the other player's moves). Leave battle gives the battle up (a loss, asked first) and
    // goes back to the main menu; a hand laid on your deck gives up too (DeckGiveUp), as in Yu-Gi-Oh, to the end screen.
    // A UI Toolkit dialog (Assets/UI/Toolkit/PauseMenu.uxml); this component sits on its object to put time back should
    // the scene close under it.
    public class PauseMenu : MonoBehaviour
    {
        private static PauseMenu current;
        private float previousTimeScale = 1f;

        public static bool IsOpen { get { return current != null; } }

        public static void Open()
        {
            if (current != null || SettingsPanel.IsOpen)
                return;

            float time_scale = Time.timeScale;
            if (!InLanBattle)
                Time.timeScale = 0f;
            // The battle reads its keys itself (BattleKeys): the menu button that opened this must not get the selection
            // back on Resume, or Enter would press it again instead of taking a card
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            GameObject dialog = UIScreens.ShowDialog("PauseMenu", UITheme.Current.pauseMenu, Bind, Resumed);
            current = dialog.AddComponent<PauseMenu>();
            current.previousTimeScale = time_scale;
        }

        public static void Close()
        {
            if (current != null)
                UIOverlay.CloseModal(current.gameObject);
        }

        private static bool InLanBattle
        {
            get { return Gwent.Net.LanSession.Current != null && Gwent.Net.LanSession.Current.State == Gwent.Net.LanSession.Stage.Battle; }
        }

        private static void Bind(VisualElement root)
        {
            if (InLanBattle)
                root.Q<Label>(className: "title").text = "Menu";
            MenuEntry resume = root.Q<MenuEntry>("Item Resume");
            resume.clicked += Close;
            MenuEntry settings = root.Q<MenuEntry>("Item Settings");
            settings.clicked += () => SettingsPanel.Open(() => UIScreens.Focus(settings));
            // While the battle is on, leaving gives it up; once it is over, back to the main menu from here too. A replay is
            // left for the replays, nothing asked (nothing is lost)
            BattleController battle = BattleController.Instance;
            bool watching = battle != null && battle.Replaying;
            bool battle_on = battle != null && !battle.IsBattleOver && !watching;
            Entry(root, "Item Leave battle", battle_on, () => UIOverlay.Confirm("Leave the battle? It counts as a loss.", "Leave", "Stay", GiveUpAndLeave));
            if (watching)
            {
                MenuEntry stop = root.Q<MenuEntry>("Item Main menu");
                stop.text = "Stop watching";
                stop.clicked += Gwent.Replays.ReplayPlayer.Leave;
            }
            else
                Entry(root, "Item Main menu", !battle_on, () => UIOverlay.Confirm("Leave and go back to the main menu?", "Leave", "Stay", LeaveBattle));
            // As the main menu's Quit: everywhere but in a browser, whose tab is closed, not quit
            Entry(root, "Item Quit game", !GameSettings.IsWeb, () => UIOverlay.Confirm("Quit the game?", "Quit", "Cancel", Application.Quit));
            root.schedule.Execute(() => UIScreens.Focus(resume));
        }

        // An entry shown only where it applies
        private static void Entry(VisualElement root, string name, bool shown, System.Action on_click)
        {
            MenuEntry entry = root.Q<MenuEntry>(name);
            if (shown)
                entry.clicked += on_click;
            else
                entry.RemoveFromHierarchy();
        }

        private static void Resumed()
        {
            // In a battle, at the game speed (it may have changed in the settings), or a replay's own pace
            float previous = current != null ? current.previousTimeScale : 1f;
            Time.timeScale = BattleController.Instance != null ? BattleController.PlayingTimeScale : previous > 0 ? previous : 1f;
            current = null;
        }

        // The battle given up (in a LAN battle, the other player wins), then left
        private static void GiveUpAndLeave()
        {
            if (BattleController.Instance != null)
                BattleController.Instance.GiveUp(true);
            LeaveBattle();
        }

        // The battle stays paused under the loading screen (leaving it sets the time going again, OnDestroy)
        private static void LeaveBattle()
        {
            Gwent.Net.LanSession.Current?.Leave();
            LoadingScreen.Load(SceneNames.Menu);
        }

        private void OnDestroy()
        {
            // Destroyed with the scene while open: never leave the next scene frozen
            if (current == this)
            {
                current = null;
                Time.timeScale = 1f;
            }
        }
    }
}
