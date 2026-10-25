using System;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // Before a battle: who to play against. Every AI deck (its leader's portrait, the deck's name, faction and leader)
    // and a random opponent, two per row. Choosing one remembers it and starts the battle.
    // A UI Toolkit dialog: Assets/UI/Toolkit/OpponentPicker.uxml, an OpponentEntry.uxml per opponent.
    public static class OpponentPicker
    {
        public static void Open(Action on_close)
        {
            UIScreens.ShowDialog("OpponentPicker", UITheme.Current.opponentPicker, Bind, on_close);
        }

        private static void Bind(VisualElement root)
        {
            CardDatabase db = CardLibrary.Get();
            string chosen = OpponentDecks.Chosen;
            VisualElement entries = root.Q("Entries");
            MenuEntry focus = Entry(entries, -1, "Random opponent", "A different deck every battle", "", chosen == "");
            foreach (TextAsset file in OpponentDecks.All())
            {
                Deck deck = CardLibrary.ParseDeck(file.text, file.name);
                if (deck == null)
                    continue;
                // The faction and the leader on a line each: on one, a long leader's name was cut short
                string leader = db.Contains(deck.Leader) ? db.Get(deck.Leader).DisplayName : "";
                MenuEntry entry = Entry(entries, deck.Leader, deck.Name, deck.Faction.DisplayName() + "\n" + leader, file.name, file.name == chosen);
                if (file.name == chosen)
                    focus = entry;
            }
            root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);
            // The keys move the focus through the list: it scrolls to the focused opponent
            ScrollView scroll = root.Q<ScrollView>("Entries");
            scroll.RegisterCallback<FocusInEvent>(e =>
            {
                if (e.target is VisualElement focused && focused != scroll)
                    scroll.ScrollTo(focused);
            });
            root.schedule.Execute(() => UIScreens.Focus(focus));
        }

        // An opponent: its leader's portrait (none: the random one), name and detail; deck is the AI deck's file name
        private static MenuEntry Entry(VisualElement entries, int leader, string name, string detail, string deck, bool current)
        {
            MenuEntry entry = UITheme.Current.opponentEntry.Instantiate().Q<MenuEntry>();
            entry.name = "Opponent " + name;
            entry.Q<Label>("Title").text = name;
            entry.Q<Label>("Detail").text = detail;
            entry.Q("Chosen").style.visibility = current ? Visibility.Visible : Visibility.Hidden;
            VisualElement art = entry.Q("Art");
            if (leader >= 0)
            {
                CardArt.ShowPortrait(art, leader);
                entry.Q("Question").RemoveFromHierarchy();
            }
            else
                art.RemoveFromHierarchy();
            entry.clicked += () =>
            {
                OpponentDecks.Chosen = deck;
                LoadingScreen.Load(SceneNames.Game);
            };
            entries.Add(entry);
            return entry;
        }
    }
}
