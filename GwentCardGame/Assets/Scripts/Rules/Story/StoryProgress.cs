using System;
using System.Collections.Generic;
using System.Linq;

namespace Gwent.Rules
{
    // What a win, a purchase, a card found or a tournament's round gives: cards, crowns, and what it led to (quests done,
    // their rewards among the cards; regions opened; places that appeared on the map)
    public class StoryReward
    {
        public readonly List<int> Cards = new List<int>();
        public int Crowns;
        public readonly List<StoryQuest> Quests = new List<StoryQuest>();
        public readonly List<StoryRegion> Regions = new List<StoryRegion>();
        public readonly List<StoryPlace> Revealed = new List<StoryPlace>();
    }

    // The story's rules over its content and a save (StoryState), as The Witcher 3 hands out its cards:
    // - a player's first win gives their prize (a player of no particular renown: the next card of the pool, in the
    //   order the save's seed gives it), and pays their crowns; a rematch pays a quarter, so crowns can always be earned;
    // - a shop sells each card of its stock once; a card found or given is taken once;
    // - a tournament is entered for its fee; each round won gives its prize the first time; a loss ends the run (enter
    //   again for the fee); won through, it is done;
    // - a quest is done when every step is (a place beaten, found or given; a tournament won through), and gives its
    //   reward then; regions open and places appear by their conditions (StoryContent.Condition).
    // Nothing can be missed: no place goes away, rematches are always allowed.
    public class StoryProgress : ICardSupply
    {
        public const int RematchShare = 4; // a rematch pays a quarter of a first win

        public StoryContent Content { get; private set; }
        public StoryState State { get; private set; }

        private readonly CardDatabase db;
        private readonly Dictionary<int, int> owned = new Dictionary<int, int>();
        private readonly Dictionary<string, int> wins = new Dictionary<string, int>();
        private readonly HashSet<string> taken, bought, quests;
        private List<int> pool;

        public StoryProgress(StoryContent content, StoryState state, CardDatabase db)
        {
            Content = content;
            State = state;
            this.db = db;
            foreach (int card in state.Cards)
                owned[card] = Owned(card) + 1;
            foreach (StoryWins win in state.Wins)
                wins[win.Place] = win.Count;
            taken = new HashSet<string>(state.Taken);
            bought = new HashSet<string>(state.Bought);
            quests = new HashSet<string>(state.Quests);
        }

        // A new story: The Witcher 3's starter deck and four leaders, no crowns, in its first region; seed orders the pool
        public static StoryState NewState(StoryContent content, int seed)
        {
            return new StoryState { Seed = seed, Cards = new List<int>(content.File.Starter), Region = content.First.Id };
        }

        //----------------------------------------Cards----------------------------------------//
        public int Owned(int card)
        {
            int n;
            return owned.TryGetValue(card, out n) ? n : 0;
        }

        // ICardSupply: a story deck holds the copies owned (never more than a deck may)
        public int Copies(Card card)
        {
            return Math.Min(Owned(card._id), DeckRules.MaxCopies(card));
        }

        // The factions a story deck can be built for: those with a leader owned
        public List<Faction> OpenFactions
        {
            get
            {
                return Factions.Playable.Where(faction => LeaderIds.ByFaction[faction].Any(leader => Owned(leader) > 0)).ToList();
            }
        }

        // Every copy of every card owned (The Witcher 3's Collect 'Em All)
        public bool CollectionComplete
        {
            get { return db.All.All(card => Owned(card._id) >= card.copies); }
        }

        // Why a story deck can't go into battle (the deck rules, then the copies owned), or null
        public string DeckProblem(Deck deck)
        {
            List<string> errors = DeckRules.Validate(deck, db);
            if (errors.Count > 0)
                return errors[0];
            Card leader = db.Get(deck.Leader);
            if (Copies(leader) == 0)
                return "You don't own " + leader.DisplayName;
            foreach (IGrouping<int, int> group in deck.Cards.GroupBy(id => id))
            {
                Card card = db.Get(group.Key);
                int have = Copies(card);
                if (group.Count() > have)
                    return have == 0 ? "You don't own " + card.DisplayName : "You own only " + have + " " + card.DisplayName;
            }
            return null;
        }

