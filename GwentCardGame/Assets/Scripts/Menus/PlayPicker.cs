using System;
using Gwent.Core;
using Gwent.Story;
using Gwent.UI;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // Play: what to play, as card games ask it. The story (where it is and how many cards are yours, or how it begins),
    // a single match against the AI (the opponent picker), or multiplayer (MultiplayerPicker). The Multiplayer picker's
    // document and its entries (an icon in the gold ring, a name, a line), under its own title. A mode's own dialog takes
    // its place (it hides meanwhile, not stacked behind) and comes back to it when closed.
    public static class PlayPicker
    {
        public static void Open(Action on_close)
        {
            UIScreens.ShowDialog("PlayPicker", UITheme.Current.multiplayerPicker, Bind, on_close);
        }

        private static void Bind(VisualElement root)
        {
            root.Q<Label>("Title").text = Loc.T(Loc.UI, "menu.play");
            VisualElement entries = root.Q("Entries");
            MenuEntry story = Entry(entries, "Story", Loc.T(Loc.UI, "play.story"), "story", StoryLine());
            MenuEntry match = Entry(entries, "Single match", Loc.T(Loc.UI, "play.match"), "match", Loc.T(Loc.UI, "play.match.detail"));
            MenuEntry multiplayer = Entry(entries, "Multiplayer", Loc.T(Loc.UI, "mp.title"), "multiplayer", Loc.T(Loc.UI, "play.multiplayer.detail"));
            story.clicked += () => Into(root, story, back => StoryPages.Begin(back));
            match.clicked += () => Into(root, match, back => OpponentPicker.Open(back));
            multiplayer.clicked += () => Into(root, multiplayer, back => MultiplayerPicker.Open(back));
            root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);
            root.schedule.Execute(() => UIScreens.Focus(story));
        }

        // A mode's dialog in the picker's place; back on it once that closes
        private static void Into(VisualElement root, MenuEntry entry, Action<Action> open)
        {
            root.style.display = DisplayStyle.None;
            open(() =>
            {
                if (root.panel == null)
                    return; // gone with the menu (a battle loads)
                root.style.display = DisplayStyle.Flex;
                UIScreens.Focus(entry);
            });
        }

        // Where the story is: "Velen & Novigrad · 35 of 254 cards", or how it begins
        private static string StoryLine()
        {
            Gwent.Rules.StoryProgress story = StoryGame.Load();
            if (story == null)
                return Loc.T(Loc.UI, StoryGame.Problem != null ? "play.story.unreadable" : "play.story.begin");
            return Loc.T(Loc.UI, "play.story.progress", story.Current.Name, story.State.Cards.Count, StoryPages.AllCards);
        }

        private static MenuEntry Entry(VisualElement entries, string id, string title, string icon, string detail)
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
            entries.Add(entry);
            return entry;
        }
    }
}
