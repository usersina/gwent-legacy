using System;
using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine.UIElements;

namespace Gwent.Story
{
    // The story's pages (Assets/UI/Toolkit/StoryPage.uxml): the journal (the Gwent quests and their steps, as far as they
    // have shown), the collection (The Witcher 3's A Miraculous Guide to Gwent: what is still to find, region by region,
    // and the cards owned), travel (the regions: those open, and what opens the others), and the story's start from the
    // main menu (begin, continue, start over).
    public static class StoryPages
    {
        // Every copy of every card: the collection's whole
        public static int AllCards { get { return CardLibrary.Get().All.Sum(card => card.copies); } }

        //----------------------------------------The journal----------------------------------------//
        public static void Journal(StoryProgress story, Action on_close)
        {
            Open("story.journal", root =>
            {
                CardDatabase db = CardLibrary.Get();
                ScrollView entries = root.Q<ScrollView>("Entries");
                IEnumerable<StoryQuest> quests = story.Content.Quests.Where(quest => quest.Steps.Any(step => story.IsVisible(story.Content.Place(step))))
                    .OrderBy(quest => story.QuestDone(quest) ? 1 : 0);
                foreach (StoryQuest quest in quests)
                {
                    bool done = story.QuestDone(quest);
                    VisualElement entry = Entry(entries, quest.Name, done);
                    bool hidden = false;
                    foreach (string step in quest.Steps)
                    {
                        StoryPlace place = story.Content.Place(step);
                        if (!story.IsVisible(place) && !story.IsDone(place))
                        {
                            hidden = true; // the steps not shown yet: once the ones before lead there
                            continue;
                        }
                        bool step_done = story.IsDone(place);
                        Line(entry, (step_done ? "✓ " : "• ") + Step(place), step_done);
                    }
                    if (hidden)
                        Line(entry, "• " + Loc.T(Loc.UI, "story.more_to_come"), false);
                    if (quest.Reward.Count > 0)
                        Line(entry, Loc.T(Loc.UI, done ? "story.reward_done" : "story.reward", string.Join(", ", quest.Reward.Select(id => db.Get(id).DisplayName))), done);
                }
                VisualElement collect = Entry(entries, Loc.T(Loc.UI, "story.collect_all"), story.CollectionComplete);
                Line(collect, (story.CollectionComplete ? "✓ " : "• ") + Loc.T(Loc.UI, "story.every_card_count", story.State.Cards.Count, AllCards), story.CollectionComplete);
            }, on_close);
        }

        // The place's name stays as the story gives it (story.json is not translated), so it comes after a colon, whole:
        // no sentence bends around it, whatever the language
        private static string Step(StoryPlace place)
        {
            switch (place.Type)
            {
                case StoryPlaceKind.Found: return Loc.T(Loc.UI, "story.step_found", place.Name);
                case StoryPlaceKind.Event: return Loc.T(Loc.UI, "story.step_event", place.Name);
                case StoryPlaceKind.Tournament: return Loc.T(Loc.UI, "story.step_tournament", place.Name);
                default: return Loc.T(Loc.UI, "story.step_beat", place.Name);
            }
        }

        //----------------------------------------The collection----------------------------------------//
        public static void Collection(StoryProgress story, Action on_close)
        {
            Open("story.collection", root =>
            {
                CardDatabase db = CardLibrary.Get();
                ScrollView entries = root.Q<ScrollView>("Entries");
                Bar(entries, Loc.T(Loc.UI, "story.every_card"), story.State.Cards.Count, AllCards, true);
                Heading(entries, Loc.T(Loc.UI, "story.where"));
                foreach (StoryRegion region in story.Content.Regions)
                {
                    int left = story.StillIn(region).Count, total = Gives(story, region);
                    Bar(entries, story.IsOpen(region) ? region.Name : Loc.T(Loc.UI, "story.not_reached", region.Name), total - left, total, false);
                }
                int pool = story.Content.File.Pool.Count;
                Bar(entries, Loc.T(Loc.UI, "story.random_cards"), pool - story.PoolLeft, pool, false);
                Heading(entries, Loc.T(Loc.UI, "story.by_faction"));
                foreach (Faction faction in new[] { Faction.NorthernRealms, Faction.Nilfgaard, Faction.Scoiatael, Faction.Monsters, Faction.Skellige, Faction.Neutral, Faction.Special })
                {
                    List<Card> cards = db.All.Where(card => card.faction == faction && card.copies > 0).ToList();
                    int have = cards.Sum(card => story.Owned(card._id)), total = cards.Sum(card => card.copies);
                    Bar(entries, faction == Faction.Special ? Loc.T(Loc.UI, "story.special_cards") : faction == Faction.Neutral ? Loc.T(Loc.UI, "story.neutral") : faction.DisplayName(), have, total, false);
                }
            }, on_close);
        }

