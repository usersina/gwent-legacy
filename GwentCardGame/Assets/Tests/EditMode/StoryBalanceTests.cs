using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Gwent.Rules.Tests
{
    // How hard the story is, measured (a report, not a check: run it by name). The story is played through the way a
    // player would, the easiest opponent left first, buying what can be bought; before each battle the player's deck is
    // the strongest that the cards owned make (StrongestDeck), played by the Normal AI as a stand-in for the player,
    // against the opponent's own deck at its own level; whole battles by BattleSim, as AiDifficultyTests plays them.
    // Writes Logs/story-balance.txt: each opponent's win rate, then each region's and level's.
    public class StoryBalanceTests
    {
        private const string StoryPath = "Assets/Resources/Story/story.json";
        private const string ReportPath = "Logs/story-balance.txt";
        private const int Battles = 8;           // battles against each opponent (each round of a tournament)
        private const int UnitsKept = 24;        // a deck's units: its strongest, at least DeckRules.MinUnits
        private const AiDifficulty Player = AiDifficulty.Normal;

        private static CardDatabase Db { get { return TestBoard.Db; } }

        private class Result
        {
            public string Region, Name;
            public int Tier;
            public double Won;
        }

        [Test, Explicit("A report of minutes: run it by name"), Timeout(1800000)]
        public void HowHardTheStoryIs()
        {
            StoryContent content = new StoryContent(JsonUtility.FromJson<StoryFile>(File.ReadAllText(StoryPath)));
            StoryProgress story = new StoryProgress(content, StoryProgress.NewState(content, 7), Db);
            List<string> lines = new List<string>();
            List<Result> results = new List<Result>();
            int limit = AiSearch.TimeLimitMs, rollouts = AiSearch.Rollouts;
            AiSearch.TimeLimitMs = int.MaxValue; // the same result however slow the machine (Hard: a third of its look-ahead)
            AiSearch.Rollouts = 8;
            try
            {
                for (int step = 0; step < 1000; step++)
                {
                    Shop(story, content);
                    StoryPlace next = content.Places.Where(story.IsVisible)
                        .Where(place => (place.Type == StoryPlaceKind.Player && story.Wins(place.Id) == 0)
                                     || (place.Type == StoryPlaceKind.Tournament && !story.IsDone(place) && story.CannotEnter(place) == null))
                        .OrderBy(place => place.Tier).FirstOrDefault();
                    if (next == null)
                    {
                        bool waiting = content.Places.Where(story.IsVisible).Any(place => story.HasMore(place) || (place.Type == StoryPlaceKind.Tournament && !story.IsDone(place)));
                        if (!waiting)
                            break;
                        story.Win(content.Places.First(place => place.Type == StoryPlaceKind.Player && story.IsVisible(place))); // a rematch, for crowns
                        continue;
                    }
                    if (next.Type == StoryPlaceKind.Player)
                    {
                        results.Add(Measure(story, next, null, step, lines));
                        story.Win(next);
                        continue;
                    }
                    story.Enter(next);
                    for (int round = 0; story.Entered(next); round++)
                    {
                        results.Add(Measure(story, next, story.NextRound(next), step * 10 + round, lines));
                        story.WinRound(next);
                    }
                }
            }
            finally
            {
                AiSearch.TimeLimitMs = limit;
                AiSearch.Rollouts = rollouts;
            }

            lines.Add("");
            lines.Add("By region and level (the stand-in's wins, average and lowest):");
            foreach (IGrouping<string, Result> group in results.GroupBy(result => result.Region + " tier " + result.Tier))
                lines.Add(string.Format("  {0,-26} {1,3} battles  {2,4:P0}  lowest {3,4:P0} ({4})", group.Key, group.Count(),
                    group.Average(result => result.Won), group.Min(result => result.Won), group.OrderBy(result => result.Won).First().Name));
            File.WriteAllLines(ReportPath, lines);
            TestContext.WriteLine(string.Join("\n", lines));
            Assert.That(story.CollectionComplete, "the story played through");
        }

        // What a player does between battles: takes what is found or given, buys what they can
        private static void Shop(StoryProgress story, StoryContent content)
        {
            foreach (StoryPlace place in content.Places.Where(story.IsVisible).ToList())
            {
                if (place.Taken && !story.IsDone(place))
                    story.Take(place);
                for (int sale = 0; sale < place.Sells.Count; sale++)
                    if (story.CannotBuy(place, sale) == null)
                        story.Buy(place, sale);
            }
        }

        // The player's strongest deck against the opponent's own, Battles times
        private static Result Measure(StoryProgress story, StoryPlace place, StoryRound round, int step, List<string> lines)
        {
            Deck mine = StrongestDeck(story, place.OnlyDecks);
            Deck theirs = DeckGenerator.For(place, round, Db);
            double won = 0;
            for (int i = 0; i < Battles; i++)
            {
                RoundWinner winner = AiDifficultyTests.PlayBattle(mine, theirs,
                    (board, is_player) => AiDifficultyTests.Decide(board, is_player, is_player ? Player : place.Difficulty), 7000 + step * 100 + i);
                won += winner == RoundWinner.Player ? 1 : winner == RoundWinner.Draw ? 0.5 : 0;
            }
            string name = round != null ? place.Name + ": " + round.Name : place.Name;
            Result result = new Result { Region = place.Region, Name = name, Tier = place.Tier, Won = won / Battles };
            lines.Add(string.Format("{0,-12} t{1} {2,-6} {3,-44} you {4} {5,3} ({6,3} cards owned)  them {7} {8,3}  won {9,4:P0}",
                place.Region, place.Tier, place.Difficulty, Cut(name, 44), mine.Faction.Code(), Strength(mine),
                story.State.Cards.Count, theirs.Faction.Code(), Strength(theirs), result.Won));
            return result;
        }

        // The deck a player would make from the cards they own: for each faction they have a leader of, its units and the
        // neutral ones, the strongest kept (spies and medics counted higher), and the useful specials; the strongest deck
        private static Deck StrongestDeck(StoryProgress story, Faction only)
        {
            Deck best = null;
            int best_strength = int.MinValue;
            List<string> problems = new List<string>();
            foreach (Faction faction in story.OpenFactions)
            {
                if (only != Faction.Neutral && faction != only)
                    continue;
                List<Card> units = new List<Card>(), specials = new List<Card>();
                foreach (Card card in Db.All)
                {
                    if (card.IsLeader || !(card.IsSpecial || card.faction == faction || card.faction == Faction.Neutral))
                        continue;
                    for (int i = 0; i < story.Copies(card); i++)
                        (card.IsSpecial ? specials : units).Add(card);
                }
                if (units.Count < DeckRules.MinUnits)
                {
                    problems.Add(faction.Code() + ": " + units.Count + " units");
                    continue;
                }
                List<int> cards = units.OrderByDescending(Value).Take(Math.Min(units.Count, UnitsKept)).Select(card => card._id).ToList();
                cards.AddRange(specials.GroupBy(card => card.ability).OrderBy(kind => SpecialRank(kind.Key))
                    .SelectMany(kind => kind.Take(SpecialsKept(kind.Key))).Take(DeckRules.MaxSpecials).Select(card => card._id));
                int leader = LeaderIds.ByFaction[faction].First(id => story.Owned(id) > 0);
                Deck deck = new Deck { Faction = faction, Leader = leader, Cards = cards, Name = "Strongest " + faction.Code() };
                string problem = story.DeckProblem(deck);
                if (problem != null)
                {
                    problems.Add(faction.Code() + ": " + problem);
                    continue;
                }
                int strength = Strength(deck);
                if (strength > best_strength)
                {
                    best = deck;
                    best_strength = strength;
                }
            }
            Assert.That(best, Is.Not.Null, "a deck can be made: " + string.Join("; ", problems));
            return best;
        }

        private static int Value(Card card)
        {
            return card.strength + (card.ability == Ability.Spy ? 10 : card.ability == Ability.Medic ? 4 : 0) + (card.unique ? 3 : 0);
        }

        // Decoys first, then scorch, horns, clear weather, the weathers
        private static int SpecialRank(Ability ability)
        {
            switch (ability)
            {
                case Ability.Decoy: return 0;
                case Ability.Scorch: return 1;
                case Ability.CommanderHorn:
                case Ability.UnitHorn: return 2;
                case Ability.ClearWeather: return 3;
                default: return 4;
            }
        }

        private static int SpecialsKept(Ability ability)
        {
            switch (ability)
            {
                case Ability.Decoy: return 3;
                case Ability.Scorch:
                case Ability.CommanderHorn:
                case Ability.UnitHorn: return 2;
                case Ability.ClearWeather:
                case Ability.ReduceClose:
                case Ability.ReduceRange:
                case Ability.ReduceSiege:
                case Ability.ReduceRangeSiege:
                case Ability.Mardroeme: return 1;
                default: return 0;
            }
        }

        // The units' strength, spies left out (they count for the other side)
        private static int Strength(Deck deck)
        {
            return deck.Cards.Select(id => Db.Get(id)).Where(card => card.IsUnit && card.ability != Ability.Spy).Sum(card => card.strength);
        }

        private static string Cut(string text, int length)
        {
            return text.Length <= length ? text : text.Substring(0, length - 1) + "…";
        }
    }
}
