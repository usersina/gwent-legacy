using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gwent.DeckBuilding
{
    // Deck builder: owns the card database, the deck files and the currently edited deck
    public class DeckBuilder : MonoBehaviour
    {
        public const string EmptyDeckResourcePath = "resource://Decks/__empty__";

        public static DeckBuilder Instance { get; private set; }

        public TextAsset CardsDatabase;
        public DeckSummary deckSummary;
        public CardGrid deckCollection;

        public string currentDeckPath;

        // A list of deck paths for each faction
        public List<string> NRDecks;
        public List<string> NFDecks;
        public List<string> SCDecks;
        public List<string> MDecks;
        public List<string> SKDecks;

        // Runtime state, never saved with the scene: every card (each card has unique stats), the deck being edited
        [System.NonSerialized] public List<Card> allCards;
        [System.NonSerialized] public Deck my_deck;

        public bool IsReady { get; private set; }
        public CardDatabase Db { get; private set; }

        private TextMeshProUGUI battleDeckStatus;

        // The line telling which deck is used in battle (and why the edited one isn't)
        public string BattleDeckStatus { get { return battleDeckStatus != null ? battleDeckStatus.text : ""; } }

        // Loads card data and the selected deck before any other deck builder script starts
        private void Awake()
        {
            Instance = this;
            Db = CardLibrary.Get(CardsDatabase);
            // Every card; in the story, those owned (DeckContext)
            allCards = DeckContext.InStory ? Db.All.Where(card => DeckContext.Supply.Copies(card) > 0).ToList() : Db.All;

            DeckStorage.EnsureDirectories();

            // Get Player Deck
            currentDeckPath = EmptyDeckResourcePath;
            if (DeckStorage.Exists(DeckStorage.BattleDeckPath)) // a deck file, or a preset
                currentDeckPath = DeckStorage.BattleDeckPath;
            GameLog.Event(LogCategory.Deck, "builder_open", "deck", currentDeckPath);

            my_deck = LoadDeckFromPath(currentDeckPath);
            if (my_deck == null)
            {
                currentDeckPath = EmptyDeckResourcePath;
                my_deck = LoadDeckFromPath(EmptyDeckResourcePath);
            }
            PopulateDeckAssets();
            OpenOnDeckFaction();
            IsReady = true;
        }

        // The builder opens on the faction of the deck it shows: its header panel and both card lists (they start set up
        // for the first faction, the header only places itself in its Start)
        private void OpenOnDeckFaction()
        {
            foreach (CardGrid collection in FindObjectsByType<CardGrid>())
                collection.OpenOn(my_deck.Faction);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            GameAudio.PlayDeckMusic();
            ConfigureMobileDeckBuilder();
            LayOutDeckControls(); // after the touch sizes
            AddTooltips();
            UpdateBattleDeckStatus();
            UIOverlay.SetEscapeAction(OnEscape);
        }

        // Escape (and Android's Back button) goes back to the main menu, unless something of the builder's own was open the
        // frame before: then that press closes it, one thing at a time. A card's details; the name being typed (its field
        // cancels the name itself while it has the keyboard, so only a field left open without it is closed here); the deck
        // list (it closes on the press itself while one of its entries is selected; otherwise here). The dialogs over the
        // builder, the leaders' too, take the press before this.
        private bool escapeCloses;
        private bool detailsOpen, nameTyped, listOpen;

        private void LateUpdate()
        {
            detailsOpen = deckCollection.cardDetails != null && deckCollection.cardDetails.activeInHierarchy;
            nameTyped = nameField != null && nameField.isFocused;
            listOpen = deckSummary.deckPicker.IsExpanded;
            escapeCloses = naming || listOpen || detailsOpen;
        }

        private void OnEscape()
        {
            if (!escapeCloses)
                ToMainMenu();
            else if (detailsOpen)
                deckCollection.cardDetails.SetActive(false);
            else if (naming && !nameTyped)
                StopNaming();
            else if (listOpen && deckSummary.deckPicker.IsExpanded)
                deckSummary.deckPicker.Hide();
        }

        // What the icon buttons do, when the pointer rests on them (or a finger is held on them)
        private void AddTooltips()
        {
            string escape = GameInput.IsTouch ? "" : " (Esc)";
            Tooltip("BackBtn", () => (DeckContext.InStory ? "Back to the map" : "Back to the main menu") + escape);
            Tooltip("RenameBtn", () => naming ? "Keep this name" : ShowsPreset ? "Name a copy of this preset" : "Rename this deck");
            Tooltip("DeleteBtn", () => naming ? "Leave the name as it was" : ShowsPreset ? "Presets can't be deleted" : "Delete this deck");
        }

        private static void Tooltip(string control, System.Func<string> text)
        {
            GameObject button = GameObject.Find(control);
            if (button != null)
                button.AddComponent<UITooltip>().text = text;
        }

        private void ConfigureMobileDeckBuilder()
        {
            if (!GameInput.IsTouch)
                return;

            if (EventSystem.current != null)
                EventSystem.current.pixelDragThreshold = Mathf.Max(EventSystem.current.pixelDragThreshold, 20);

            foreach (TMP_InputField input in FindObjectsByType<TMP_InputField>())
            {
                input.shouldHideMobileInput = false;
                input.shouldHideSoftKeyboard = false;
                input.onSelect.AddListener(delegate { input.ActivateInputField(); });
            }

            ExpandTouchTarget("RenameBtn", 52f, 52f);
            ExpandTouchTarget("DeleteBtn", 52f, 52f);
        }

        private void ExpandTouchTarget(string objectName, float minimumWidth, float minimumHeight)
        {
            GameObject buttonObject = GameObject.Find(objectName);
            if (buttonObject == null)
                return;

            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            rectTransform.sizeDelta = new Vector2(
                Mathf.Max(rectTransform.sizeDelta.x, minimumWidth),
                Mathf.Max(rectTransform.sizeDelta.y, minimumHeight));
        }

        //-----------------------------------------------Decks----------------------------------------------------//
        // The deck shown is a preset (an AI deck): picked as it is, never renamed, deleted or changed
        public bool ShowsPreset { get { return OpponentDecks.IsPreset(currentDeckPath); } }

        // Null when the file cannot be read; unknown card ids are dropped
        public Deck LoadDeckFromPath(string path)
        {
            Deck deck;
            if (path == EmptyDeckResourcePath)
            {
                TextAsset deckAsset = Resources.Load<TextAsset>("Decks/__empty__");
                deck = deckAsset != null ? CardLibrary.ParseDeck(deckAsset.text, path) : null;
                if (deck == null)
                    deck = new Deck { Faction = Faction.NorthernRealms, Name = DeckRules.EmptyDeckName, Leader = LeaderIds.FoltestKingOfTemeria };
            }
            else
                deck = DeckStorage.Load(path);
            return DeckRules.Sanitize(deck, Db, DeckContext.Supply);
        }

        // Rescans the deck folders (call every time a deck is added, renamed or deleted)
        public void PopulateDeckAssets()
        {
            NRDecks = DeckStorage.DeckPaths(Faction.NorthernRealms);
            NFDecks = DeckStorage.DeckPaths(Faction.Nilfgaard);
            SCDecks = DeckStorage.DeckPaths(Faction.Scoiatael);
            MDecks = DeckStorage.DeckPaths(Faction.Monsters);
            SKDecks = DeckStorage.DeckPaths(Faction.Skellige);
        }

        public List<string> DeckPathsOf(Faction faction)
        {
            switch (faction)
            {
                case Faction.Nilfgaard: return NFDecks;
                case Faction.Scoiatael: return SCDecks;
                case Faction.Monsters: return MDecks;
                case Faction.Skellige: return SKDecks;
                default: return NRDecks;
            }
        }

        public Card GetCard(int card_id)
        {
            return Db.Get(card_id);
        }

        public List<Card> GetCardsFromDeck(Deck deck)
        {
            return Db.GetCards(deck.Cards.Where(Db.Contains));
        }

        //----------------------------------Card Fetching--------------------------------------//
        // The units of a list: of a faction (null: any), that can go on a row (null: any; agile units go on close and
        // ranged), heroes only if asked
        public List<Card> GetUnitCards(List<Card> cards_list, Faction? faction = null, Row? row = null, bool getUnique = false)
        {
            return cards_list.Where(card =>
                card.IsUnit
                && (faction == null || card.faction == faction)
                && (row == null || card.row == row || (card.row == Row.Agile && (row == Row.Close || row == Row.Range)))
                && (!getUnique || card.unique)).ToList();
        }

        // The special cards of a list, those a deck of the faction can hold if one is given (Mardroeme is Skellige's)
        public List<Card> GetSpecialCards(List<Card> cards_list, Faction? faction = null)
        {
            return cards_list.Where(card => card.IsSpecial && (faction == null || DeckRules.Fits(card, faction.Value))).ToList();
        }

        public List<Card> MergedList(params List<Card>[] lists)
        {
            List<Card> returned_list = new List<Card>(lists.Sum(list => list.Count));
            foreach (List<Card> list in lists)
                returned_list.AddRange(list);
            return returned_list;
        }

        public int GetUnitsStrength(List<Card> units_list)
        {
            return units_list.Sum(card => card.strength);
        }

        public int GetSpecialsOccurence(Deck deck)
        {
            return GetSpecialCards(GetCardsFromDeck(deck)).Count;
        }

        //------------------------------------The deck's name---------------------------------------------------//
        // The deck list's row: the list, the pencil, the trash. The pencil names the deck shown: the list gives way to a
        // field holding its name, chosen whole (typing replaces it); Enter or the pencil, now a check, keeps the name;
        // Escape or the trash, now a cross, leaves things as they were (clicking elsewhere leaves the field open). Your own
        // deck is renamed; a preset stays as it is and the name goes to your own copy of it (the field offers a free
        // "(copy)" name). The list's last entry, "+ New empty deck", opens the same field for a new deck with no cards.
        private TMP_InputField nameField;
        private UnityEngine.UI.Image pencilIcon, trashIcon;
        private Sprite pencilSprite, trashSprite;
        private bool naming;
        private bool creating; // naming a new empty deck (else the deck shown)
        private int namedFrame = -1; // Enter and the keyboard's Done may both end one edit

        public bool Naming { get { return naming; } }

        // The list, the pencil and the trash on one row (the buttons at their touch size on a phone), the name field
        // over the list, hidden until the pencil is pressed
        private void LayOutDeckControls()
        {
            RectTransform list = (RectTransform)deckSummary.deckPicker.transform;
            GameObject pencil = GameObject.Find("RenameBtn"), trash = GameObject.Find("DeleteBtn");
            nameField = FindAnyObjectByType<TMP_InputField>();
            if (pencil == null || trash == null || nameField == null)
                return;
            const float gap = 6, wider = 40; // the list reaches further left, for longer names
            RectTransform pencil_rect = (RectTransform)pencil.transform, trash_rect = (RectTransform)trash.transform;
            float left = list.anchoredPosition.x - list.sizeDelta.x / 2 - wider;
            pencil_rect.anchoredPosition = new Vector2(trash_rect.anchoredPosition.x - trash_rect.sizeDelta.x / 2 - gap - pencil_rect.sizeDelta.x / 2,
                trash_rect.anchoredPosition.y);
            float right = pencil_rect.anchoredPosition.x - pencil_rect.sizeDelta.x / 2 - gap;
            list.sizeDelta = new Vector2(right - left, list.sizeDelta.y);
            list.anchoredPosition = new Vector2((left + right) / 2, list.anchoredPosition.y);
            RectTransform field = (RectTransform)nameField.transform;
            field.anchoredPosition = list.anchoredPosition;
            field.sizeDelta = list.sizeDelta;
            nameField.characterLimit = DeckRules.MaxNameLength;
            nameField.gameObject.SetActive(false);
            // The list opens on up to six entries, "+ New empty deck" among them (it shrinks to fewer)
            RectTransform template = deckSummary.deckPicker.template;
            if (template != null)
                template.sizeDelta = new Vector2(template.sizeDelta.x, 6 * list.sizeDelta.y);

            pencilIcon = pencil.GetComponent<UnityEngine.UI.Image>();
            trashIcon = trash.GetComponent<UnityEngine.UI.Image>();
            pencilSprite = pencilIcon.sprite;
            trashSprite = trashIcon.sprite;
            nameField.onSubmit.AddListener(_ => KeepName());
            nameField.onEndEdit.AddListener(_ =>
            {
                if (nameField.wasCanceled)
                    StopNaming();
                else if (GameInput.IsTouch && naming && namedFrame != Time.frameCount)
                    KeepName(); // the phone's keyboard closed: its Done
            });
        }

        // Reference: pencil button (named so in the scene): starts naming the deck, or keeps the name typed
        public void RenameDeck(TMP_InputField input)
        {
            if (naming)
                KeepName();
            else
                StartNaming();
        }

        // The list's "+ New empty deck": the name field, offering a free "New deck" name
        public void StartNewDeck()
        {
            if (naming)
                StopNaming();
            StartNaming(true);
        }

        private void StartNaming(bool create = false)
        {
            if (nameField == null || !create && (currentDeckPath == EmptyDeckResourcePath || my_deck.Name == DeckRules.EmptyDeckName))
            {
                RejectName("Choose a deck first");
                return;
            }
            naming = true;
            creating = create;
            deckSummary.deckPicker.gameObject.SetActive(false);
            nameField.gameObject.SetActive(true);
            if (nameField.placeholder is TMP_Text placeholder)
                placeholder.text = create ? "Name your new deck" : ShowsPreset ? "Name your copy" : "Deck name";
            nameField.text = create ? FreeNewDeckName() ?? "" : ShowsPreset ? FreeCopyName() ?? "" : my_deck.Name;
            pencilIcon.sprite = UITheme.Current.iconCheck != null ? UITheme.Current.iconCheck : pencilSprite;
            trashIcon.sprite = UITheme.Current.iconClose != null ? UITheme.Current.iconClose : trashSprite;
            UpdateDeckActions();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(nameField.gameObject);
            nameField.ActivateInputField();
            GameAudio.PlaySfx("ui_click");
        }

        // Keeps the name typed: the deck renamed, or the preset's copy made (an unchanged name of your own deck: nothing to
        // do); a name that cannot be had says why and stays to be changed
        private void KeepName()
        {
            if (!naming)
                return;
            namedFrame = Time.frameCount;
            if (!creating && !ShowsPreset && DeckRules.NormalizeDisplayName(nameField.text) == my_deck.Name)
            {
                StopNaming();
                return;
            }
            if (creating ? CreateDeck(nameField.text) : NameDeck(nameField.text))
                StopNaming();
            else
                nameField.ActivateInputField();
        }

        // Back to the deck list, nothing named. The field goes a frame later: this may run inside the field's own events.
        public void StopNaming()
        {
            if (!naming)
                return;
            naming = false;
            creating = false;
            deckSummary.deckPicker.gameObject.SetActive(true);
            pencilIcon.sprite = pencilSprite;
            trashIcon.sprite = trashSprite;
            UpdateDeckActions();
            HideNameField().Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid HideNameField()
        {
            await Cysharp.Threading.Tasks.UniTask.Yield(destroyCancellationToken);
            if (!naming && nameField != null)
            {
                nameField.DeactivateInputField();
                nameField.gameObject.SetActive(false);
            }
        }

        // Names the deck shown: your own deck is renamed; a preset gives its cards to your own copy under that name (the
        // preset stays as it was), the deck shown from then on. False, saying why, when the name cannot be had.
        public bool NameDeck(string typed)
        {
            if (currentDeckPath == EmptyDeckResourcePath || my_deck.Name == DeckRules.EmptyDeckName)
            {
                RejectName("Choose a deck first");
                return false;
            }
            string name = DeckRules.NormalizeDisplayName(typed);
            string file_name = DeckRules.ToFileName(name);
            if (file_name == null)
            {
                RejectName("A deck's name needs a letter or a number");
                return false;
            }
            if (System.Array.IndexOf(Factions.Playable, my_deck.Faction) < 0)
                return false; // a hand-edited faction must not choose the folder the deck goes to
            bool preset = ShowsPreset;
            if (NameTaken(name, preset ? null : currentDeckPath))
            {
                RejectName("Another deck is already called \"" + name + "\"");
                return false;
            }
            if (!preset)
                return Rename(name, file_name);
            if (!SaveCopy(name))
                return false;
            UIOverlay.Toast("Saved as your own deck, \"" + name + "\": the preset stays as it was", 4f);
            return true;
        }

        // A new deck of the faction shown, with no cards, under that name (its leader the deck shown's when it is the
        // faction's), shown from then on. False, saying why, when the name cannot be had.
        public bool CreateDeck(string typed)
        {
            string name = DeckRules.NormalizeDisplayName(typed);
            string file_name = DeckRules.ToFileName(name);
            if (file_name == null)
            {
                RejectName("A deck's name needs a letter or a number");
                return false;
            }
            if (System.Array.IndexOf(Factions.Playable, my_deck.Faction) < 0)
                return false;
            if (NameTaken(name, null))
            {
                RejectName("Another deck is already called \"" + name + "\"");
                return false;
            }
            Deck deck = new Deck
            {
                Faction = my_deck.Faction,
                Name = name,
                Leader = LeaderIds.BelongsTo(my_deck.Leader, my_deck.Faction) && DeckContext.Supply.Copies(Db.Get(my_deck.Leader)) > 0
                    ? my_deck.Leader : DeckRules.DefaultLeader(my_deck.Faction, Db, DeckContext.Supply),
            };
            string path = DeckStorage.PathFor(deck.Faction, file_name);
            if (!DeckStorage.Save(deck, path))
            {
                RejectName("Could not save the deck");
                return false;
            }
            GameLog.Event(LogCategory.Deck, "deck_added", "path", path);
            GameAudio.PlaySfx("deck_save");
            PopulateDeckAssets();
            deckSummary.LoadDecks(false);
            deckSummary.SelectDeckPath(path);
            return true;
        }

        // "New deck", else "New deck 2"... the first no deck of the faction shown has (null: too many)
        private string FreeNewDeckName()
        {
            for (int n = 1; n <= MaxPresetCopies; n++)
            {
                string name = n == 1 ? "New deck" : "New deck " + n;
                if (!NameTaken(name, null))
                    return name;
            }
            return null;
        }

        private bool Rename(string name, string file_name)
        {
            string oldPath = currentDeckPath;
            string newPath = DeckStorage.PathFor(my_deck.Faction, file_name);
            Deck renamed = new Deck { Faction = my_deck.Faction, Name = name, Leader = my_deck.Leader, Cards = new List<int>(my_deck.Cards) };

            // Write the new file first, then remove the old one: a failure never loses the deck
            if (!DeckStorage.Save(renamed, newPath))
            {
                RejectName("Could not rename the deck");
                return false;
            }
            if (!DeckStorage.SamePath(oldPath, newPath))
                DeckStorage.Delete(oldPath);
            if (DeckStorage.SamePath(DeckStorage.SavedBattleDeck, oldPath))
                SetBattleDeck(newPath);

            GameLog.Event(LogCategory.Deck, "deck_renamed", "from", oldPath, "to", newPath);
            GameAudio.PlaySfx("deck_save");
            my_deck = renamed;
            currentDeckPath = newPath;
            PopulateDeckAssets();
            deckSummary.LoadDecks(false);
            deckSummary.SelectDeckPath(newPath);
            return true;
        }

        // The pencil (the deck's name: a preset's too, for a copy) and the trash (your own deck; the cross while naming)
        private void UpdateDeckActions()
        {
            bool deck = currentDeckPath != EmptyDeckResourcePath;
            foreach ((string control, bool on) in new[] { ("RenameBtn", deck), ("DeleteBtn", naming || deck && !ShowsPreset) })
            {
                GameObject button = GameObject.Find(control);
                if (button != null && button.GetComponent<UnityEngine.UI.Button>() != null)
                    button.GetComponent<UnityEngine.UI.Button>().interactable = on;
            }
        }

        // Reference: trash button (asks for confirmation first); while naming, the cross: back, nothing named
        public void DeleteDeck()
        {
            if (naming)
            {
                StopNaming();
                return;
            }
            if (deckSummary.deckPicker.options.Count <= 0 || currentDeckPath == EmptyDeckResourcePath)
                return;
            if (ShowsPreset)
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast("Presets can't be deleted");
                return;
            }

            string filePath = currentDeckPath;
            UIOverlay.Confirm("Delete the deck \"" + my_deck.Name + "\"?\nThis cannot be undone.", "Delete", "Cancel", () => ConfirmDelete(filePath));
        }

        public void ConfirmDelete(string filePath)
        {
            if (!DeckStorage.Delete(filePath))
            {
                UIOverlay.Toast("Could not delete the deck");
                return;
            }
            GameAudio.PlaySfx("deck_remove");
            GameLog.Event(LogCategory.Deck, "deck_deleted", "path", filePath);
            if (DeckStorage.SamePath(DeckStorage.SavedBattleDeck, filePath))
                SetBattleDeck(null);

            PopulateDeckAssets();
            // To make sure deck list is cleared as well
            deckCollection.UpdateSelectedFaction();
        }

        private const int MaxPresetCopies = 99;

        // A preset is never changed: its first edit (already made to the deck shown) is saved as the player's own copy,
        // "The Monster Horde (copy)", which is the deck shown and edited from then on
        public void KeepPresetEdit()
        {
            if (!ShowsPreset)
                return;
            string name = FreeCopyName();
            if (name == null)
            {
                RejectName("Too many copies of this preset: delete some first");
                return;
            }
            if (SaveCopy(name))
                UIOverlay.Toast("Presets stay as they are: your changes are saved in your own deck, \"" + name + "\"", 4f);
        }

        // The first "(copy)" name of the deck shown that no deck of its faction has (null: too many)
        private string FreeCopyName()
        {
            for (int n = 1; n <= MaxPresetCopies; n++)
            {
                string name = DeckRules.CopyName(my_deck.Name, n);
                if (!NameTaken(name, null))
                    return name;
            }
            return null;
        }

        // The deck shown, saved as the player's own under that name, and shown from then on (a preset's copy)
        private bool SaveCopy(string name)
        {
            Deck copy = new Deck { Faction = my_deck.Faction, Name = name, Leader = my_deck.Leader, Cards = new List<int>(my_deck.Cards) };
            string path = DeckStorage.PathFor(copy.Faction, DeckRules.ToFileName(name));
            if (!DeckStorage.Save(copy, path))
            {
                RejectName("Could not save the deck");
                return false;
            }
            GameLog.Event(LogCategory.Deck, "preset_copied", "path", path);
            GameAudio.PlaySfx("deck_save");
            my_deck = copy;
            currentDeckPath = path;
            PopulateDeckAssets();
            deckSummary.LoadDecks(false);
            deckSummary.SelectDeckPath(path);
            return true;
        }

        // Same display name (ignoring case/spaces) or same file name as another deck of the faction
        private bool NameTaken(string name, string ignored_path)
        {
            string file_name = DeckRules.ToFileName(name);
            foreach (string path in DeckPathsOf(my_deck.Faction))
            {
                if (ignored_path != null && DeckStorage.SamePath(path, ignored_path))
                    continue;
                if (Path.GetFileNameWithoutExtension(path) == file_name)
                    return true;
                Deck other = DeckStorage.Load(path);
                if (other != null && DeckRules.NamesCollide(name, other.Name))
                    return true;
            }
            return false;
        }

        private static void RejectName(string message)
        {
            GameAudio.PlaySfx("invalid");
            UIOverlay.Toast(message);
        }

        //----------------------Battle deck (the deck used when pressing Play)---------------------//
        // Called whenever the edited deck changes: a valid deck becomes the battle deck, an invalid one stops being it
        public void UpdateBattleDeck()
        {
            bool real_deck = currentDeckPath != EmptyDeckResourcePath && my_deck.Name != DeckRules.EmptyDeckName;
            if (real_deck && IsDeckValid(my_deck))
                SetBattleDeck(currentDeckPath);
            else if (real_deck && DeckStorage.SamePath(DeckStorage.SavedBattleDeck, currentDeckPath))
                SetBattleDeck(null);
            UpdateBattleDeckStatus();
        }

        private void SetBattleDeck(string path)
        {
            string current = DeckStorage.BattleDeckPath;
            if (string.IsNullOrEmpty(path) ? current == null : DeckStorage.SamePath(current, path))
                return; // already it: no settings write (a whole file on Android, the browser's storage on the web) per edit
            DeckStorage.SaveBattleDeck(path);
            GameLog.Event(LogCategory.Deck, "battle_deck_chosen", "path", path ?? "default");
        }

        // Line at the bottom of the screen telling which deck will be used in battle
        public void UpdateBattleDeckStatus()
        {
            if (battleDeckStatus == null)
            {
                // Fits between the two "Show neutrals" toggles; long messages shrink instead of overlapping them
                battleDeckStatus = UIOverlay.CreateText(UIOverlay.FixedRoot, "", 26, new Vector2(0.5f, 0f), new Vector2(0, 42), new Vector2(560, 72));
                battleDeckStatus.textWrappingMode = TextWrappingModes.Normal;
                battleDeckStatus.enableAutoSizing = true;
                battleDeckStatus.fontSizeMin = 14;
                battleDeckStatus.fontSizeMax = 26;
            }

            string battle_path = DeckStorage.BattleDeckPath;
            Deck battle_deck = DeckStorage.Exists(battle_path) ? DeckStorage.Load(battle_path) : null;
            string battle_name = battle_deck == null ? (DeckContext.InStory ? "the starter deck" : "default (Northern Realms)")
                : battle_deck.Name + (OpponentDecks.IsPreset(battle_path) ? " (preset)" : "");

            bool editing_real_deck = currentDeckPath != EmptyDeckResourcePath && my_deck.Name != DeckRules.EmptyDeckName;
            if (editing_real_deck && !IsDeckValid(my_deck))
            {
                string problem = DeckRules.Validate(my_deck, Db).FirstOrDefault() ?? (DeckContext.InStory ? DeckContext.Story.DeckProblem(my_deck) : null);
                battleDeckStatus.text = "Not battle-ready: " + problem + "\nBattle deck: " + battle_name;
                battleDeckStatus.color = UITheme.Current.danger;
            }
            else
            {
                battleDeckStatus.text = "Battle deck: " + battle_name;
                battleDeckStatus.color = UIOverlay.Gold;
            }
            if (ShowsPreset)
                battleDeckStatus.text += "\nA preset: changing or naming it makes your own copy";
            UpdateDeckActions();
        }

        //----------------------Helper Methods---------------------//
        public void WriteDeckToFile(Deck deck, string file_path)
        {
            if (DeckStorage.IsResource(file_path))
            {
                Debug.LogWarning("Skipping write to a deck of the game's own: " + file_path);
                return;
            }

            if (DeckStorage.Save(deck, file_path))
                GameLog.Event(LogCategory.Deck, "deck_saved", "path", file_path, "cards", deck.Cards.Count, "leader", Db.Get(deck.Leader).name);
            else
                UIOverlay.Toast("Could not save the deck");
        }

        // Valid by the rules (in the story, made of the cards owned)
        public bool IsDeckValid(Deck deck)
        {
            return DeckRules.IsValid(deck, Db) && (!DeckContext.InStory || DeckContext.Story.DeckProblem(deck) == null);
        }

        //--------To Main Menu
        // Back to the main menu, or the story's map
        public void ToMainMenu()
        {
            GameAudio.PlaySfx("ui_click");
            LoadingScreen.Load(DeckContext.InStory ? SceneNames.Story : SceneNames.Menu);
        }
    }
}
