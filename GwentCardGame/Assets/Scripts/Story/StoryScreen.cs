using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

namespace Gwent.Story
{
    // The story's map (the Story scene; Assets/UI/Toolkit/StoryMap.uxml): The Witcher 3's map of the region the story is in
    // (MapView, its image loaded with the scene and let go with it), a marker on each place that has appeared (gold while
    // there is something to win there, its quest's mark while a quest leads there, a tick once beaten). A marker opens its
    // place (StorySheet). Along the top: the region and what is left to find there, the crowns, the journal (J), the
    // collection (C), the story's deck (D: the deck builder, DeckContext), travel (M) as icons; the × (and Escape) back to
    // the main menu.
    // Back from a battle, the map opens on its place and tells who appeared.
    public class StoryScreen : MonoBehaviour
    {
        private StoryProgress story;
        private StoryRegion region;
        private VisualElement root;
        private MapView map;
        private Label crowns, hint;
        private AsyncOperationHandle<Sprite> image;
        private readonly Dictionary<string, Button> markers = new Dictionary<string, Button>();

        private void Start()
        {
            story = StoryGame.Load();
            if (story == null)
            {
                LoadingScreen.Load(SceneNames.Menu); // no story to show (the main menu begins one)
                return;
            }
            StoryGame.Enter();
            GameAudio.PlayMenuMusic();
            StoryBattle last = StoryBattle.Forget();
            UIScreens.Show("Story", UITheme.Current.storyMap, Bind);
            UIOverlay.SetEscapeAction(ToMainMenu);
            if (last != null && last.Reward != null)
                Announce(last.Reward);
        }

        private void OnDestroy()
        {
            if (image.IsValid())
                Addressables.Release(image);
        }

        private void Bind(VisualElement document)
        {
            root = document;
            crowns = root.Q<Label>("Crowns");
            hint = root.Q<Label>("Hint");
            ElementTooltip.Attach(root.Q("Purse"), () => "Crowns: wins pay them; innkeepers and merchants sell cards for them");
            UITheme theme = UITheme.Current;
            VisualElement tools = root.Q("Tools");
            Tool(tools, "Journal", theme.iconJournal, "J", OpenJournal, "The Gwent quests");
            Tool(tools, "Collection", theme.iconCards, "C", OpenCollection, "Your collection: what is still to find");
            Tool(tools, "Deck", theme.iconDeck, "D", OpenDecks, "Your story decks");
            Tool(tools, "Travel", theme.iconTravel, "M", OpenTravel, "Travel");
            Button("Button Close", ToMainMenu, "Back to the main menu" + (GameInput.IsTouch ? "" : " (Esc)"));
            map = new MapView();
            root.Q("Map").Add(map);
            Show(story.Current);
        }

        private void Button(string name, System.Action on_click, string tooltip)
        {
            Button button = root.Q<Button>(name);
            button.clicked += on_click;
            ElementTooltip.Attach(button, () => tooltip);
        }

        // An icon in a ring, its key under it on a computer
        private static void Tool(VisualElement tools, string name, Sprite icon, string key, System.Action on_click, string tooltip)
        {
            Button tool = new Button { name = "Button " + name, text = "" };
            tool.AddToClassList("story__tool");
            VisualElement mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("story__tool-icon");
            mark.style.backgroundImage = new StyleBackground(icon);
            tool.Add(mark);
            if (!GameInput.IsTouch)
            {
                VisualElement cap = new VisualElement { pickingMode = PickingMode.Ignore };
                cap.AddToClassList("keycap");
                cap.AddToClassList("story__tool-key");
                Label label = new Label(key) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("keycap__label");
                cap.Add(label);
                tool.Add(cap);
            }
            tool.clicked += () =>
            {
                GameAudio.PlaySfx("ui_click");
                on_click();
            };
            ElementTooltip.Attach(tool, () => tooltip + (GameInput.IsTouch ? "" : " (" + key + ")"));
            tools.Add(tool);
        }

        private void OpenJournal()
        {
            StoryPages.Journal(story, Refocus);
        }

        private void OpenCollection()
        {
            StoryPages.Collection(story, Refocus);
        }

        private void OpenTravel()
        {
            StoryPages.Travel(story, Travel, Refocus);
        }

