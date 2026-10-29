using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace Gwent.Rules.Tests
{
    // The difficulties on the AI's own decks (Resources/Decks/AI), whole battles played by BattleSim: each level beats
    // the one below it, no card is lost or made along the way, and every preset's leader comes into play.
    public class AiDifficultyTests
    {
        private const string PresetFolder = "Assets/Resources/Decks/AI";

        private static CardDatabase Db { get { return TestBoard.Db; } }

        private static List<Deck> Presets()
        {
            return Directory.GetFiles(PresetFolder, "*.json").OrderBy(path => path)
                .Select(path => JsonUtility.FromJson<DeckFile>(File.ReadAllText(path)).ToDeck()).ToList();
        }

        [Test]
        public void PresetsAreValidDecks()
        {
            List<Deck> presets = Presets();
            Assert.That(presets.Count, Is.GreaterThanOrEqualTo(8));
            foreach (Deck deck in presets)
                Assert.IsTrue(DeckRules.IsValid(deck, Db) && LeaderIds.BelongsTo(deck.Leader, deck.Faction), deck.Name);
        }

        [Test]
        public void NormalBeatsEasy()
        {
            int[] results = Tournament(AiDifficulty.Normal, AiDifficulty.Easy, 1);
            Assert.Greater(results[0], 2 * results[1], "Normal's wins, Easy's: " + results[0] + ", " + results[1]);
        }

        // With a third of its look-ahead (8 battles played out per move instead of 24), to keep the test short: it is
        // stronger than Normal from there on
        [Test]
        public void HardBeatsNormal()
        {
            int limit = AiSearch.TimeLimitMs, rollouts = AiSearch.Rollouts;
            AiSearch.TimeLimitMs = int.MaxValue; // every battle it plays out, however slow the machine: the same result
            AiSearch.Rollouts = 8;
            try
            {
                int[] results = Tournament(AiDifficulty.Hard, AiDifficulty.Normal, 0, 4);
                Assert.Greater(results[0], results[1], "Hard's wins, Normal's: " + results[0] + ", " + results[1]);
            }
            finally
            {
                AiSearch.TimeLimitMs = limit;
                AiSearch.Rollouts = rollouts;
            }
        }

        // An active leader in a preset deck is there to be used: its AI uses it in a good share of its battles
        [Test]
        public void EveryPresetsLeaderComesIntoPlay()
        {
            List<Deck> presets = Presets();
            foreach (Deck deck in presets)
            {
                if (Db.Get(deck.Leader).row != Row.LeaderActive)
                    continue; // passive: always at work
                int battles = 0, used = 0;
                foreach (Deck opponent in presets.Where(other => other != deck))
                    for (int i = 0; i < 4; i++)
                    {
                        bool leader_used = false;
                        Func<Board, bool, AiAction> watch = (board, is_player) =>
                        {
                            AiAction action = Decide(board, is_player, AiDifficulty.Normal);
                            leader_used |= is_player && action.Type == AiActionType.UseLeader;
                            return action;
                        };
                        PlayBattle(deck, opponent, watch, 500 + battles);
                        battles++;
                        if (leader_used)
                            used++;
                    }
                Assert.GreaterOrEqual(used * 10, battles, deck.Name + ": " + Db.Get(deck.Leader).name + " used in " + used + " of " + battles + " battles");
            }
        }

        //----------------------------------------Battles----------------------------------------//
        // Every preset against every preset (every other one: skip > 0), the two levels taking turns in the seats;
        // returns the first level's wins, the second's, the draws
        private static int[] Tournament(AiDifficulty first, AiDifficulty second, int repeats, int skip = 0)
        {
            List<Deck> presets = Presets();
            int[] results = new int[3];
            int game = 0;
            for (int r = 0; r <= repeats; r++)
                foreach (Deck a in presets)
                    foreach (Deck b in presets)
                    {
                        int index = game++;
                        if (skip > 0 && index % skip != 0)
                            continue;
                        bool first_is_player = index / Math.Max(1, skip) % 2 == 0;
                        Func<Board, bool, AiAction> levels = (board, is_player) => Decide(board, is_player, is_player == first_is_player ? first : second);
                        RoundWinner winner = PlayBattle(a, b, levels, 1000 + index);
                        if (winner == RoundWinner.Draw)
                            results[2]++;
                        else
                            results[(winner == RoundWinner.Player) == first_is_player ? 0 : 1]++;
                    }
            return results;
        }

        internal static AiAction Decide(Board board, bool is_player, AiDifficulty difficulty)
        {
            AiContext ctx = board.AiContextFor(is_player);
            ctx.Difficulty = difficulty;
            ctx.Seed = board.Rnd.Next();
            return AiBrain.Decide(ctx);
        }

        // A battle from the deal (BattleController.Start): shuffled decks, 10 cards (11 under Francesca, Daisy of the
        // Valley), the White Flame's cancel, Scoia'tael going first against another faction
        internal static RoundWinner PlayBattle(Deck player_deck, Deck enemy_deck, Func<Board, bool, AiAction> policy, int seed)
        {
            Rng.Seed(seed);
            Random rnd = new Random(seed);
            Board board = new Board
            {
                Player = new PlayerState { Faction = player_deck.Faction, Leader = player_deck.Leader },
                Enemy = new PlayerState { Faction = enemy_deck.Faction, Leader = enemy_deck.Leader },
                Db = Db,
                Rnd = rnd,
            };
            board.RandomMedic = player_deck.Leader == LeaderIds.EmhyrInvaderOfTheNorth || enemy_deck.Leader == LeaderIds.EmhyrInvaderOfTheNorth;
            board.DoubleSpies = player_deck.Leader == LeaderIds.EredinTheTreacherous || enemy_deck.Leader == LeaderIds.EredinTheTreacherous;
            foreach (bool is_player in new[] { true, false })
            {
                Deck deck = is_player ? player_deck : enemy_deck;
                PlayerState side = board.Side(is_player);
                side.DeckList = Rng.Shuffle(deck.Cards);
                RoundRules.Draw(side, deck.Leader == LeaderIds.FrancescaDaisyOfTheValley ? 11 : 10);
                if (deck.Leader == LeaderIds.EmhyrTheWhiteFlame)
                    board.Opponent(is_player).leaderDisabled = true;
            }
            int cards = CountCards(board);

            bool player_sc = player_deck.Faction == Faction.Scoiatael, enemy_sc = enemy_deck.Faction == Faction.Scoiatael;
            bool player_first = player_sc != enemy_sc ? player_sc : rnd.Next(2) == 0;
            RoundWinner winner = BattleSim.PlayOut(board, player_first, false, policy);

            Assert.AreEqual(cards, CountCards(board), "no card lost or made: " + player_deck.Name + " vs " + enemy_deck.Name + " (seed " + seed + ")");
            Assert.IsTrue(board.Player.Lives == 0 || board.Enemy.Lives == 0, "the battle ended: " + player_deck.Name + " vs " + enemy_deck.Name + " (seed " + seed + ")");
            return winner;
        }

        private static int CountCards(Board board)
        {
            return new[] { board.Player, board.Enemy }.Sum(p => p.AllCards().Count(id => !CardIds.IsSummoned(id)) - p.LeaderHornRows.Count);
        }
    }
}