        // How many cards a region gives in all (its shops, prizes, finds, quests' rewards): what a new story has left there
        private static int Gives(StoryProgress story, StoryRegion region)
        {
            StoryProgress fresh = new StoryProgress(story.Content, StoryProgress.NewState(story.Content, 0), CardLibrary.Get());
            return fresh.StillIn(region).Count;
        }

        // A row: its name, a gold bar of have out of total, the numbers
        private static void Bar(VisualElement entries, string name, int have, int total, bool whole)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("story-bar");
            row.EnableInClassList("story-bar--whole", whole);
            row.EnableInClassList("story-bar--done", total > 0 && have >= total);
            Label label = new Label(name);
            label.AddToClassList("story-bar__name");
            row.Add(label);
            VisualElement track = new VisualElement();
            track.AddToClassList("story-bar__track");
            VisualElement fill = new VisualElement();
            fill.AddToClassList("story-bar__fill");
            fill.style.width = Length.Percent(total > 0 ? 100f * have / total : 0);
            track.Add(fill);
            row.Add(track);
            Label count = new Label(have + " / " + total);
            count.AddToClassList("story-bar__count");
            row.Add(count);
            entries.Add(row);
        }

        private static void Heading(VisualElement entries, string text)
        {
            Label heading = new Label(text.ToUpperInvariant());
            heading.AddToClassList("story-page__heading");
            entries.Add(heading);
        }

        //----------------------------------------Travel----------------------------------------//
        public static void Travel(StoryProgress story, Action<StoryRegion> travel, Action on_close)
        {
            Open("story.travel", root =>
            {
                ScrollView entries = root.Q<ScrollView>("Entries");
                MenuEntry focus = null;
                foreach (StoryRegion region in story.Content.Regions)
                {
                    bool open = story.IsOpen(region), here = region == story.Current;
                    MenuEntry entry = UITheme.Current.opponentEntry.Instantiate().Q<MenuEntry>();
                    entry.name = "Region " + region.Id;
                    entry.Q<Label>("Title").text = region.Name;
                    int left = story.StillIn(region).Count;
                    entry.Q<Label>("Detail").text = !open ? Opens(story, region.Opens)
                        : (here ? Loc.T(Loc.UI, "story.here") + " · " : "") + (left > 0 ? Loc.T(Loc.UI, "story.to_find", left) : Loc.T(Loc.UI, "story.all_found"));
                    entry.Q("Chosen").style.visibility = here ? Visibility.Visible : Visibility.Hidden;
                    entry.Q("Art").RemoveFromHierarchy();
                    entry.Q<Label>("Question").text = open ? "→" : "?";
                    entry.SetEnabled(open);
                    if (open)
                        entry.clicked += () =>
                        {
                            UIScreens.Close(root);
                            travel(region);
                        };
                    entries.Add(entry);
                    if (here)
                        focus = entry;
                }
                if (focus != null)
                    root.schedule.Execute(() => UIScreens.Focus(focus));
            }, on_close);
        }

        // "Opens once beaten: the White Orchard scholar": what a region's or a place's condition waits for
        private static string Opens(StoryProgress story, string condition)
        {
            string kind, id;
            if (!StoryContent.Condition(condition, out kind, out id))
                return Loc.T(Loc.UI, "story.opens_start");
            if (kind == "quest")
                return Loc.T(Loc.UI, "story.opens_quest", story.Content.Quest(id).Name);
            return Loc.T(Loc.UI, "story.opens_place", story.Content.Place(id).Name);
        }