        private void Give(int card, StoryReward reward)
        {
            owned[card] = Owned(card) + 1;
            State.Cards.Add(card);
            reward.Cards.Add(card);
        }

        private void Pay(int crowns, StoryReward reward)
        {
            State.Crowns += crowns;
            reward.Crowns += crowns;
        }

        //----------------------------------------The map----------------------------------------//
        public bool Met(string condition)
        {
            string kind, id;
            if (!StoryContent.Condition(condition, out kind, out id))
                return true;
            if (kind == "quest")
                return QuestDone(Content.Quest(id));
            StoryPlace place = Content.Place(id);
            return place != null && IsDone(place);
        }

        public bool IsOpen(StoryRegion region)
        {
            return region != null && Met(region.Opens);
        }

        public bool IsVisible(StoryPlace place)
        {
            return IsOpen(Content.Region(place.Region)) && Met(place.Appears);
        }

        public int Wins(string id)
        {
            int n;
            return wins.TryGetValue(id, out n) ? n : 0;
        }

        // Beaten (a tournament: won through), found or given; a shop is never done (its stock is Bought)
        public bool IsDone(StoryPlace place)
        {
            if (place.Taken)
                return taken.Contains(place.Id);
            if (place.Type == StoryPlaceKind.Tournament)
                return place.Rounds.All(round => Wins(RoundKey(place, round)) > 0);
            return place.Type == StoryPlaceKind.Player && Wins(place.Id) > 0;
        }

        // Something there to win, find or buy still (the map shows it in gold)
        public bool HasMore(StoryPlace place)
        {
            if (place.Sells.Where((sale, i) => !Bought(place, i)).Any())
                return true;
            if (place.Taken)
                return !taken.Contains(place.Id);
            if (place.Type == StoryPlaceKind.Tournament)
                return place.Rounds.Any(round => Wins(RoundKey(place, round)) == 0 && round.Prize.Count > 0);
            return place.Type == StoryPlaceKind.Player && Wins(place.Id) == 0 && PrizeOf(place).Count > 0;
        }

        // What a first win against a player gives: their prize; a player of no particular renown, the pool's next card
        // (none once it is empty)
        public List<int> PrizeOf(StoryPlace place)
        {
            if (place.Prize.Count > 0 || !place.Random)
                return place.Prize;
            List<int> order = PoolOrder();
            return State.Drawn < order.Count ? new List<int> { order[State.Drawn] } : new List<int>();
        }

        // What beating them pays now: a first win their crowns, a rematch a quarter
        public int CrownsFor(StoryPlace place, StoryRound round = null)
        {
            int crowns = round != null ? round.Crowns : place.Crowns;
            int before = round != null ? Wins(RoundKey(place, round)) : Wins(place.Id);
            return before == 0 ? crowns : crowns / RematchShare;
        }

        // The pool in the save's order (a shuffle by its seed): reloading never draws again
        private List<int> PoolOrder()
        {
            if (pool == null)
            {
                pool = new List<int>(Content.File.Pool);
                SeededRandom random = new SeededRandom(State.Seed);
                for (int i = pool.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    int swap = pool[i];
                    pool[i] = pool[j];
                    pool[j] = swap;
                }
            }
            return pool;
        }

        // The random cards still to be won
        public int PoolLeft { get { return Math.Max(0, Content.File.Pool.Count - State.Drawn); } }

