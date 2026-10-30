using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gwent.DeckBuilding
{
    // A card of the deck builder's grids: the collection's (added to the deck by a double click, a tap on touch) or the
    // deck's (taken out of it the same way). It can also be dragged across: a drag mostly sideways carries a copy of it,
    // dropped anywhere in the other grid's column (CardGrid.TakesDropAt) it is added or taken out as a double click does
    // (refused: it goes back), dropped anywhere else it goes back; a drag mostly up or down scrolls the grid, as before.
    // A collection card all of whose copies are in the deck (spent, greyed) is not taken: its drag scrolls.
    public class CollectionCard : MonoBehaviour, IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public List<Sprite> normalPatch;
        public List<Sprite> uniquePatch;

        private const float BackSeconds = 0.25f;  // a copy dropped where it does nothing, back to its card

        private readonly DoubleClick doubleClick = new DoubleClick();
        private RectTransform carried;  // the copy a drag carries (null: the drag scrolls the grid, or none)
        private Vector2 grabbed;        // the copy's centre from the pointer (where the card was taken)
        private Rect takenFrom;         // the card's place on screen when it was taken

        [HideInInspector]
        public DeckBuilder deckBuilder;

        // The "Cards in Deck" collection (the one tagged Player)
        [HideInInspector]
        public CardGrid deckCollection;

        private void Start()
        {
            deckBuilder = DeckBuilder.Instance;
            deckCollection = deckBuilder.deckCollection;

            UpdateCardGUI();
            // Touch controls have no right click: a finger held on the card shows it large until it lifts
            LongPress.On(gameObject, () => { ShowBigCard(); return true; }, () => Details().SetActive(false));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Detect Card Right Click
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                GameAudio.PlaySfx("card_select");
                ShowBigCard();
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (UseSingleTapActions() || doubleClick.Register())
                    EditDeckWithThisCard();
            }
        }

        private bool UseSingleTapActions()
        {
            return GameInput.IsTouch;
        }

        // Whether this card is the deck's (else the collection's)
        private bool InDeck { get { return transform.parent.parent.parent.CompareTag("Player"); } }

        // The grid on the other side: the collection for a deck card, the deck for a collection card
        private CardGrid OtherGrid()
        {
            return InDeck ? FindObjectsByType<CardGrid>().First(grid => grid != deckCollection) : deckCollection;
        }

        // Adds the card to the deck, or takes it out (carried: the copy a drag brought, else one flies from the card);
        // false when the deck cannot take it
        private bool EditDeckWithThisCard(RectTransform carried = null)
        {
            Card cardStats = GetComponent<CardRef>().Card;
            if (InDeck)
            {
                // Remove card from deck
                GameAudio.PlaySfx("deck_remove");
                GameLog.Event(LogCategory.Deck, "card_removed", "deck", deckBuilder.my_deck.Name, "card", cardStats.name);
                RectTransform ghost = carried != null ? carried : TakeOff();
                CardGrid collection = OtherGrid();
                deckBuilder.my_deck.Cards.Remove(cardStats._id);
                deckCollection.OnDeckEdit();
                Land(ghost, collection, cardStats._id, deckBuilder.destroyCancellationToken).Forget();
                return true;
            }
            return AddCardToSelectedDeck(cardStats, carried);
        }

        private bool AddCardToSelectedDeck(Card cardStats, RectTransform carried)
        {
            if (deckBuilder.my_deck.Name == Gwent.Rules.DeckRules.EmptyDeckName)
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast("Create a deck first: type its name, then press +");
                return false;
            }

            int occurences = deckBuilder.my_deck.Cards.Where(x => x.Equals(cardStats._id)).Count();

            // Only the deck's faction, neutral and special cards (Mardroeme: Skellige's)
            Gwent.Rules.Card card = cardStats;
            if (!Gwent.Rules.DeckRules.Fits(card, deckBuilder.my_deck.Faction))
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast(card.DisplayName + " does not belong to this deck's faction");
                return false;
            }

            // The copies of the card that exist in The Witcher 3 (one of a hero); in the story, those owned
            if (occurences >= DeckContext.Supply.Copies(card))
            {
                GameAudio.PlaySfx("invalid");
                int owned = DeckContext.Supply.Copies(card);
                bool story_limit = DeckContext.InStory && owned < Gwent.Rules.DeckRules.MaxCopies(card);
                UIOverlay.Toast(story_limit ? "You own " + (owned == 1 ? "one " + card.DisplayName : owned + " copies of " + card.DisplayName)
                    : Gwent.Rules.DeckRules.TooManyCopies(card));
                return false;
            }

            // At most 10 special cards in a deck
            if (cardStats.faction == Faction.Special)
            {
                int specialsOcc = deckBuilder.GetSpecialsOccurence(deckBuilder.my_deck);
                if (specialsOcc >= Gwent.Rules.DeckRules.MaxSpecials)
                {
                    GameAudio.PlaySfx("invalid");
                    UIOverlay.Toast("A deck can hold at most " + Gwent.Rules.DeckRules.MaxSpecials + " special cards");
                    return false;
                }
            }

            // If all checks don't match, add the card to deck
            GameAudio.PlaySfx("deck_add");
            RectTransform ghost = carried != null ? carried : TakeOff();
            deckBuilder.my_deck.Cards.Add(cardStats._id);
            GameLog.Event(LogCategory.Deck, "card_added", "deck", deckBuilder.my_deck.Name, "card", cardStats.name);
            deckCollection.OnDeckEdit();
            Land(ghost, deckCollection, cardStats._id, deckBuilder.destroyCancellationToken).Forget();
            return true;
        }

        //----------------------------------------Dragging----------------------------------------//
        private ScrollRect Scroll { get { return GetComponentInParent<ScrollRect>(); } }

        // The grid's scroll stops gliding as a finger comes down, as it did before the cards took drags
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            ScrollRect scroll = Scroll;
            if (scroll != null)
                scroll.OnInitializePotentialDrag(eventData);
        }

        // Sideways: the card is taken (a copy follows the pointer); otherwise the grid scrolls
        public void OnBeginDrag(PointerEventData eventData)
        {
            Vector2 moved = eventData.position - eventData.pressPosition;
            if (eventData.button != PointerEventData.InputButton.Left || Mathf.Abs(moved.x) <= Mathf.Abs(moved.y) || CardGrid.IsSpent(gameObject))
            {
                ScrollRect scroll = Scroll;
                if (scroll != null)
                {
                    eventData.pointerDrag = scroll.gameObject; // the rest of this drag is the grid's
                    scroll.OnBeginDrag(eventData);
                }
                return;
            }
            takenFrom = CardMotion.ScreenRect((RectTransform)transform);
            grabbed = takenFrom.center - eventData.pressPosition;
            carried = TakeOff();
            Details().SetActive(false);
            GameAudio.PlaySfx("card_select");
            Follow(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (carried != null)
                Follow(eventData.position);
        }

        // In the other grid's column, the card goes in (or out of the deck); anywhere else, or refused, it goes back
        public void OnEndDrag(PointerEventData eventData)
        {
            if (carried == null)
                return;
            RectTransform ghost = carried;
            carried = null;
            if (OtherGrid().TakesDropAt(eventData.position) && EditDeckWithThisCard(ghost))
                return;
            GoBack(ghost, this != null ? CardMotion.ScreenRect((RectTransform)transform) : takenFrom, deckBuilder.destroyCancellationToken).Forget();
        }

        private void Follow(Vector2 pointer)
        {
            CardMotion.Place(carried, CardMotion.Around(pointer + grabbed, takenFrom.size));
        }

        private void OnDisable()
        {
            // The grid made anew under a drag (another edit): the copy goes
            if (carried != null)
                Destroy(carried.gameObject);
            carried = null;
        }

        private static async UniTaskVoid GoBack(RectTransform ghost, Rect home, CancellationToken ct)
        {
            try
            {
                await CardMotion.Glide(ghost, () => home, BackSeconds, ct: ct);
            }
            finally
            {
                if (ghost != null)
                    Destroy(ghost.gameObject);
            }
        }

        //----------------------------------------A card's flight----------------------------------------//
        // A card added or removed flies from where it was clicked to its place in the other grid (the deck, or back to the
        // collection). The edit makes both grids anew, this object with them: a copy of it flies, taken off first. Where
        // copies are left, the card stays (one fewer) and the copy leaves it; the last copy is the card itself leaving.
        private const float FlightSeconds = 0.35f;

        private RectTransform TakeOff()
        {
            RectTransform ghost = CardMotion.Copy(gameObject, CardMotion.ScreenRect((RectTransform)transform));
            Transform badge = ghost.Find(CardGrid.CountBadge);
            if (badge != null)
                Destroy(badge.gameObject); // one copy flies, not the count
            return ghost;
        }

        // The copy glides to the card's place in the grid, which counts it once it lands (CardGrid.Arriving): a card the
        // grid showed already stays, its count going up as the copy joins it; a card new there shows when it lands. A card
        // out of the grid's view (scrolled away, or not in the tab shown) is met at the view's edge, where the copy fades.
        private static async UniTaskVoid Land(RectTransform ghost, CardGrid grid, int card_id, CancellationToken ct)
        {
            grid.Arriving(card_id, 1);
            bool landed = false;
            try
            {
                GameObject target = grid.CardShown(card_id);
                bool in_view = grid.InView(target);
                Vector2 size = CardMotion.ScreenRect(ghost).size;
                await CardMotion.Glide(ghost, () =>
                {
                    if (target == null)
                        target = grid.CardShown(card_id); // made anew by another edit on the way
                    return grid.ArrivalRect(target, size);
                }, FlightSeconds, ct: ct);
                landed = true;
                if (in_view)
                    Destroy(ghost.gameObject);
                else
                    CardMotion.FadeOut(ghost, 0.15f, ct).Forget();
            }
            finally
            {
                if (grid != null)
                    grid.Arriving(card_id, -1);
                if (!landed && ghost != null)
                    Destroy(ghost.gameObject); // the deck builder closed on the way
            }
        }

        //------------------------------------------------------Functions---------------------------------------------------//
        private void UpdateCardGUI()
        {
            Card stats = GetComponent<CardRef>().Card;
            TextMeshProUGUI name_text = transform.Find("Name").GetComponent<TextMeshProUGUI>();
            Image patch = transform.Find("Patch").GetComponent<Image>();
            name_text.text = Card.DisplayNameOf(stats.name);

            // Neutral and special cards have no faction banner: their name is centered on a wider patch (Blood and Wine's
            // Mardroeme and Skellige Storm carry Skellige's)
            bool bannerless = (stats.faction == Faction.Neutral || stats.faction == Faction.Special)
                && stats._id != CardIds.Mardroeme && stats._id != CardIds.SkelligeStorm;
            if (bannerless)
            {
                RectTransform name_rect = name_text.rectTransform;
                name_rect.localPosition = new Vector2(0, name_rect.localPosition.y);
            }
            int variant = bannerless ? 1 : 0;
            patch.sprite = stats.unique ? uniquePatch[variant] : normalPatch[variant];
        }

        private GameObject Details()
        {
            return transform.parent.parent.parent.GetComponent<CardGrid>().cardDetails;
        }

        private void ShowBigCard()
        {
            GameObject cardDetails = Details();
            Card cardStats = GetComponent<CardRef>().Card;

            cardDetails.SetActive(true);
            CardArt.Show(cardDetails.transform.Find("BigImage").GetComponent<Image>(), cardStats._id);
            // A plain unit has no description
            GameObject effect = cardDetails.transform.Find("BigEffect").gameObject;
            effect.SetActive(DescriptionBox.Show(effect.GetComponent<Image>(), cardStats));
        }

    }
}
