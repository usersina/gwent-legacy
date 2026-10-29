using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.DeckBuilding
{
    public class DeckSummary : MonoBehaviour
    {
        public DeckBuilder deckBuilder;
        public CardGrid deckCollection;
        public GameObject leaderGo;
        public TMP_Dropdown deckPicker;
        [System.NonSerialized] public Deck loadedDeck;
        [System.NonSerialized] public List<Card> deckCards;
        public List<TextMeshProUGUI> deckInfo;

        [System.NonSerialized] public List<Deck> myDecks = new List<Deck>();
        public List<string> myPaths = new List<string>();

        // The list's last entry, after the decks: a new empty deck, named first (DeckBuilder.StartNewDeck)
        public const string NewDeckOption = "+ New empty deck";

        private async UniTaskVoid Start()
        {
            // Long names ("Siege Engines of Temeria (preset)") shrink to fit rather than lose their end
            foreach (TMP_Text text in new[] { deckPicker.captionText, deckPicker.itemText })
                if (text != null)
                {
                    text.enableAutoSizing = true;
                    text.fontSizeMax = text.fontSize;
                    text.fontSizeMin = 14;
                }
            // DeckBuilder loads everything in Awake; this only guards against a disabled controller
            if (!deckBuilder.IsReady)
                await UniTask.WaitUntil(() => deckBuilder.IsReady, cancellationToken: destroyCancellationToken);
            string selected_path = deckBuilder.currentDeckPath;
            InitializeDeck();
            LoadDecks(false);
            // Keep the deck that was selected last time instead of jumping to the first one
            SelectDeckPath(selected_path);
        }

        // Updates the deck UI and Leader from deckBuilder.my_deck
        public void InitializeDeck()
        {
            loadedDeck = deckBuilder.my_deck;
            deckCards = deckBuilder.GetCardsFromDeck(loadedDeck);
            UIUpdateDeckInfo();
            UIUpdateLeader();
        }

        // Load Decks in the DropDown and show the first one.
        // select_for_battle: the shown deck becomes the battle deck (if valid). False when another deck gets selected right after.
        public void LoadDecks(bool select_for_battle = true)
        {
            deckBuilder.StopNaming(); // another deck, another faction: the name being typed goes
            // Clear Dropdown
            deckPicker.ClearOptions();

            List<TMP_Dropdown.OptionData> decks_list_drop = new List<TMP_Dropdown.OptionData>();
            // The player's decks, then the faction's presets (the AI's decks: not in the story, whose decks hold the cards
            // owned)
            List<string> deck_paths_list = new List<string>(deckBuilder.DeckPathsOf(deckBuilder.my_deck.Faction));
            if (!DeckContext.InStory)
                deck_paths_list.AddRange(OpponentDecks.PresetPaths(deckBuilder.my_deck.Faction));

            myDecks.Clear();
            myPaths.Clear();

            // Add decks to options and to global deck list (unreadable files are skipped)
            int skipped = 0;
            foreach (string path in deck_paths_list)
            {
                Deck auxdeck = deckBuilder.LoadDeckFromPath(path);
                if (auxdeck == null)
                {
                    skipped++;
                    continue;
                }
                myDecks.Add(auxdeck);
                myPaths.Add(path);
                decks_list_drop.Add(new TMP_Dropdown.OptionData(OpponentDecks.IsPreset(path) ? auxdeck.Name + " (preset)" : auxdeck.Name));
            }
            decks_list_drop.Add(new TMP_Dropdown.OptionData(NewDeckOption));
            deckPicker.AddOptions(decks_list_drop);
            if (skipped > 0)
                UIOverlay.Toast(skipped + " deck file(s) could not be read and were skipped");

            // Reload the first deck just in case
            if (myDecks.Count > 0)
                deckCollection.OnDeckChange(myDecks[0], myPaths[0], select_for_battle);
            else
            {
                // Clear the collection (my_deck is empty at this point: Cleared from UpdateSelectedFaction)
                deckCollection.OnDeckChange(deckBuilder.my_deck, DeckBuilder.EmptyDeckResourcePath, select_for_battle);
            }
            InitializeDeck();
        }


        // Selects a deck of the dropdown by file path (after adding/renaming/deleting)
        public void SelectDeckPath(string path)
        {
            int index = myPaths.FindIndex(p => DeckStorage.SamePath(p, path));
            if (index < 0)
                return;
            deckPicker.SetValueWithoutNotify(index);
            deckCollection.OnDeckChange(myDecks[index], myPaths[index]);
            InitializeDeck();
        }

        public void OnOptionChanged(int index)
        {
            if (index == myDecks.Count)
            {
                // A new empty deck: its name first, the list back on the deck shown meanwhile
                int shown = myPaths.FindIndex(p => DeckStorage.SamePath(p, deckBuilder.currentDeckPath));
                deckPicker.SetValueWithoutNotify(Mathf.Max(0, shown));
                deckBuilder.StartNewDeck();
                return;
            }
            if (index < 0 || index >= myDecks.Count || index >= myPaths.Count)
                return;

            GameAudio.PlaySfx("deck_select");
            deckCollection.OnDeckChange(myDecks[index], myPaths[index]);
            InitializeDeck();
        }

        // Sets the leader stats and ui
        public void UIUpdateLeader()
        {
            Card leader_card = deckBuilder.GetCard(loadedDeck.Leader);
            CardRef stats = leaderGo.GetComponent<CardRef>();
            stats.Card = leader_card;

            CardArt.Show(leaderGo.GetComponent<Image>(), loadedDeck.Leader);
            leaderGo.transform.Find("Name").GetComponent<TextMeshProUGUI>().text = leader_card.name;
        }

        // Updates the middle info numbers
        public void UIUpdateDeckInfo()
        {
            int helperNumber;

            // Total number of cards
            deckInfo[0].text = loadedDeck.Cards.Count.ToString();

            // Unit Cards number (Minimum: 22)
            helperNumber = deckBuilder.GetUnitCards(deckCards, null, null, false).Count;
            if (helperNumber < DeckRules.MinUnits)
            {
                deckInfo[1].color = UITheme.Current.danger;
                deckInfo[1].text = helperNumber.ToString() + "/" + DeckRules.MinUnits;
            }
            else
            {
                deckInfo[1].color = UITheme.Current.gold;
                deckInfo[1].text = helperNumber.ToString();
            }

            // Special Cards number (Maximum: 10)
            helperNumber = deckBuilder.GetSpecialCards(deckCards).Count;
            deckInfo[2].text = helperNumber.ToString() + "/" + DeckRules.MaxSpecials;

            // Total Strengths
            deckInfo[3].text = deckBuilder.GetUnitsStrength(deckBuilder.GetUnitCards(deckCards, null, null, false)).ToString();

            // Hero Cards number
            deckInfo[4].text = deckBuilder.GetUnitCards(deckCards, null, null, true).Count.ToString();
        }

    }
}
