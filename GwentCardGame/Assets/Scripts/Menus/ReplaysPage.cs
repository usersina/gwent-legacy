using System;
using System.Collections.Generic;
using System.Globalization;
using Gwent.Core;
using Gwent.Replays;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // The replays saved (main menu > Replays; a battle's end screen saves one), the newest first, two a row as the
    // opponents are: the opponent's leader, who played whom, how it ended and when, both factions. A click watches one
    // (ReplayPlayer); one that this build plays differently says so instead. Beside each, a cross removes it (asked
    // first). A UI Toolkit dialog:
    // Assets/UI/Toolkit/ReplaysPage.uxml, an OpponentEntry.uxml per replay.
    public static class ReplaysPage
    {
        public static void Open(Action on_close)
        {
            UIScreens.ShowDialog("ReplaysPage", UITheme.Current.replaysPage, Bind, on_close);
        }

        private static void Bind(VisualElement root)
        {
            root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);
            root.Q<Label>("Caption").text = "The battles you saved: Save replay, on a battle's end screen, keeps one here.";
            // The keys move the focus through the list: it scrolls to the focused replay
            ScrollView list = root.Q<ScrollView>("Entries");
            list.RegisterCallback<FocusInEvent>(e =>
            {
                if (e.target is VisualElement focused && focused != list)
                    list.ScrollTo(focused);
            });
            Fill(root);
        }

        // The list as it is now, its first replay focused
        private static void Fill(VisualElement root)
        {
            ScrollView list = root.Q<ScrollView>("Entries");
            list.Clear();
            List<ReplayLibrary.Entry> replays = ReplayLibrary.All();
            Label empty = root.Q<Label>("Empty");
            empty.text = "No replays yet: Save replay, on a battle's end screen, keeps the battle here to watch again.";
            empty.style.display = replays.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            list.style.display = replays.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            VisualElement focus = null;
            foreach (ReplayLibrary.Entry replay in replays)
            {
                VisualElement row = Row(root, replay, out MenuEntry entry);
                list.Add(row);
                if (focus == null)
                    focus = entry;
            }
            Button close = root.Q<Button>("Button Close");
            root.schedule.Execute(() => UIScreens.Focus(focus ?? close));
        }

        // A replay: its entry (a click watches it) and its cross
        private static VisualElement Row(VisualElement root, ReplayLibrary.Entry replay, out MenuEntry entry)
        {
            MatchRecord record = replay.Record;
            VisualElement row = new VisualElement { name = "Replay " + System.IO.Path.GetFileNameWithoutExtension(replay.File) };
            row.AddToClassList("replay");

            entry = UITheme.Current.opponentEntry.Instantiate().Q<MenuEntry>();
            entry.Q<Label>("Title").text = Title(record);
            entry.Q<Label>("Detail").text = Detail(record);
            entry.Q("Chosen").style.display = DisplayStyle.None;
            entry.Q("Question").RemoveFromHierarchy();
            CardArt.ShowPortrait(entry.Q("Art"), record.TheirDeck.Leader);
            entry.clicked += () => Watch(replay);
            row.Add(entry);

            Button remove = ActionButton(row, UITheme.Current.iconClose);
            remove.name = "Delete";
            ElementTooltip.Attach(remove, () => "Delete");
            remove.clicked += () => UIOverlay.Confirm("Delete this replay?", "Delete", "Keep it", () =>
            {
                ReplayLibrary.Delete(replay);
                Fill(root);
            });
            return row;
        }

        // A small gold icon button beside the entry
        private static Button ActionButton(VisualElement row, Sprite icon)
        {
            Button button = new Button();
            button.AddToClassList("replay__action");
            button.style.backgroundImage = new StyleBackground(icon);
            button.clicked += () => GameAudio.PlaySfx("ui_click");
            row.Add(button);
            return button;
        }

        private static void Watch(ReplayLibrary.Entry replay)
        {
            string problem = ReplayPlayer.Watch(replay);
            if (problem != null)
                UIOverlay.Toast(problem, 5f);
        }

        // "You vs The Monster Horde" (against the AI); "Sina vs Bob" (on LAN)
        public static string Title(MatchRecord record)
        {
            if (record.Kind == RecordKind.Ai)
            {
                string deck = record.TheirDeck.Name;
                return "You vs " + (string.IsNullOrEmpty(deck) ? record.TheirDeck.Faction.DisplayName() : deck);
            }
            return record.MyName + " vs " + record.TheirName;
        }

        // "Won 2-1, 27 Oct, 14:05", then "Northern Realms vs Monsters"
        public static string Detail(MatchRecord record)
        {
            string outcome;
            if (record.GaveUp)
                outcome = record.GaveUpSeat == record.Seat ? "Gave up" : record.Kind == RecordKind.Ai ? "Opponent gave up" : record.TheirName + " left";
            else
                outcome = record.Outcome == RoundWinner.Player ? "Won" : record.Outcome == RoundWinner.Enemy ? "Lost" : "Draw";
            string when = record.Saved.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
            return outcome + " " + record.MyRounds + "\u2013" + record.TheirRounds + " \u00B7 " + when + "\n"
                + record.MyDeck.Faction.DisplayName() + " vs " + record.TheirDeck.Faction.DisplayName();
        }
    }
}
