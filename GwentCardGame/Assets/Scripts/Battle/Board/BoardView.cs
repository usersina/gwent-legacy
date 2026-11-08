using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // Draws the battle from the model: the card objects in the hands, rows, horn slots, discard piles and weather slot,
    // row highlights, the rows' weather, scores and the panels' markers. BattleController runs the battle and asks it
    // to redraw; this class never changes the model.
    public class BoardView
    {

        //----------------------------------------Cached scene references----------------------------------------//
        // Looked up once instead of transform.Find chains on every refresh / every frame
        public class RowView
        {
            public TextMeshProUGUI Score;
            public GameObject Unit;
            public GameObject Special;
            public Image UnitImage;
            public Image SpecialImage;
            public Image Emblem;     // sword, bow or catapult, engraved in the middle of the row while it has no card
            public Sprite Idle, Lit, IdleEmblem, LitEmblem;

            // Lights the row (a card can go there) or lights it off
            public void Light(bool lit)
            {
                UnitImage.sprite = lit ? Lit : Idle;
                Emblem.sprite = lit ? LitEmblem : IdleEmblem;
            }

            // The row's total
            public void ShowScore(List<int> strengths)
            {
                int total = 0;
                foreach (int strength in strengths)
                    total += strength;
                Score.text = total.ToString();
            }

            // The strength labels of the row's cards. The row shows its cards sorted (The Witcher 3's order) while the
            // game keeps them in play order, so each card takes the strength of a game entry with its id.
            public async UniTaskVoid ShowStrengths(List<int> card_ids, List<int> base_strengths, List<int> strengths, CancellationToken ct)
            {
                // New card objects are only there from the next frame (the destroyed ones are still children until then)
                if (await UniTask.NextFrame(ct).SuppressCancellationThrow())
                    return;

                // Only card children
                List<CardFace> cards = new List<CardFace>();
                foreach (Transform child in Unit.transform)
                {
                    CardFace face = child.GetComponent<CardFace>();
                    if (face != null)
                        cards.Add(face);
                }
                if (cards.Count != card_ids.Count || base_strengths.Count != card_ids.Count || strengths.Count != card_ids.Count)
                {
                    Debug.LogError("ShowStrengths: params mismatch (cards " + cards.Count + ", ids " + card_ids.Count
                        + ", base " + base_strengths.Count + ", modified " + strengths.Count + ")");
                    return;
                }

                bool[] used = new bool[card_ids.Count];
                foreach (CardFace card in cards)
                {
                    int id = card.Card._id;
                    int i = 0;
                    while (i < card_ids.Count && (used[i] || card_ids[i] != id))
                        i++;
                    if (i == card_ids.Count)
                        continue;
                    used[i] = true;
                    TextMeshProUGUI strength_text = card.strengthImage.GetComponent<TextMeshProUGUI>();
                    // Red when weakened, green when boosted, else the card's own colour: card objects are reused
                    // between refreshes, so a card back to its base strength must lose the red / green
                    if (base_strengths[i] > strengths[i])
                        strength_text.color = new Color32(188, 0, 0, 255);
                    else if (base_strengths[i] < strengths[i])
                        strength_text.color = new Color32(0, 188, 0, 255);
                    else
                        strength_text.color = card.Card.unique ? Color.white : Color.black;
                    strength_text.text = strengths[i].ToString();
                }
            }
        }

        public class FieldView
        {
            public GameObject Root;
            public RowPicker Picker;
            public GameObject Hand;
            public DeckStack Deck;
            public GameObject DiscardPile;
            public SidePanel Stats;
            public LeaderSlot Leader;
            public GameObject LeaderHolder;
            public GameObject LeadMarker;   // the laurel around the total of the side that is ahead (as in The Witcher 3)
            public GameObject PassedLabel;  // "Passed" by the total score of a side that has passed
            public readonly Dictionary<Row, RowView> Rows = new Dictionary<Row, RowView>();

            public FieldView(GameObject root)
            {
                Root = root;
                Transform t = root.transform;
                Picker = root.GetComponent<RowPicker>();
                Hand = t.Find("Hand").gameObject;
                Deck = t.Find("Deck").GetComponent<DeckStack>();
                DiscardPile = t.Find("Discard").Find("Pile").gameObject;
                Stats = t.Find("Stats").GetComponent<SidePanel>();
                Leader = t.Find("Leader").GetComponent<LeaderSlot>();
                LeaderHolder = t.Find("Leader").Find("Holder").gameObject;
                LeadMarker = t.Find("Stats").Find("Total").Find("Laurel").gameObject;
                PassedLabel = t.Find("Stats").Find("Passed").gameObject;
                TMP_Text passed_text = PassedLabel.GetComponentInChildren<TMP_Text>(true);
                if (passed_text != null)
                    passed_text.text = Loc.T(Loc.UI, "battle.passed_label"); // the scene says "Passed" itself
                foreach (Row row in Gwent.Rules.Rows.Units)
                {
                    Transform row_t = t.Find(row.ToString()); // the rows' objects are named Close, Range and Siege
                    RowView view = new RowView
                    {
                        Score = row_t.Find("ScoreCount").Find("Number").GetComponent<TextMeshProUGUI>(),
                        Unit = row_t.Find("Unit").gameObject,
                        Special = row_t.Find("Special").gameObject,
                        UnitImage = row_t.Find("Unit").GetComponent<Image>(),
                        SpecialImage = row_t.Find("Special").GetComponent<Image>(),
                    };
                    switch (row)
                    {
                        case Gwent.Rules.Row.Close:
                            view.Idle = Picker.close; view.Lit = Picker.closeSelected;
                            view.IdleEmblem = Picker.closeEmblem; view.LitEmblem = Picker.closeEmblemSelected;
                            break;
                        case Gwent.Rules.Row.Range:
                            view.Idle = Picker.range; view.Lit = Picker.rangeSelected;
                            view.IdleEmblem = Picker.rangeEmblem; view.LitEmblem = Picker.rangeEmblemSelected;
                            break;
                        default:
                            view.Idle = Picker.siege; view.Lit = Picker.siegeSelected;
                            view.IdleEmblem = Picker.siegeEmblem; view.LitEmblem = Picker.siegeEmblemSelected;
                            break;
                    }
                    view.Emblem = AddEmblem(view);
                    Rows[row] = view;
                }
            }

            // In the middle of the row, at the size the row art is shown at (under the cards)
            private static Image AddEmblem(RowView row)
            {
                Image emblem = UIKit.NewImage(row.Unit.transform, "Emblem", row.IdleEmblem, Color.white);
                RectTransform rect = emblem.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                Vector2 shown = row.UnitImage.rectTransform.rect.size;
                Vector2 art = row.Idle.rect.size;
                rect.sizeDelta = Vector2.Scale(row.IdleEmblem.rect.size, new Vector2(shown.x / art.x, shown.y / art.y));
                rect.SetAsFirstSibling();
                return emblem;
            }
        }

        private readonly BattleController controller;
        private readonly FieldView playerView;
        private readonly FieldView enemyView;

        public WeatherSlot Weather { get; private set; }
        private readonly Image weatherImage;

        public BoardView(BattleController battle)
        {
            controller = battle;
            playerView = new FieldView(battle.PlayerField);
            enemyView = new FieldView(battle.EnemyField);
            foreach (bool is_player in new[] { true, false })
            {
                FieldView view = View(is_player);
                // A click on either discard pile shows it card by card
                view.DiscardPile.transform.parent.gameObject.AddComponent<DiscardPileBrowser>().isPlayer = is_player;
                // "Passed" right under the opponent's total score, above the player's (The Witcher 3), on the edge of
                // the band, clear of the laurel and of the rows' score coins
                RectTransform passed = (RectTransform)view.PassedLabel.transform;
                RectTransform total = (RectTransform)passed.parent.Find("Total");
                float gap = ((RectTransform)view.LeadMarker.transform).rect.height / 2 + passed.rect.height / 2;
                passed.anchoredPosition = total.anchoredPosition + new Vector2(-6, is_player ? gap : -gap);
                // The Witcher 3's lines of cards: centred, gliding into place (the grids snapped)
                CardLane.Install(view.Hand, 50);
                foreach (RowView row in view.Rows.Values)
                {
                    CardLane.Install(row.Unit, 100).emptyMark = row.Emblem.gameObject;
                    CardLane.Install(row.Special, 0);
                }
            }
            CardLane.Install(battle.WeatherField, 0);
            Weather = battle.WeatherField.GetComponent<WeatherSlot>();
            weatherImage = battle.WeatherField.GetComponent<Image>();
        }

        public FieldView View(bool is_player) { return is_player ? playerView : enemyView; }

        private BattleController.BattleSide Info(bool is_player) { return is_player ? controller.PlayerInfo : controller.EnemyInfo; }

        private bool clearingRound; // the board refreshed is a new round's (the weather goes with the round: no sun)

        //----------------------------------------Whole board----------------------------------------//
        // Shows both sides and the weather from the lists, then lights off the rows and updates the scores
        // (round_clear: the board cleared for a new round)
        public void Refresh(bool round_clear = false)
        {
            clearingRound = round_clear;
            RefreshField(true);
            RefreshField(false);
            SyncCards(controller.WeatherField, controller.weatherList, true, BoardChange.Place.Weather);
            Lightoff(true);
            Lightoff(false);
            UpdateScores(true);
            UpdateScores(false);
        }

        // The laurel on the side that is ahead, "Passed" on a side that has passed, the gold lines on the band of the
        // side whose turn it is, the opponent greyed out while its device is away (a LAN battle), the lit crown of a leader
        // ability that can still be used this round (every frame)
        public void UpdateMarkers()
        {
            foreach (bool is_player in new[] { true, false })
            {
                BattleController.BattleSide info = Info(is_player);
                View(is_player).Leader.UISetReady(info.canLeader && !info.leaderDisabled && !info.hasPassed);
            }
            playerView.Stats.UISetTurnState(controller.battleState == BattleState.PlayerTurn && !controller.PlayerInfo.hasPassed, false);
            enemyView.Stats.UISetTurnState(controller.battleState == BattleState.EnemyTurn && !controller.EnemyInfo.hasPassed, controller.OpponentAway);
            int player_total = controller.PlayerInfo.TotalScore(), enemy_total = controller.EnemyInfo.TotalScore();
            SetActiveIfChanged(playerView.LeadMarker, player_total > enemy_total);
            SetActiveIfChanged(enemyView.LeadMarker, enemy_total > player_total);
            SetActiveIfChanged(playerView.PassedLabel, controller.PlayerInfo.hasPassed);
            SetActiveIfChanged(enemyView.PassedLabel, controller.EnemyInfo.hasPassed);
        }

        private static void SetActiveIfChanged(GameObject go, bool active)
        {
            if (go.activeSelf != active)
                go.SetActive(active);
        }

        private void RefreshField(bool is_player_field)
        {
            BattleController.BattleSide info = Info(is_player_field);
            FieldView view = View(is_player_field);

            SetHand(info.HandList, is_player_field);

            // Rows in The Witcher 3's order (special cards first, then units by strength)
            foreach (Row row in Rows.Units)
            {
                RowView row_view = view.Rows[row];
                SyncCards(row_view.Unit, CardOrder.Sorted(info.UnitRow(row), controller.Db), is_player_field, BoardChange.Place.Row);
                SyncCards(row_view.Special, info.SpecialRow(row), is_player_field, BoardChange.Place.Horn);
            }

            SetDiscard(info.DiscardList, is_player_field);
            RefreshWeather(view);
        }

        //----------------------------------------Piles and hand----------------------------------------//
        // Updates the deck count and the stack of card backs
        public void SetDeck(BattleController.BattleSide info, bool is_player_deck)
        {
            View(is_player_deck).Deck.ShowStack(info.DeckBSprite, info.DeckList.Count);
        }

        // Shows the hand as it was drawn, as The Witcher 3 does (the redraw's carousel shows it the same way): a redrawn
        // card takes the place of the one it replaces, cards that come later join at the right. Card objects are reused
        // (cards slide to make room); the count shows too.
        public void SetHand(List<int> hand_list, bool is_player_hand)
        {
            FieldView view = View(is_player_hand);
            SyncCards(view.Hand, hand_list, is_player_hand, BoardChange.Place.Hand);
            view.Stats.UIUpdateHandCount(hand_list.Count);
        }

        public void SetDiscard(List<int> discard_list, bool is_player_hand)
        {
            FieldView view = View(is_player_hand);
            if (!SyncCards(view.DiscardPile, discard_list, is_player_hand, BoardChange.Place.Discard))
                return;

            // A visible stack, as in The Witcher 3: older cards peek out to the bottom right of the top one
            Transform pile = view.DiscardPile.transform;
            int top = pile.childCount - 1;
            for (int i = 0; i < pile.childCount; i++)
            {
                float depth = Mathf.Min(top - i, 12) * 0.7f;
                pile.GetChild(i).localPosition = new Vector3(depth, -depth, 0);
            }
        }

        //----------------------------------------Card objects----------------------------------------//
        // True when a container already shows exactly these cards, in this order (card objects are named after their
        // id; other children are ignored). Refreshes skip such containers: rebuilding the
        // whole board on every play cost ~30 ms in the editor on a busy board, several times that on a phone.
        private static bool ShowsCards(Transform container, List<int> card_ids)
        {
            int shown = 0;
            foreach (Transform child in container)
            {
                if (child.GetComponent<CardFace>() == null)
                    continue;
                if (shown >= card_ids.Count || child.name != card_ids[shown].ToString())
                    return false;
                shown++;
            }
            return shown == card_ids.Count;
        }

        // Makes a container show exactly these cards, in this order, reusing the card objects already there: a play
        // only creates the cards that arrived and removes the ones that left. Hand cards can be selected and hovered,
        // board cards only show their preview; other children stay after the cards.
        // While a change is recorded, the board keeps looking as it was (see BeginChange). Returns false when nothing
        // changed.
        private bool SyncCards(GameObject container, List<int> card_ids, bool is_player_card, BoardChange.Place place)
        {
            Transform parent = container.transform;
            if (ShowsCards(parent, card_ids))
                return false;
            bool hand = place == BoardChange.Place.Hand;
            CardLane lane = container.GetComponent<CardLane>();

            List<GameObject> before = new List<GameObject>();
            foreach (Transform child in parent)
                if (child.GetComponent<CardFace>() != null)
                    before.Add(child.gameObject);

            // The objects kept, for each card: first the one still in its place (a redraw replaces the very card chosen,
            // even with two alike in the hand), then one further along the line, in order (the cards after one that left
            // slide), then any other copy
            GameObject[] chosen = new GameObject[card_ids.Count];
            HashSet<GameObject> kept = new HashSet<GameObject>();
            for (int i = 0; i < card_ids.Count && i < before.Count; i++)
                if (before[i].name == card_ids[i].ToString())
                {
                    chosen[i] = before[i];
                    kept.Add(before[i]);
                }
            for (int i = 0, last = -1; i < card_ids.Count; i++)
            {
                if (chosen[i] != null)
                {
                    last = i;
                    continue;
                }
                string name = card_ids[i].ToString();
                for (int j = last + 1; j < before.Count; j++)
                    if (!kept.Contains(before[j]) && before[j].name == name)
                    {
                        chosen[i] = before[j];
                        kept.Add(before[j]);
                        last = j;
                        break;
                    }
            }
            for (int i = 0; i < card_ids.Count; i++)
                if (chosen[i] == null)
                {
                    chosen[i] = before.Find(card => !kept.Contains(card) && card.name == card_ids[i].ToString());
                    if (chosen[i] != null)
                        kept.Add(chosen[i]);
                }

            List<GameObject> ordered = new List<GameObject>(card_ids.Count);
            for (int i = 0; i < card_ids.Count; i++)
            {
                int id = card_ids[i];
                GameObject card = chosen[i];
                if (card == null)
                {
                    card = CreateCardObject(controller.Db.Get(id), is_player_card);
                    card.GetComponent<CardSelect>().isSelectable = hand;
                    card.GetComponent<CardHover>().isHoverable = hand;
                    card.GetComponent<CardExchange>().isExchangeable = hand && controller.Db.Get(id).ability != Ability.Leader;
                    card.transform.SetParent(parent, false);
                    if (arrivals != null)
                    {
                        CardMotion.Hide(card, true);
                        if (lane != null)
                            LaneHold.Hold(card);
                        arrivals.Add(new BoardChange.Arrival { CardId = id, IsPlayer = is_player_card, To = place, Card = card });
                    }
                    else
                        CardMotion.SettleIn(card);
                }
                ordered.Add(card);
            }
            List<GameObject> unused = before.Where(card => !kept.Contains(card)).ToList();

            List<GameObject> siblings = new List<GameObject>(ordered);
            foreach (GameObject card in unused.OrderBy(before.IndexOf))
            {
                int id;
                BoardChange.Arrival came = arrivals != null ? arrivals.Find(a => a.Card == card) : null;
                if (arrivals == null || came != null || !int.TryParse(card.name, out id))
                {
                    if (came != null)
                        arrivals.Remove(came); // came and went within the change
                    card.transform.SetParent(null, false); // out of the container now: counts and layouts must not see it
                    Object.Destroy(card);
                    continue;
                }
                // Recorded: the card stays where it stood, and its line keeps its room, until it is taken away
                BoardChange.Departure departure = new BoardChange.Departure { CardId = id, IsPlayer = is_player_card, From = place };
                if (lane != null)
                {
                    departure.Spacer = LaneHold.Spacer(parent);
                    siblings.Insert(SpacerIndex(siblings, kept, before.Take(before.IndexOf(card)).Count(kept.Contains)), departure.Spacer);
                }
                departure.Ghost = CardMotion.Lift(card);
                departures.Add(departure);
            }
            for (int i = 0; i < siblings.Count; i++)
                siblings[i].transform.SetSiblingIndex(i);
            return true;
        }

        // Where a spacer goes in a line: after the cards still there that stood before the card that left, and after the
        // spacers already there (held cards are out of the line, they do not count)
        private static int SpacerIndex(List<GameObject> siblings, HashSet<GameObject> kept, int kept_before)
        {
            int index = 0;
            for (int seen = 0; index < siblings.Count && seen < kept_before; index++)
                if (kept.Contains(siblings[index]))
                    seen++;
            while (index < siblings.Count && siblings[index].GetComponent<CardFace>() == null)
                index++;
            return index;
        }

        //----------------------------------------Recording a play (the choreography)----------------------------------------//
        private List<BoardChange.Arrival> arrivals;     // while recording
        private List<BoardChange.Departure> departures;

        // Until EndChange, refreshes leave the board looking as it was: a card that leaves stays where it stood (on the
        // overlay, its room kept in its line) and a card that arrives stays hidden, out of its line. BoardPresenter
        // then moves them as The Witcher 3 does.
        public void BeginChange()
        {
            if (arrivals != null)
                EndChange().Flush(); // a play that failed halfway left its change open: show it as it is
            arrivals = new List<BoardChange.Arrival>();
            departures = new List<BoardChange.Departure>();
        }

        public BoardChange EndChange()
        {
            BoardChange change = new BoardChange(arrivals ?? new List<BoardChange.Arrival>(), departures ?? new List<BoardChange.Departure>());
            arrivals = null;
            departures = null;
            return change;
        }

        // Screen rectangles the choreography moves cards from
        public Rect DeckRect(bool is_player) { return CardMotion.ScreenRect((RectTransform)View(is_player).Deck.transform.Find("DeckHolder")); }
        // The top card of a discard pile (the pile itself when it is empty)
        public RectTransform PileTop(bool is_player)
        {
            Transform pile = View(is_player).DiscardPile.transform;
            for (int i = pile.childCount - 1; i >= 0; i--)
                if (pile.GetChild(i).GetComponent<CardFace>() != null)
                    return (RectTransform)pile.GetChild(i);
            return (RectTransform)pile;
        }

        public Rect PileRect(bool is_player) { return CardMotion.ScreenRect((RectTransform)View(is_player).DiscardPile.transform.parent); }

        // Where a played card appears, at its size there (The Witcher 3): at the right end of its row, level with its place,
        // to glide left to it; a card off the rows (the weather board) at the right end of the rows, halfway up the board
        public Rect LaunchRect(GameObject card)
        {
            Rect slot = CardMotion.ScreenRect((RectTransform)card.transform);
            foreach (FieldView view in new[] { playerView, enemyView })
                foreach (RowView row in view.Rows.Values)
                    if (card.transform.parent == row.Unit.transform || card.transform.parent == row.Special.transform)
                    {
                        Rect lane = CardMotion.ScreenRect((RectTransform)row.Unit.transform);
                        return new Rect(lane.xMax - slot.width, slot.y, slot.width, slot.height);
                    }
            Rect mine = CardMotion.ScreenRect((RectTransform)playerView.Rows[Row.Close].Unit.transform);
            Rect theirs = CardMotion.ScreenRect((RectTransform)enemyView.Rows[Row.Close].Unit.transform);
            return CardMotion.Around(new Vector2(mine.xMax, (mine.yMax + theirs.yMin) / 2), slot.size);
        }

        // Where the opponent's card comes in from: above the board, over the middle of the rows
        public Rect EntryRect(Vector2 size)
        {
            Rect mine = CardMotion.ScreenRect((RectTransform)playerView.Rows[Row.Close].Unit.transform);
            return new Rect(new Vector2(mine.center.x - size.x / 2, Screen.height + 10), size);
        }

        // Generate Cards GameObject from cards prefab and set their parent to the passed gameobject
        public void CreateCardObjects(GameObject my_object, List<int> card_list, bool isSelectable, bool is_player_card)
        {
            foreach (int card_id in card_list)
            {
                GameObject instantiatedCard = CreateCardObject(controller.Db.Get(card_id), is_player_card);
                instantiatedCard.GetComponent<CardSelect>().isSelectable = isSelectable;
                instantiatedCard.GetComponent<CardHover>().isHoverable = isSelectable;
                instantiatedCard.GetComponent<CardExchange>().isExchangeable = controller.Db.Get(card_id).ability != Ability.Leader;
                instantiatedCard.transform.SetParent(my_object.transform, false);
            }
        }

        // A card object with its stats filled in (not parented)
        public GameObject CreateCardObject(Card card, bool is_player_card)
        {
            GameObject instantiatedCard = Object.Instantiate(controller.CardPrefab);
            instantiatedCard.name = card._id.ToString();
            CardRef stats = instantiatedCard.transform.Find("Stats").GetComponent<CardRef>();
            stats.Card = card;
            instantiatedCard.tag = is_player_card ? "Player" : "Enemy";
            return instantiatedCard;
        }

        //----------------------------------------Weather and scores----------------------------------------//
        // Each row's weather over its cards (WeatherEffect): it fades in with its weather card, out once cleared. Skellige
        // Storm is a stormy fog over the ranged row and a stormy rain over the siege row. Weather cleared during a round
        // (Clear Weather, Foltest's order) goes in the sun's light (SunSweep), unless Settings > Graphics turned it off.
        private void RefreshWeather(FieldView view)
        {
            List<int> weather = controller.weatherList;
            bool storm = weather.Contains(CardIds.SkelligeStorm);
            ShowWeather(view.Rows[Row.Close], WeatherEffect.Kind.Frost, weather.Contains(CardIds.BitingFrost), false);
            ShowWeather(view.Rows[Row.Range], WeatherEffect.Kind.Fog, storm || weather.Contains(CardIds.ImpenetrableFog), storm);
            ShowWeather(view.Rows[Row.Siege], WeatherEffect.Kind.Rain, storm || weather.Contains(CardIds.TorrentialRain), storm);
        }

        private void ShowWeather(RowView row, WeatherEffect.Kind kind, bool on, bool storm)
        {
            RectTransform slot = (RectTransform)row.Special.transform, lane = (RectTransform)row.Unit.transform;
            if (WeatherEffect.Show(slot, lane, kind, on, storm) && !clearingRound && GameSettings.Shows(GameSettings.Effect.ClearWeatherSun))
                SunSweep.Over(slot, lane);
        }

        private void UpdateScores(bool is_player_field)
        {
            BattleController.BattleSide info = Info(is_player_field);
            FieldView view = View(is_player_field);

            // New card objects get their strength labels next frame
            foreach (Row row in Rows.Units)
            {
                RowView row_view = view.Rows[row];
                row_view.ShowStrengths(info.UnitRow(row), info.BaseStrengths(row), info.ModifiedStrengths(row), controller.destroyCancellationToken).Forget();
                row_view.ShowScore(info.ModifiedStrengths(row));
            }

            view.Stats.UIUpdateTotalScore(info.MCloseStrength, info.MRangeStrength, info.MSiegeStrength);
            SetDeck(info, is_player_field);
            view.Stats.UIUpdateHandCount(info.HandList.Count);
        }

        //----------------------------------------Highlighting----------------------------------------//
        // Highlights the rows where the selected card can go, and stores them in the RowPicker
        public void Highlight(Card card_stats, bool is_player_turn)
        {
            FieldView mine = View(is_player_turn);
            FieldView opp = View(!is_player_turn);

            mine.Picker.ResetStored();
            Weather.isWeatherCard = false;

            if (card_stats.faction == Faction.Special)
            {
                if (card_stats.row == Row.Special)
                {
                    // Commander's Horn or Mardroeme: any free special slot (one card per row)
                    PlayerState side = is_player_turn ? (PlayerState)controller.PlayerInfo : controller.EnemyInfo;
                    foreach (KeyValuePair<Row, RowView> row in mine.Rows)
                    {
                        if (side.SlotTaken(row.Key))
                            continue;
                        row.Value.SpecialImage.sprite = mine.Picker.specialSelected;
                        mine.Picker.StoreField(row.Value.Special);
                    }
                }
                else if (card_stats.ability == Ability.Decoy)
                {
                    // Decoy goes on a card, not a row: frame the units it can take back
                    MarkDecoyTargets(mine, true);
                }
                else
                {
                    // Weather and scorch go on the weather board
                    weatherImage.sprite = Weather.weatherSelected;
                    Weather.isWeatherCard = true;
                }
                return;
            }

            // Spies are played on the opponent's side
            FieldView target = card_stats.ability == Ability.Spy ? opp : mine;
            target.Picker.ResetStored();
            Row[] rows = card_stats.row == Row.Agile ? new[] { Row.Close, Row.Range } : new[] { card_stats.row };
            foreach (Row row in rows.Where(Rows.IsUnitRow))
            {
                target.Rows[row].Light(true);
                target.Picker.StoreField(target.Rows[row].Unit);
            }
        }

        // Lights off any highlighted field
        public void Lightoff(bool is_player_turn)
        {
            FieldView view = View(is_player_turn);
            view.Picker.ResetStored();
            foreach (KeyValuePair<Row, RowView> row in view.Rows)
            {
                row.Value.SpecialImage.sprite = view.Picker.special;
                row.Value.Light(false);
            }

            // Light off the weather board and deselect weather card
            weatherImage.sprite = Weather.weather;
            Weather.isWeatherCard = false;
            MarkDecoyTargets(view, false);
        }

        public const string LeaderTargetFrame = "LeaderTarget";

        // Lights where the player's leader ability acts, as for a selected card: the weather board (weather leaders),
        // its row's horn slot, the opponent's row it scorches, the rows of agile units, the discard pile it takes a unit
        // from (framed). Nothing for the others (the crown uses them). Returns the places lit.

        public List<GameObject> MarkLeaderTargets(int leader_id)
        {
            List<GameObject> targets = new List<GameObject>();
            FieldView mine = View(true);
            FieldView opp = View(false);
            Row? horn = LeaderIds.HornRow(leader_id);
            Row? scorch = LeaderIds.ScorchRow(leader_id);
            if (LeaderIds.WeatherPickRow(leader_id) != null || leader_id == LeaderIds.FoltestLordCommander)
            {
                weatherImage.sprite = Weather.weatherSelected;
                targets.Add(controller.WeatherField);
            }
            else if (horn != null)
            {
                mine.Rows[horn.Value].SpecialImage.sprite = mine.Picker.specialSelected;
                targets.Add(mine.Rows[horn.Value].Special);
            }
            else if (scorch != null)
            {
                opp.Rows[scorch.Value].Light(true);
                targets.Add(opp.Rows[scorch.Value].Unit);
            }
            else if (leader_id == LeaderIds.FrancescaHopeOfTheAenSeidhe)
            {
                foreach (Row row in new[] { Row.Close, Row.Range })
                {
                    mine.Rows[row].Light(true);
                    targets.Add(mine.Rows[row].Unit);
                }
            }
            else if (leader_id == LeaderIds.EredinBringerOfDeath || leader_id == LeaderIds.EmhyrTheRelentless || leader_id == LeaderIds.CrachAnCraite)
            {
                // The discard pile it takes a card from; Crach an Craite's, both (back into the decks)
                bool[] sides = leader_id == LeaderIds.CrachAnCraite ? new[] { true, false } : new[] { leader_id == LeaderIds.EredinBringerOfDeath };
                foreach (bool own in sides)
                {
                    GameObject pile = View(own).DiscardPile.transform.parent.gameObject;
                    foreach (Image side in UIKit.Frame(pile.transform, LeaderTargetFrame, UITheme.Current.goldBright, 4, 3).GetComponentsInChildren<Image>())
                        UIMotion.Pulse(side);
                    targets.Add(pile);
                }
            }
            return targets;
        }

        // Lights them off (the rows and the weather board go back to normal with Lightoff)
        public void UnmarkLeaderTargets(List<GameObject> targets)
        {
            foreach (GameObject target in targets)
            {
                Transform frame = target != null ? target.transform.Find(LeaderTargetFrame) : null;
                if (frame != null)
                {
                    frame.SetParent(null, false);
                    Object.Destroy(frame.gameObject);
                }
            }
        }

        // A gold frame around each unit of this side a decoy can take back (PlayRules.IsDecoyTarget: no hero, no token)
        public const string DecoyFrame = "DecoyTarget";

        private void MarkDecoyTargets(FieldView view, bool on)
        {
            foreach (RowView row in view.Rows.Values)
                foreach (Transform card in row.Unit.transform)
                {
                    CardFace display = card.GetComponent<CardFace>();
                    if (display == null)
                        continue;
                    Transform frame = card.Find(DecoyFrame);
                    Card stats = display.Card;
                    bool target = on && PlayRules.IsDecoyTarget(stats);
                    if (target && frame == null)
                    {
                        // Square, as the card is, and pulsing like the leader's targets
                        foreach (Image side in UIKit.Frame(card, DecoyFrame, UITheme.Current.goldBright, 3, 3).GetComponentsInChildren<Image>())
                            UIMotion.Pulse(side);
                    }
                    else if (!target && frame != null)
                    {
                        frame.SetParent(null, false);
                        Object.Destroy(frame.gameObject);
                    }
                }
        }

        // Hides the player's hand interactability
        public void SetHider(bool hidden)
        {
            controller.hiderObject.transform.localPosition = hidden ? new Vector2(0, -461) : new Vector2(0, -618);
        }
    }
}
