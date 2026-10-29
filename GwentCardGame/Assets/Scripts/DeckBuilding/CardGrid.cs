using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Gwent.DeckBuilding
{
    public class CardGrid : MonoBehaviour
    {
        // The factions of the header's panels, in order: every faction, or in the story those with a leader owned
        public static Faction[] HeaderFactions { get { return DeckContext.Factions; } }

        public DeckBuilder deckBuilder;
        public FactionHeader header;
        public Toggle toggleBtn;

        public TextMeshProUGUI subtitle;
        public GameObject CCardPrefab;
        public GameObject cardsArea;
        public GameObject cardDetails;

        public int selectedTab;
        public Faction faction = Faction.NorthernRealms;
        public TextMeshProUGUI factionPerk;

        public List<Image> currentSprites;
        public List<Sprite> idleSprites;
        public List<Sprite> selectedSprites;

        [System.NonSerialized] public List<Card> CardsToDisplay;

        public DeckSummary deckSummary;

        // The Witcher 3's filters: all cards, close, ranged, siege, heroes, weather, special cards
        private enum Listing { All, Close, Range, Siege, Heroes, Weather, Specials, None }
        public const int WeatherTab = 5;
        public const int SpecialTab = 6;

        // The tabs' names: the list's subtitle, and the tab's tooltip
        private static readonly string[] TabNames = { "All Cards", "Close Units", "Ranged Units", "Siege Units", "Heroes", "Weather", "Specials" };
        private const float TabSize = 84;

        private async UniTaskVoid Start()
        {
            AddWeatherTab();
            AddScrollbar();
            ClearCardsArea();
            // DeckBuilder loads everything in Awake; this only guards against a disabled controller
            if (!deckBuilder.IsReady)
                await UniTask.WaitUntil(() => deckBuilder.IsReady, cancellationToken: destroyCancellationToken);

            // Collection Cards or player deck
            if (transform.CompareTag("Player"))
                LoadFromDeck();
            else
                CardsToDisplay = deckBuilder.allCards;

            SetCardsArea(UnitMergerHelper(faction, null));
        }

        // Reference: Menu Buttons
        public void HandleClick(int index)
        {
            if (selectedTab != index)
            {
                Deselect(selectedTab);
                selectedTab = index;
                Select(selectedTab);
            }
        }

        public void Deselect(int index)
        {
            currentSprites[index].sprite = idleSprites[index];
        }

        public void Select(int index, bool resetScroll = true)
        {
            Listing listing;

            // UI: Text and Icon
            currentSprites[index].sprite = selectedSprites[index];
            if (index >= 0 && index < TabNames.Length)
                subtitle.text = TabNames[index];
            switch (index)
            {
                case 0:
                    listing = Listing.All;
                    break;
                case 1:
                    listing = Listing.Close;
                    break;
                case 2:
                    listing = Listing.Range;
                    break;
                case 3:
                    listing = Listing.Siege;
                    break;
                case 4:
                    listing = Listing.Heroes;
                    break;
                case WeatherTab:
                    listing = Listing.Weather;
                    break;
                case SpecialTab:
                    listing = Listing.Specials;
                    break;
                default:
                    listing = Listing.None;
                    break;
            }

            // GAME: Instantiated Cards
            ManageCardsArea(listing, resetScroll);
        }

        //------------------------------------------------------------Functions-----------------------------------------------//
        // Sets Deck faction and Cards from my_deck
        private void LoadFromDeck()
        {
            faction = deckBuilder.my_deck.Faction;
            CardsToDisplay = deckBuilder.GetCardsFromDeck(deckBuilder.my_deck);
        }

        private void ClearCardsArea()
        {
            for (int i = 0; i < cardsArea.transform.childCount; i++)
            {
                Destroy(cardsArea.transform.GetChild(i).gameObject);
            }

            // Do not remove this, otherwise count will still get the previous frame's count (NOT 0);
            cardsArea.transform.DetachChildren();
        }

        // The Witcher 3 shows each card once, with its count: the copies in the deck, or in the collection the copies
        // left to add (a card whose copies are all in the deck stays there, at 0 and greyed: Spent)
        private void SetCardsArea(List<Card> cards_list)
        {
            ClearCardsArea();
            bool deck = transform.CompareTag("Player");
            Dictionary<int, int> in_deck = new Dictionary<int, int>();
            foreach (int id in deckBuilder.my_deck.Cards)
                in_deck[id] = in_deck.TryGetValue(id, out int copies) ? copies + 1 : 1;
            HashSet<int> shown = new HashSet<int>();
            foreach (Card card in cards_list)
            {
                // A summoned card (the Bovine Defense Force, Hemdall, a bear) cannot be added to a deck
                if (CardIds.IsSummoned(card._id) || !shown.Add(card._id))
                    continue;
                in_deck.TryGetValue(card._id, out int used);
                int count = deck ? used : DeckContext.Supply.Copies(card) - used; // in the story, the copies owned
                if (deck && count <= 0)
                    continue;

                GameObject instantiatedCard = Instantiate(CCardPrefab);
                instantiatedCard.name = card._id.ToString();
                CardRef stats = instantiatedCard.GetComponent<CardRef>();
                stats.Card = card;

                CardArt.Show(instantiatedCard.GetComponent<Image>(), card._id);
                instantiatedCard.transform.SetParent(cardsArea.transform, false);
                AddCountBadge(instantiatedCard.transform, count);
                SetSpent(instantiatedCard, count <= 0);
            }
            foreach (int card_id in arriving.Keys)
                ShowArrivals(card_id); // made anew while copies are still on their way
        }

        // "x2" near the bottom of the card, as in The Witcher 3: at the right of the art, just above the name strip
        // (which it would cover on the left)
        public const string CountBadge = "Count";

        private static void AddCountBadge(Transform card, int count)
        {
            UITheme theme = UITheme.Current;
            RectTransform badge = UIKit.Rect(CountBadge, card).Place(new Vector2(1, 0), new Vector2(-8, 70), new Vector2(50, 28), new Vector2(1, 0));
            Image back = badge.gameObject.AddComponent<Image>();
            back.sprite = theme.pill;
            back.type = Image.Type.Sliced;
            back.color = new Color(0.07f, 0.05f, 0.03f, 0.88f);
            back.raycastTarget = false;
            TextMeshProUGUI text = UIKit.Text(badge, "x" + count, UIKit.TextStyle.Caption, TextAlignmentOptions.Center);
            text.rectTransform.Stretch(0, 0, 0, 1);
            text.fontSize = 21;
            text.color = theme.goldBright;
            text.raycastTarget = false;
        }

        // A card of the collection all of whose copies are in the deck: still shown, at 0, under a dark veil (its count
        // above it, muted); it cannot be added, nor taken by a drag. Not the CanvasGroup's alpha: that hides a card whose
        // copies are on their way (CardMotion.Hide).
        public const string SpentVeil = "Spent";

        private static void SetSpent(GameObject card, bool spent)
        {
            Transform veil = card.transform.Find(SpentVeil);
            if (veil == null && spent)
            {
                RectTransform rect = UIKit.Rect(SpentVeil, card.transform).Stretch();
                Image dim = rect.gameObject.AddComponent<Image>();
                dim.color = new Color(0.06f, 0.05f, 0.04f, 0.68f);
                dim.raycastTarget = false;
                Transform badge = card.transform.Find(CountBadge);
                if (badge != null)
                    rect.SetSiblingIndex(badge.GetSiblingIndex()); // under the count
                veil = rect;
            }
            if (veil != null)
                veil.gameObject.SetActive(spent);
            TextMeshProUGUI count = CountText(card);
            if (count != null)
                count.color = spent ? UITheme.Current.textMuted : UITheme.Current.goldBright;
        }

        public static bool IsSpent(GameObject card)
        {
            Transform veil = card.transform.Find(SpentVeil);
            return veil != null && veil.gameObject.activeSelf;
        }

        // Where a card dragged from the other grid counts as dropped here: anywhere in this grid's column, its panel's
        // width from top to bottom (its title and tabs, its cards, the space under them)
        public bool TakesDropAt(Vector2 screen_point)
        {
            ScrollRect scroll = cardsArea.GetComponentInParent<ScrollRect>();
            Rect panel = CardMotion.ScreenRect((RectTransform)(scroll != null ? scroll.transform : cardsArea.transform));
            return screen_point.x >= panel.xMin && screen_point.x <= panel.xMax;
        }

        // The scene has six tabs, the specials last; The Witcher 3 gives the weather cards a tab of their own, before
        // the specials. The specials' tab is copied for it, and the seven tabs share the menu's width (its row's spacing
        // was the scene's six tabs': seven ran past both sides of the list), each telling its name when hovered.
        private void AddWeatherTab()
        {
            if (currentSprites.Count > SpecialTab)
                return;
            Image special = currentSprites[WeatherTab];
            Image weather = Instantiate(special.gameObject, special.transform.parent).GetComponent<Image>();
            weather.name = "weatherBtn";
            weather.transform.SetSiblingIndex(special.transform.GetSiblingIndex());
            currentSprites.Insert(WeatherTab, weather);
            idleSprites.Insert(WeatherTab, SpriteCache.Load("DeckEdit/CardsView/Menu/idle/weather"));
            selectedSprites.Insert(WeatherTab, SpriteCache.Load("DeckEdit/CardsView/Menu/selected/weather"));
            weather.sprite = idleSprites[WeatherTab];
            OnClickSelects(weather.GetComponent<Button>(), WeatherTab);
            OnClickSelects(special.GetComponent<Button>(), SpecialTab);

            RectTransform menu = (RectTransform)special.transform.parent;
            GridLayoutGroup row = menu.GetComponent<GridLayoutGroup>(); // it sizes the tabs: its cells
            if (row != null)
            {
                row.cellSize = new Vector2(TabSize, TabSize);
                row.spacing = new Vector2((menu.rect.width - TabSize * currentSprites.Count) / (currentSprites.Count - 1), row.spacing.y);
            }
            for (int i = 0; i < currentSprites.Count; i++)
            {
                string tab_name = TabNames[i];
                currentSprites[i].gameObject.AddComponent<UITooltip>().text = () => tab_name;
            }
        }

        private void OnClickSelects(Button button, int tab)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => HandleClick(tab));
        }

        private ScrollRect Scroll { get { return cardsArea.transform.parent.GetComponent<ScrollRect>(); } }

        // A scroll bar just right of the cards (beside the view, which masks what it holds), shown while they overflow
        private const float ScrollbarGap = 8;

        private void AddScrollbar()
        {
            ScrollRect scroll = Scroll;
            RectTransform view = (RectTransform)scroll.transform;
            Scrollbar bar = UIKit.VerticalScrollbar(view.parent, "Scrollbar");
            RectTransform rect = (RectTransform)bar.transform;
            rect.SetSiblingIndex(view.GetSiblingIndex() + 1);
            rect.anchorMin = view.anchorMin;
            rect.anchorMax = view.anchorMax;
            rect.pivot = new Vector2(0, 1);
            Rect shown = view.rect;
            rect.anchoredPosition = view.anchoredPosition + new Vector2((1 - view.pivot.x) * shown.width + ScrollbarGap, (1 - view.pivot.y) * shown.height);
            rect.sizeDelta = new Vector2(UIKit.ScrollbarWidth, shown.height);
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        // The card object this grid shows for a card, or null (laid out: where it is drawn is known at once)
        public GameObject CardShown(int card_id)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)cardsArea.transform);
            Transform card = cardsArea.transform.Find(card_id.ToString());
            return card != null ? card.gameObject : null;
        }

        //----------------------------------------Cards on their way here----------------------------------------//
        // A card added to the deck (or put back in the collection) flies to its place: until each copy lands, the grid
        // shows one fewer (a card all of whose copies are still on their way is not shown yet). Counted by card, so quick
        // clicks and the rebuilds they cause keep it right.
        private readonly Dictionary<int, int> arriving = new Dictionary<int, int>();

        public void Arriving(int card_id, int copies)
        {
            int count;
            arriving.TryGetValue(card_id, out count);
            count += copies;
            if (count > 0)
                arriving[card_id] = count;
            else
                arriving.Remove(card_id);
            ShowArrivals(card_id);
        }

        // How many copies this grid counts of a card: those in the deck, or (the collection) those left to add
        private int CopiesOf(int card_id)
        {
            int used = 0;
            foreach (int id in deckBuilder.my_deck.Cards)
                if (id == card_id)
                    used++;
            return transform.CompareTag("Player") ? used : DeckContext.Supply.Copies(deckBuilder.Db.Get(card_id)) - used;
        }

        private void ShowArrivals(int card_id)
        {
            GameObject card = CardShown(card_id);
            if (card == null)
                return;
            int on_the_way;
            arriving.TryGetValue(card_id, out on_the_way);
            int shown = CopiesOf(card_id) - on_the_way;
            bool deck = transform.CompareTag("Player");
            CardMotion.Hide(card, deck && shown <= 0); // the deck shows what it holds; the collection keeps a spent card
            if (!deck)
                SetSpent(card, shown <= 0);
            TextMeshProUGUI count = CountText(card);
            if (count != null && (shown > 0 || !deck))
                count.text = "x" + Mathf.Max(0, shown);
        }

        private static TextMeshProUGUI CountText(GameObject card)
        {
            Transform badge = card.transform.Find(CountBadge);
            return badge != null ? badge.GetComponentInChildren<TextMeshProUGUI>() : null;
        }

        // Where a card arrives on screen: its own place, kept within the grid's view (a card scrolled out of sight, or
        // not shown at all, arrives at the view's edge or its middle)
        public Rect ArrivalRect(GameObject card, Vector2 size)
        {
            Rect view = CardMotion.ScreenRect((RectTransform)Scroll.transform);
            Rect place = card != null ? CardMotion.ScreenRect((RectTransform)card.transform) : CardMotion.Around(view.center, size);
            Vector2 center = new Vector2(Mathf.Clamp(place.center.x, view.xMin + place.width / 2, view.xMax - place.width / 2),
                Mathf.Clamp(place.center.y, view.yMin + place.height / 2, view.yMax - place.height / 2));
            return CardMotion.Around(center, place.size);
        }

        // Whether the whole card shows in the grid's view
        public bool InView(GameObject card)
        {
            if (card == null)
                return false;
            Rect view = CardMotion.ScreenRect((RectTransform)Scroll.transform);
            Rect place = CardMotion.ScreenRect((RectTransform)card.transform);
            return place.yMin >= view.yMin - 1 && place.yMax <= view.yMax + 1;
        }

        private void ResetScrolling()
        {
            ScrollRect scroll = Scroll;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1; // back to the top, at any resolution
        }

        private void ManageCardsArea(Listing listing, bool resetScroll = true)
        {
            if (resetScroll)
                ResetScrolling();

            switch (listing)
            {
                case Listing.Weather:
                case Listing.Specials:
                    List<Card> specials_list = deckBuilder.GetSpecialCards(CardsToDisplay, faction).FindAll(card => (card.row == Row.Weather) == (listing == Listing.Weather));
                    specials_list.Sort((a, b) => a._id.CompareTo(b._id));
                    SetCardsArea(specials_list);
                    break;
                case Listing.Heroes:
                    SetCardsArea(UnitMergerHelper(faction, null, true));
                    break;
                case Listing.All:
                    SetCardsArea(UnitMergerHelper(faction, null));
                    break;
                case Listing.Close:
                    SetCardsArea(UnitMergerHelper(faction, Row.Close));
                    break;
                case Listing.Range:
                    SetCardsArea(UnitMergerHelper(faction, Row.Range));
                    break;
                case Listing.Siege:
                    SetCardsArea(UnitMergerHelper(faction, Row.Siege));
                    break;
                default:
                    Debug.LogError("ManageCardsArea: Unexpected value: " + listing);
                    break;
            }
        }

        // The units of a faction (and the neutral ones, if shown) for a row (null: all, with the special cards after)
        private List<Card> UnitMergerHelper(Faction fc, Row? rw, bool ishero = false)
        {
            List<Card> returned_list;
            if (toggleBtn.isOn)
            {
                returned_list = deckBuilder.MergedList(
                deckBuilder.GetUnitCards(CardsToDisplay, fc, rw, ishero),
                deckBuilder.GetUnitCards(CardsToDisplay, Faction.Neutral, rw, ishero)
                );
            }
            else
            {
                returned_list = deckBuilder.MergedList(
                deckBuilder.GetUnitCards(CardsToDisplay, fc, rw, ishero)
                );
            }

            // Sort By strength, if strength is equal then sort by id
            // (To get cards with similar names besides each other in deck collection)
            returned_list.Sort((x, y) =>
            {
                var ret = x.strength.CompareTo(y.strength);
                if (ret == 0) ret = x._id.CompareTo(y._id);
                return ret;
            });

            if (rw == null && !ishero) // Add special Cards to all cards
            {
                List<Card> specials_list = deckBuilder.GetSpecialCards(CardsToDisplay, faction);
                specials_list.Sort((a, b) => a._id.CompareTo(b._id));
                returned_list = deckBuilder.MergedList(returned_list, specials_list);
            }

            return returned_list;
        }

        //----------------------------------------------------Event Handlers---------------------------------------------------//
        // Opens on a faction (the deck's, before the header places itself): its cards and its ability
        public void OpenOn(Faction deck_faction)
        {
            int panel = System.Array.IndexOf(HeaderFactions, deck_faction);
            if (panel < 0)
                return;
            faction = deck_faction;
            if (factionPerk != null)
                factionPerk.text = faction.Ability();
            if (header != null)
                header.startingPanel = panel;
        }

        // Reference: FactionHeader On Panel Changed
        public void UpdateSelectedFaction()
        {
            int panel = header.CurrentPanel;
            if (panel >= 0 && panel < HeaderFactions.Length)
            {
                faction = HeaderFactions[panel];
                factionPerk.text = faction.Ability();
                Select(selectedTab);
            }

            // Related to Deck Cards
            if (transform.CompareTag("Player"))
            {
                // Start from a new empty deck of that faction (the loaded deck object is shared with the dropdown list)
                deckBuilder.my_deck = new Deck { Faction = faction, Name = DeckRules.EmptyDeckName };

                // Change the leader to the default of each faction (in the story, the first one owned)
                deckBuilder.my_deck.Leader = DeckRules.DefaultLeader(faction, deckBuilder.Db, DeckContext.Supply);

                // Load the new decklists and get the first deck if not empty
                deckSummary.LoadDecks();
            }
        }

        // The collection counts the copies left to add: it follows the deck (where it is scrolled to stays)
        private void RefreshCollection()
        {
            foreach (CardGrid collection in FindObjectsByType<CardGrid>())
                if (collection != this && collection.CardsToDisplay != null)
                    collection.Select(collection.selectedTab, false);
        }

        // Reference: Toggle On Value Changed
        public void ToggleNeutrals()
        {
            Select(selectedTab, false);
        }

        // DECK CARDS RELATED
        // Changes my_deck and currentDeckPath in DeckBuilder
        public void OnDeckChange(Deck selectedDeck, string selectedPath, bool select_for_battle = true)
        {
            // Replace the loaded deck with the selected one
            deckBuilder.my_deck = selectedDeck;
            deckBuilder.currentDeckPath = selectedPath;
            LoadFromDeck();
            Select(selectedTab);
            RefreshCollection();

            // A valid selected deck becomes the battle deck (the status line follows the deck shown either way)
            if (select_for_battle)
                deckBuilder.UpdateBattleDeck();
            else
                deckBuilder.UpdateBattleDeckStatus();
        }

        // From Double Clicking a card
        public void OnDeckEdit()
        {
            // A preset is never changed: the edit goes to the player's own copy
            if (deckBuilder.ShowsPreset)
            {
                deckBuilder.KeepPresetEdit();
                return;
            }
            LoadFromDeck();
            Select(selectedTab, false);
            RefreshCollection();
            deckSummary.InitializeDeck();

            // Save to the current deck
            deckBuilder.WriteDeckToFile(deckBuilder.my_deck, deckBuilder.currentDeckPath);

            // Becomes (or stops being, if now invalid) the battle deck
            deckBuilder.UpdateBattleDeck();
        }

    }
}
