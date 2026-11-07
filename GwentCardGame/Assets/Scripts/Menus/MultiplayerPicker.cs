using System;
using Gwent.Core;
using Gwent.Net;
using Gwent.UI;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // Multiplayer: how to play someone. On the local network (LanLobby), but not in a browser, which cannot host or find
    // games on a network; or online, through a server (not there yet). One a row, as an opponent of the picker
    // (OpponentEntry.uxml): an icon in the gold ring, the way's name and what it needs, and on the right why it cannot be
    // chosen, or that a LAN battle waits to be rejoined. One that cannot be chosen shows muted and takes no focus. The lobby
    // opens over the picker: leaving it comes back here.
    // A UI Toolkit dialog: Assets/UI/Toolkit/MultiplayerPicker.uxml.
    public static class MultiplayerPicker
    {
        // lobby: the LAN lobby opens over it at once (back from the deck builder the lobby sent the player to)
        public static void Open(Action on_close, bool lobby = false)
        {
            UIScreens.ShowDialog("MultiplayerPicker", UITheme.Current.multiplayerPicker, root => Bind(root, lobby), on_close);
        }

        private static void Bind(VisualElement root, bool lobby)
        {
            VisualElement entries = root.Q("Entries");
            bool web = GameSettings.IsWeb;
            root.Q<Label>("Title").text = Loc.T(Loc.UI, "mp.title");
            MenuEntry lan = Entry(entries, "Local network", Loc.T(Loc.UI, "lan.title"), "network",
                Loc.T(Loc.UI, web ? "mp.lan.web" : "mp.lan.detail"),
                web ? Loc.T(Loc.UI, "mp.lan.not_web") : LanSave.Load() != null ? Loc.T(Loc.UI, "mp.lan.rejoin") : null, !web);
            Entry(entries, "Online", Loc.T(Loc.UI, "mp.online"), "online", Loc.T(Loc.UI, "mp.online.detail"), Loc.T(Loc.UI, "mp.online.soon"), false);
            Button close = root.Q<Button>("Button Close");
            close.clicked += () => UIScreens.Close(root);
            if (!web)
                lan.clicked += () => OpenLobby(lan);
            root.schedule.Execute(() =>
            {
                UIScreens.Focus(web ? (VisualElement)close : lan);
                if (lobby && !web)
                    OpenLobby(lan);
            });
        }

        private static void OpenLobby(MenuEntry lan)
        {
            LanLobby.Open(() => UIScreens.Focus(lan));
        }

        // A way to play someone: its name (id: in English, it names the entry) and the title shown, its icon (a .mode__icon--
        // class) and detail; tag: why it cannot be chosen, or what waits there (null: none)
        private static MenuEntry Entry(VisualElement entries, string id, string title, string icon, string detail, string tag, bool open)
        {
            MenuEntry entry = UITheme.Current.opponentEntry.Instantiate().Q<MenuEntry>();
            entry.name = "Mode " + id;
            entry.Q<Label>("Title").text = title;
            entry.Q<Label>("Detail").text = detail;
            entry.Q("Art").RemoveFromHierarchy();
            entry.Q("Question").RemoveFromHierarchy();
            entry.Q("Chosen").RemoveFromHierarchy();
            VisualElement mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("mode__icon");
            mark.AddToClassList("mode__icon--" + icon);
            entry.Q("Portrait").Add(mark);
            if (tag != null)
            {
                Label label = new Label(tag) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("mode__tag");
                label.EnableInClassList("mode__tag--on", open); // a battle to rejoin, not a reason
                entry.Add(label);
            }
            entry.EnableInClassList("mode--off", !open);
            entry.SetEnabled(open);
            entries.Add(entry);
            return entry;
        }
    }
}