        // The tools' keys, as The Witcher 3's: J the journal, M the map's regions; C the collection, D the deck
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || root == null || UIOverlay.HasModal || LoadingScreen.Showing)
                return;
            if (keyboard.jKey.wasPressedThisFrame)
                OpenJournal();
            else if (keyboard.cKey.wasPressedThisFrame)
                OpenCollection();
            else if (keyboard.dKey.wasPressedThisFrame)
                OpenDecks();
            else if (keyboard.mKey.wasPressedThisFrame)
                OpenTravel();
        }

        //----------------------------------------A region----------------------------------------//
        private void Show(StoryRegion shown)
        {
            region = shown;
            story.Travel(region);
            StoryGame.Save();
            root.Q<Label>("Region").text = region.Name;
            StoryMap geometry = story.Content.Map(region.Map);
            bool on_map = geometry != null;
            root.Q("Map").style.display = on_map ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("Place").style.display = on_map ? DisplayStyle.None : DisplayStyle.Flex;
            if (image.IsValid())
                Addressables.Release(image);
            if (on_map)
            {
                map.ClearMarkers();
                markers.Clear();
                image = Addressables.LoadAssetAsync<Sprite>("StoryMaps/" + region.Map);
                image.Completed += loaded =>
                {
                    if (map == null || region != shown || loaded.Status != AsyncOperationStatus.Succeeded)
                        return;
                    map.Show(loaded.Result);
                    PlaceMarkers(geometry);
                };
            }
            else
                ShowPlaces();
            Refresh();
        }

        // The markers of the places that have appeared (those too near each other MapView sets apart)
        private void PlaceMarkers(StoryMap geometry)
        {
            map.ClearMarkers();
            markers.Clear();
            StoryPlace focus = null;
            foreach (StoryPlace place in story.Content.PlacesIn(region).Where(p => p.Placed && story.IsVisible(p)))
            {
                (float x, float y) = geometry.Position(place.Lat, place.Lng);
                Button marker = Marker(place);
                markers[place.Id] = marker;
                map.AddMarker(marker, new Vector2(x, y));
                if (place.Id == StoryBattle.Back)
                    focus = place; // back from its battle
                else if (focus == null && story.HasMore(place))
                    focus = place;
            }
            Refresh();
            if (focus != null)
            {
                (float fx, float fy) = geometry.Position(focus.Lat, focus.Lng);
                map.Focus(new Vector2(fx, fy), 0.35f);
                UIScreens.Focus(markers[focus.Id]);
            }
        }

        private Button Marker(StoryPlace place)
        {
            Button marker = new Button { name = "Marker " + place.Id, text = "" };
            marker.AddToClassList("story-marker");
            VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("story-marker__icon");
            icon.style.backgroundImage = new StyleBackground(Icon(place));
            marker.Add(icon);
            VisualElement badge = new VisualElement { name = "Badge", pickingMode = PickingMode.Ignore };
            badge.AddToClassList("story-marker__badge");
            VisualElement mark = new VisualElement { name = "Mark", pickingMode = PickingMode.Ignore };
            mark.AddToClassList("story-marker__badge-icon");
            badge.Add(mark);
            marker.Add(badge);
            marker.clicked += () =>
            {
                if (map.Dragged)
                    return; // the press moved the map
                GameAudio.PlaySfx("ui_click");
                StorySheet.Open(story, place, Changed, () => Refocus(place));
            };
            ElementTooltip.Attach(marker, () => "<b>" + place.Name + "</b>" + (StorySheet.Trade(place).Length > 0 ? "\n" + StorySheet.Trade(place) : "")
                + "\n" + StorySheet.State(story, place));
            return marker;
        }

        public static Sprite Icon(StoryPlace place)
        {
            UITheme theme = UITheme.Current;
            switch (place.Type)
            {
                case StoryPlaceKind.Shop: return theme.iconCoin;
                case StoryPlaceKind.Found: return theme.iconChest;
                case StoryPlaceKind.Event: return theme.iconQuest;
                case StoryPlaceKind.Tournament: return theme.iconGoblet;
                default: return theme.iconCards;
            }
        }

        // A region without a map: its places on a panel (as entries of the opponent picker), over the key art
        private void ShowPlaces()
        {
            Sprite art = CensoredArt.KeyArt();
            if (art != null)
                root.Q("Backdrop").style.backgroundImage = new StyleBackground(art);
            root.Q<Label>("PlaceTitle").text = region.Name;
            VisualElement entries = root.Q("PlaceEntries");
            entries.Clear();
            MenuEntry first = null;
            foreach (StoryPlace place in story.Content.PlacesIn(region).Where(story.IsVisible))
            {
                MenuEntry entry = UITheme.Current.opponentEntry.Instantiate().Q<MenuEntry>();
                entry.name = "Place " + place.Id;
                entry.Q<Label>("Title").text = place.Name;
                entry.Q<Label>("Detail").text = (StorySheet.Trade(place).Length > 0 ? StorySheet.Trade(place) + "\n" : "") + StorySheet.State(story, place);
                entry.Q("Chosen").style.visibility = Visibility.Hidden;
                entry.Q("Question").RemoveFromHierarchy();
                entry.clicked += () => StorySheet.Open(story, place, Changed, () => Refocus(place));
                entries.Add(entry); // on its panel first: art asked for by an element on none is not shown
                if (place.Plays)
                    CardArt.ShowPortrait(entry.Q("Art"), DeckGenerator.For(place, story.NextRound(place), CardLibrary.Get()).Leader);
                if (first == null)
                    first = entry;
            }
            if (first != null)
                root.schedule.Execute(() => UIScreens.Focus(first));
        }

        //----------------------------------------What changed----------------------------------------//
        // A sheet changed the story (a card bought, found, given): the markers and the crowns again, and what appeared
        private void Changed(StoryReward reward)
        {
            StoryGame.Save();
            if (reward != null)
                Announce(reward);
            if (story.Content.Map(region.Map) != null)
            {
                if (reward != null && reward.Revealed.Any(place => place.Region == region.Id))
                    PlaceMarkers(story.Content.Map(region.Map));
            }
            else
                ShowPlaces();
            Refresh();
        }

        private void Refresh()
        {
            if (crowns == null)
                return;
            crowns.text = story.State.Crowns.ToString();
            int left = story.StillIn(region).Count;
            hint.text = left > 0 ? left + " to find" : "All found";
            foreach (KeyValuePair<string, Button> pair in markers)
            {
                StoryPlace place = story.Content.Place(pair.Key);
                bool more = story.HasMore(place);
                pair.Value.EnableInClassList("story-marker--done", !more);
                bool quest = more && story.Content.QuestsOf(place).Any(quest_of => !story.QuestDone(quest_of));
                pair.Value.EnableInClassList("story-marker--quest", quest);
                pair.Value.Q("Mark").style.backgroundImage = new StyleBackground(more ? UITheme.Current.iconQuest : UITheme.Current.iconCheck);
            }
        }

        // What a win, a find or a gift led to, over the map: who appeared, which regions opened
        private void Announce(StoryReward reward)
        {
            List<string> lines = new List<string>();
            List<StoryPlace> here = reward.Revealed.Where(place => place.Region == region?.Id || region == null).ToList();
            if (here.Count > 0)
                lines.Add("On the map now: " + string.Join(", ", here.Select(place => place.Name)));
            foreach (StoryRegion opened in reward.Regions)
                lines.Add(opened.Name + " is open: Travel to go there");
            if (reward.Cards.Count > 0 && story.CollectionComplete)
                lines.Add("Card Collector: every Gwent card is yours");
            if (lines.Count > 0)
                UIOverlay.Toast(string.Join("\n", lines), 4.5f);
        }

        private void Refocus()
        {
            Refresh();
        }

        private void Refocus(StoryPlace place)
        {
            Refresh();
            Button marker;
            if (markers.TryGetValue(place.Id, out marker))
                UIScreens.Focus(marker);
        }

        private void Travel(StoryRegion to)
        {
            if (to != region)
                Show(to);
        }

        //----------------------------------------Ways out----------------------------------------//
        private static void OpenDecks()
        {
            LoadingScreen.Load(SceneNames.Deck); // the story's (DeckContext); its Back comes here
        }

        private static void ToMainMenu()
        {
            StoryBattle.Forget();
            LoadingScreen.Load(SceneNames.Menu);
        }
    }
}
