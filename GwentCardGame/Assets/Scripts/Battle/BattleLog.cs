using System.Collections.Generic;
using System.Diagnostics;
using Gwent.Core;
using Gwent.Rules;

namespace Gwent.Battle
{
    // The battle's part of the game log (GameLog): its start, each round and turn (a turn starts with a snapshot of the
    // whole board), every play with what it changed, the passes, the rounds' results and the end. A play's effects are
    // what moved between the zones (hands, decks, rows, horns, discard piles, weather) from before it to after it, so
    // scorch, muster, spies, medics and decoys all show without the rules knowing about the log:
    //
    //   "effects":{"enemy.hand":{"out":["Arachas 1"]},"enemy.close":{"in":["Arachas 1","Arachas 2"]},"enemy.deck":-1}
    //
    // Compiled out of release builds, like GameLog.
    public static class BattleLog
    {
        private static int battles, round, turn;
        private static Dictionary<string, List<string>> before;

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void Begin(BattleController c, Deck mine, Deck theirs, string opponent)
        {
            battles++;
            round = turn = 0;
            GameLog.SetContext("battle", battles);
            GameLog.SetContext("round", null);
            GameLog.SetContext("turn", null);
            GameLog.Event(LogCategory.Battle, "battle_start",
                "player_deck", mine.Name, "player_faction", mine.Faction, "player_leader", c.Db.Get(mine.Leader).name,
                "opponent_deck", opponent, "opponent_faction", theirs.Faction, "opponent_leader", c.Db.Get(theirs.Leader).name,
                "difficulty", BattleController.Instance != null ? BattleController.Instance.AiDifficulty : GameSettings.AiDifficulty, "double_spies", c.doubleSpies, "random_medic", c.randomMedic);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void Redraw(BattleController c, bool is_player, int card_out, int card_in)
        {
            GameLog.Event(LogCategory.Battle, "redraw", "side", Side(is_player), "out", c.Db.Get(card_out).name, "in", card_in >= 0 ? c.Db.Get(card_in).name : null);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void RoundStart(bool player_starts)
        {
            round++;
            turn = 0;
            GameLog.SetContext("round", round);
            GameLog.SetContext("turn", null);
            GameLog.Event(LogCategory.Battle, "round_start", "first", Side(player_starts));
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void TurnStart(BattleController c, bool is_player)
        {
            turn++;
            GameLog.SetContext("turn", turn);
            GameLog.Event(LogCategory.Battle, "turn_start", "side", Side(is_player), "board", Snapshot(c));
        }

        // Before the rules change the board (a play, a leader, a picked card): what the next event compares with
        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void BeforeChange(BattleController c)
        {
            before = Zones(c);
        }

        // A card played ("card_played", "decoy_played"), once the rules resolved it
        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void Played(BattleController c, string what, bool is_player, int card_id, Row row)
        {
            Card card = c.Db.Get(card_id);
            GameLog.Event(LogCategory.Battle, what, "side", Side(is_player), "card", card.name, "id", card_id, "row", row,
                "ability", card.ability, "effects", Effects(c), "scores", Scores(c));
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void LeaderUsed(BattleController c, bool is_player, int leader_id, string outcome)
        {
            GameLog.Event(LogCategory.Battle, "leader_used", "side", Side(is_player), "leader", c.Db.Get(leader_id).name,
                "outcome", outcome, "effects", Effects(c), "scores", Scores(c));
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void Passed(BattleController c, bool is_player, string why)
        {
            GameLog.Event(LogCategory.Battle, "pass", "side", Side(is_player), "why", why, "scores", Scores(c));
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void RoundEnd(BattleController c, RoundWinner winner, int player_total, int enemy_total)
        {
            GameLog.Event(LogCategory.Battle, "round_end", "winner", winner, "player_total", player_total, "enemy_total", enemy_total,
                "player_lives", c.Logged(true).Lives, "enemy_lives", c.Logged(false).Lives);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void End(BattleState outcome, bool gave_up)
        {
            GameLog.Event(LogCategory.Battle, "battle_end", "outcome", outcome, "gave_up", gave_up, "rounds", round);
            GameLog.SetContext("battle", null);
            GameLog.SetContext("round", null);
            GameLog.SetContext("turn", null);
        }

        // The battle's scene closed before its end (the menu, a restart)
        [Conditional("UNITY_EDITOR"), Conditional("DEBUG"), Conditional("GWENT_VERBOSE_LOGS")]
        public static void Left()
        {
            GameLog.Event(LogCategory.Battle, "battle_left", "rounds", round);
            GameLog.SetContext("battle", null);
            GameLog.SetContext("round", null);
            GameLog.SetContext("turn", null);
        }

        //----------------------------------------The board----------------------------------------//
        private static string Side(bool is_player) { return is_player ? "player" : "enemy"; }

        private static Dictionary<string, object> Scores(BattleController c)
        {
            return new Dictionary<string, object> { { "player", c.Logged(true).TotalScore() }, { "enemy", c.Logged(false).TotalScore() } };
        }

        // Both sides as they stand: scores, lives, the leader, the cards in hand and on the rows (with their strength)
        private static Dictionary<string, object> Snapshot(BattleController c)
        {
            return new Dictionary<string, object>
            {
                { "player", SideState(c, c.Logged(true)) },
                { "enemy", SideState(c, c.Logged(false)) },
                { "weather", Names(c, c.LoggedWeather) },
            };
        }

        private static Dictionary<string, object> SideState(BattleController c, PlayerState side)
        {
            Dictionary<string, object> state = new Dictionary<string, object>
            {
                { "score", side.TotalScore() },
                { "lives", side.Lives },
                { "passed", side.hasPassed },
                { "leader", c.Db.Get(side.Leader).name },
                { "leader_ready", side.canLeader && !side.leaderDisabled },
                { "hand", Names(c, side.HandList) },
                { "deck", side.DeckList.Count },
                { "discard", side.DiscardList.Count },
            };
            foreach (Row row in Rows.Units)
            {
                List<int> units = side.UnitRow(row), strengths = side.ModifiedStrengths(row);
                List<string> shown = new List<string>();
                for (int i = 0; i < units.Count; i++)
                    shown.Add(c.Db.Get(units[i]).name + (i < strengths.Count ? ":" + strengths[i] : ""));
                state[row.ToString().ToLowerInvariant()] = shown;
                if (side.HasHorn(row))
                    state[row.ToString().ToLowerInvariant() + "_horn"] = true;
            }
            return state;
        }

        private static List<string> Names(BattleController c, IEnumerable<int> card_ids)
        {
            List<string> names = new List<string>();
            foreach (int id in card_ids)
                names.Add(c.Db.Get(id).name);
            return names;
        }

        // Every zone's cards by name (the decks by count: what is drawn matters, not the order left)
        private static Dictionary<string, List<string>> Zones(BattleController c)
        {
            Dictionary<string, List<string>> zones = new Dictionary<string, List<string>>();
            foreach (bool is_player in new[] { true, false })
            {
                PlayerState side = is_player ? c.Logged(true) : c.Logged(false);
                string who = Side(is_player) + ".";
                zones[who + "hand"] = Names(c, side.HandList);
                zones[who + "discard"] = Names(c, side.DiscardList);
                zones[who + "deck"] = new List<string> { side.DeckList.Count.ToString() };
                foreach (Row row in Rows.Units)
                {
                    zones[who + row.ToString().ToLowerInvariant()] = Names(c, side.UnitRow(row));
                    zones[who + row.ToString().ToLowerInvariant() + "_horn"] = Names(c, side.SpecialRow(row));
                }
            }
            zones["weather"] = Names(c, c.LoggedWeather);
            return zones;
        }

        private static Dictionary<string, object> Effects(BattleController c)
        {
            Dictionary<string, object> effects = new Dictionary<string, object>();
            if (before == null)
                return effects;
            foreach (KeyValuePair<string, List<string>> zone in Zones(c))
            {
                List<string> was;
                if (!before.TryGetValue(zone.Key, out was))
                    continue;
                if (zone.Key.EndsWith(".deck"))
                {
                    int change = int.Parse(zone.Value[0]) - int.Parse(was[0]);
                    if (change != 0)
                        effects[zone.Key] = change;
                    continue;
                }
                List<string> came = Minus(zone.Value, was), went = Minus(was, zone.Value);
                if (came.Count == 0 && went.Count == 0)
                    continue;
                Dictionary<string, object> moved = new Dictionary<string, object>();
                if (came.Count > 0)
                    moved["in"] = came;
                if (went.Count > 0)
                    moved["out"] = went;
                effects[zone.Key] = moved;
            }
            before = null;
            return effects;
        }

        // The cards of a that are not in b (copies counted)
        private static List<string> Minus(List<string> a, List<string> b)
        {
            List<string> left = new List<string>(b), result = new List<string>();
            foreach (string name in a)
                if (!left.Remove(name))
                    result.Add(name);
            return result;
        }
    }
}