        //----------------------------------------What happens----------------------------------------//
        // A battle won against a player (not a tournament's round: WinRound)
        public StoryReward Win(StoryPlace place)
        {
            if (place.Type != StoryPlaceKind.Player)
                throw new ArgumentException(place.Id + " is no player");
            return Changing(reward =>
            {
                Pay(CrownsFor(place), reward);
                if (Wins(place.Id) == 0)
                {
                    foreach (int card in PrizeOf(place))
                        Give(card, reward);
                    if (place.Prize.Count == 0 && place.Random && State.Drawn < Content.File.Pool.Count)
                        State.Drawn++;
                }
                Count(place.Id);
            });
        }

        // A card found, or an event's card or deck, taken (once)
        public StoryReward Take(StoryPlace place)
        {
            if (!place.Taken)
                throw new ArgumentException(place.Id + " is nothing to take");
            return Changing(reward =>
            {
                if (!taken.Add(place.Id))
                    return;
                State.Taken.Add(place.Id);
                foreach (int card in place.Prize.Concat(place.Gives))
                    Give(card, reward);
            });
        }

        public bool Bought(StoryPlace place, int sale)
        {
            return bought.Contains(place.Id + "#" + sale);
        }

        // Why a sale can't be bought (bought already, too few crowns), or null
        public string CannotBuy(StoryPlace place, int sale)
        {
            if (sale < 0 || sale >= place.Sells.Count)
                return "Not for sale";
            if (Bought(place, sale))
                return "Bought already";
            int price = place.Sells[sale].Price;
            return State.Crowns < price ? "You need " + price + " crowns (you have " + State.Crowns + ")" : null;
        }

        public StoryReward Buy(StoryPlace place, int sale)
        {
            string problem = CannotBuy(place, sale);
            if (problem != null)
                throw new InvalidOperationException(problem);
            return Changing(reward =>
            {
                State.Crowns -= place.Sells[sale].Price;
                reward.Crowns -= place.Sells[sale].Price;
                bought.Add(place.Id + "#" + sale);
                State.Bought.Add(place.Id + "#" + sale);
                Give(place.Sells[sale].Card, reward);
            });
        }

        //----------------------------------------Tournaments----------------------------------------//
        public static string RoundKey(StoryPlace tournament, StoryRound round)
        {
            return tournament.Id + "/" + round.Id;
        }

        public bool Entered(StoryPlace tournament)
        {
            return State.Entered == tournament.Id;
        }

        // The round to play next: of the run under way, else the first
        public StoryRound NextRound(StoryPlace tournament)
        {
            int round = Entered(tournament) ? State.Round : 0;
            return round < tournament.Rounds.Count ? tournament.Rounds[round] : null;
        }

        // Why the tournament can't be entered (another under way, too few crowns), or null
        public string CannotEnter(StoryPlace tournament)
        {
            if (Entered(tournament))
                return null;
            if (!string.IsNullOrEmpty(State.Entered))
                return "Finish " + Content.Place(State.Entered).Name + " first";
            return State.Crowns < tournament.Fee ? "The entry fee is " + tournament.Fee + " crowns (you have " + State.Crowns + ")" : null;
        }

        // Pays the fee and starts a run at its first round (nothing to do for a run under way)
        public void Enter(StoryPlace tournament)
        {
            if (Entered(tournament))
                return;
            string problem = CannotEnter(tournament);
            if (problem != null)
                throw new InvalidOperationException(problem);
            State.Crowns -= tournament.Fee;
            State.Entered = tournament.Id;
            State.Round = 0;
        }

        public StoryReward WinRound(StoryPlace tournament)
        {
            if (!Entered(tournament))
                throw new InvalidOperationException(tournament.Id + " not entered");
            StoryRound round = tournament.Rounds[State.Round];
            return Changing(reward =>
            {
                string key = RoundKey(tournament, round);
                Pay(CrownsFor(tournament, round), reward);
                if (Wins(key) == 0)
                    foreach (int card in round.Prize)
                        if (Owned(card) < db.Get(card).copies)
                            Give(card, reward);
                Count(key);
                State.Round++;
                if (State.Round >= tournament.Rounds.Count)
                {
                    State.Entered = "";
                    State.Round = 0;
                }
            });
        }