        //----------------------------------------The story's start----------------------------------------//
        // From the main menu: a new story (what it is), the one saved (Continue, Start over), or a save that can't be
        // read (Start over sets it aside, nothing is deleted)
        public static void Begin(Action on_close)
        {
            StoryProgress story = StoryGame.Load();
            string problem = StoryGame.Problem;
            Open("story.title", root =>
            {
                VisualElement buttons = root.Q("Buttons");
                ScrollView entries = root.Q<ScrollView>("Entries");
                if (story == null && problem == null)
                {
                    Caption(root, Loc.T(Loc.UI, "story.begin_caption"));
                    VisualElement entry = Entry(entries, "White Orchard", false);
                    Line(entry, Loc.T(Loc.UI, "story.begin_deck"), false);
                    Button(buttons, "Begin", Loc.T(Loc.UI, "story.btn_begin"), true, () => Start(true));
                    return;
                }
                if (story == null)
                {
                    Caption(root, Loc.T(Loc.UI, "story.unreadable_caption", problem));
                    Button(buttons, "Start over", Loc.T(Loc.UI, "story.btn_start_over"), true, () => Start(true));
                    return;
                }
                VisualElement saved = Entry(entries, story.Current.Name, false);
                Line(saved, Loc.T(Loc.UI, "story.saved_summary", story.State.Cards.Count, AllCards, story.State.Crowns,
                    story.Content.Quests.Count(story.QuestDone), story.Content.Quests.Count), false);
                Button(buttons, "Start over", Loc.T(Loc.UI, "story.btn_start_over"), false, () => UIOverlay.Confirm(Loc.T(Loc.UI, "story.confirm_new"),
                    Loc.T(Loc.UI, "story.btn_start_over"), Loc.T(Loc.UI, "common.cancel"), () => Start(true)));
                Button(buttons, "Continue", Loc.T(Loc.UI, "story.btn_continue"), true, () => Start(false));
            }, on_close);
        }

        private static void Start(bool anew)
        {
            if (anew)
                StoryGame.Begin();
            else
                StoryGame.Enter();
            LoadingScreen.Load(SceneNames.Story);
        }

        //----------------------------------------Pieces----------------------------------------//
        private static void Open(string title_key, Action<VisualElement> fill, Action on_close)
        {
            UIScreens.ShowDialog("StoryPage", UITheme.Current.storyPage, root =>
            {
                root.Q<Label>("Title").text = Loc.T(Loc.UI, title_key);
                root.Q<Label>("Caption").style.display = DisplayStyle.None; // a page says only what it must
                root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);
                fill(root);
                VisualElement first = root.Q("Buttons").Query<Button>().Last() ?? (VisualElement)root.Q<Button>("Button Close");
                root.schedule.Execute(() =>
                {
                    if (UIScreens.Focused() == null || UIScreens.Focused().panel != root.panel)
                        UIScreens.Focus(first);
                });
            }, on_close);
        }

        private static void Caption(VisualElement root, string text)
        {
            root.Q<Label>("Caption").text = text;
            root.Q<Label>("Caption").style.display = DisplayStyle.Flex;
        }

        private static VisualElement Entry(VisualElement entries, string name, bool done)
        {
            VisualElement entry = new VisualElement();
            entry.AddToClassList("story-entry");
            entry.EnableInClassList("story-entry--done", done);
            Label title = new Label(name);
            title.AddToClassList("story-entry__name");
            entry.Add(title);
            entries.Add(entry);
            return entry;
        }

        private static void Line(VisualElement entry, string text, bool done)
        {
            Label line = new Label(text);
            line.AddToClassList("story-entry__line");
            line.EnableInClassList("story-entry__line--done", done);
            entry.Add(line);
        }

        // The name is the button's id (English, stable); the text is what the player reads
        private static void Button(VisualElement buttons, string id, string text, bool primary, Action on_click)
        {
            DialogButton button = new DialogButton { name = "Button " + id, text = text };
            button.AddToClassList(primary ? "button--primary" : "button--secondary");
            button.clicked += on_click;
            buttons.Add(button);
        }
    }
}