        // A round lost: the run ends (enter again, for the fee)
        public void LoseRound(StoryPlace tournament)
        {
            if (!Entered(tournament))
                return;
            State.Entered = "";
            State.Round = 0;
        }

        // Why a deck can't play there (Count Monnier's tournament takes Skellige decks only), or null
        public string CannotPlayWith(StoryPlace place, Deck deck)
        {
            Faction only = place.OnlyDecks;
            if (only != Faction.Neutral && deck.Faction != only)
                return place.Name + " takes " + only.DisplayName() + " decks only";
            return DeckProblem(deck);
        }

        //----------------------------------------Quests----------------------------------------//
        public int StepsDone(StoryQuest quest)
        {
            return quest.Steps.Count(step => IsDone(Content.Place(step)));
        }

        public bool QuestDone(StoryQuest quest)
        {
            return quest != null && quest.Steps.All(step => IsDone(Content.Place(step)));
        }

        //----------------------------------------The collection----------------------------------------//
        // The copies still to come from a region (unsold stock, prizes not won, cards not found, quests' rewards), by
        // card: The Witcher 3's A Miraculous Guide to Gwent
        public List<int> StillIn(StoryRegion region)
        {
            List<int> cards = new List<int>();
            foreach (StoryPlace place in Content.PlacesIn(region))
            {
                cards.AddRange(place.Sells.Where((sale, i) => !Bought(place, i)).Select(sale => sale.Card));
                if (place.Taken ? !taken.Contains(place.Id) : place.Type == StoryPlaceKind.Player && Wins(place.Id) == 0)
                    cards.AddRange(place.Prize.Concat(place.Gives));
                foreach (StoryRound round in place.Rounds)
                    if (Wins(RoundKey(place, round)) == 0)
                        cards.AddRange(round.Prize);
            }
            foreach (StoryQuest quest in Content.Quests)
                if (!quests.Contains(quest.Id) && quest.Steps.Count > 0 && Content.Place(quest.Steps[quest.Steps.Count - 1]).Region == region.Id)
                    cards.AddRange(quest.Reward);
            return cards;
        }

        //----------------------------------------Bookkeeping----------------------------------------//
        private void Count(string id)
        {
            wins[id] = Wins(id) + 1;
            StoryWins entry = State.Wins.FirstOrDefault(win => win.Place == id);
            if (entry == null)
                State.Wins.Add(new StoryWins { Place = id, Count = 1 });
            else
                entry.Count++;
        }

        // A change, then what it led to: the quests it completed (their rewards given), the regions it opened, the places
        // that appeared
        private StoryReward Changing(Action<StoryReward> change)
        {
            StoryReward reward = new StoryReward();
            HashSet<string> open = new HashSet<string>(Content.Regions.Where(IsOpen).Select(region => region.Id));
            HashSet<string> seen = new HashSet<string>(Content.Places.Where(IsVisible).Select(place => place.Id));
            change(reward);
            bool more = true;
            while (more) // a quest's reward could complete another (none does today)
            {
                more = false;
                foreach (StoryQuest quest in Content.Quests)
                    if (!quests.Contains(quest.Id) && QuestDone(quest))
                    {
                        quests.Add(quest.Id);
                        State.Quests.Add(quest.Id);
                        reward.Quests.Add(quest);
                        foreach (int card in quest.Reward)
                            Give(card, reward);
                        more = true;
                    }
            }
            reward.Regions.AddRange(Content.Regions.Where(region => !open.Contains(region.Id) && IsOpen(region)));
            reward.Revealed.AddRange(Content.Places.Where(place => !seen.Contains(place.Id) && IsVisible(place)));
            return reward;
        }

        // Moves the map to an open region
        public void Travel(StoryRegion region)
        {
            if (IsOpen(region))
                State.Region = region.Id;
        }

        public StoryRegion Current
        {
            get
            {
                StoryRegion region = Content.Region(State.Region);
                return IsOpen(region) ? region : Content.First;
            }
        }
    }
}
