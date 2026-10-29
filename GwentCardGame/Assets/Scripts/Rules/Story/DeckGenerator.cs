using System.Collections.Generic;
using System.Linq;

namespace Gwent.Rules
{
    // The story's opponents' decks, made from their faction (The Witcher 3's, from the wiki's Gwent players) and their
    // tier, as The Witcher 3's players get stronger along the story: White Orchard's and Velen's play plain units and a few
    // weather cards, no heroes; the renowned players of Novigrad and Skellige, and the tournaments, strong units, heroes,
    // spies and medics, decoys and horns. Their prize card is in their deck where it fits (the card you see played is
    // the one you win). The same player always gets the same deck: it comes from their id. Always valid (DeckRules).
    public static class DeckGenerator
    {
        private static readonly int[] UnitTarget = { 22, 22, 22, 24 };      // by tier
        private static readonly int[] SpecialTarget = { 3, 3, 4, 6 };
        private static readonly int[] HeroLimit = { 0, 0, 1, 3 };

        // A player's deck, or a tournament round's
        public static Deck For(StoryPlace place, StoryRound round, CardDatabase db)
        {
            if (round != null)
                return Make(StoryProgress.RoundKey(place, round), round.DeckFaction, place.Tier, round.Leader, round.Prize, db, round.Name);
            return Make(place.Id, place.DeckFaction, place.Tier, place.Leader, place.Prize, db, place.Name);
        }

        public static Deck Make(string key, Faction faction, int tier, int leader, IEnumerable<int> featured, CardDatabase db, string name = null)
        {
            tier = System.Math.Max(1, System.Math.Min(3, tier));
            SeededRandom random = new SeededRandom(Hash(key));
            int[] leaders = LeaderIds.ByFaction[faction];
            Deck deck = new Deck
            {
                Faction = faction,
                Name = name ?? key,
                Leader = LeaderIds.BelongsTo(leader, faction) ? leader : leaders[random.Next(leaders.Length)],
            };
            Dictionary<int, int> count = new Dictionary<int, int>();
            int units = 0, specials = 0, heroes = 0;
            System.Func<Card, bool> room = card => Have(count, card) < DeckRules.MaxCopies(card);
            System.Action<Card> add = card =>
            {
                count[card._id] = Have(count, card) + 1;
                deck.Cards.Add(card._id);
                if (card.IsSpecial)
                    specials++;
                else
                {
                    units++;
                    if (card.unique)
                        heroes++;
                }
            };

            // Their prize first, if it can go in their deck
            foreach (int id in featured ?? Enumerable.Empty<int>())
            {
                Card card;
                if (db.TryGet(id, out card) && !card.IsLeader && card.copies > 0 && DeckRules.Fits(card, faction) && room(card))
                    add(card);
            }

            // Units by their worth: the weakest first in the first tier, the strongest in the last, with chance between;
            // a muster or bonded unit with all its copies (as The Witcher 3's decks hold them)
            List<Card> pool = db.All.Where(card => card.IsUnit && card.copies > 0 && DeckRules.Fits(card, faction)).ToList();
            List<Card> order = pool.OrderBy(card => Rank(card, tier, random)).ToList();
            for (int pass = 0; pass < 2 && units < UnitTarget[tier]; pass++)
                foreach (Card card in order)
                {
                    if (units >= UnitTarget[tier])
                        break;
                    if (card.unique && heroes >= HeroLimit[tier] && pass == 0)
                        continue; // the second pass takes anything, so a faction short of plain units still makes 22
                    bool set = card.ability == Ability.Muster || card.ability == Ability.TightBond;
                    while (room(card) && units < UnitTarget[tier] + (set ? 2 : 0))
                    {
                        add(card);
                        if (!set)
                            break;
                    }
                }

            // Special cards: weather first, then (from the second tier) decoys, horns and scorch
            List<Card> special = db.All.Where(card => card.IsSpecial && card.copies > 0 && DeckRules.Fits(card, faction))
                .OrderBy(card => (IsWeather(card) ? 0 : tier == 1 ? 20 : 5) + random.Next(10)).ToList();
            foreach (Card card in special)
                for (int copy = 0; copy < (tier >= 3 ? 2 : 1) && specials < SpecialTarget[tier] && room(card); copy++)
                    add(card);
            return deck;
        }

        private static bool IsWeather(Card card)
        {
            return card.row == Row.Weather;
        }

        private static int Have(Dictionary<int, int> count, Card card)
        {
            int n;
            return count.TryGetValue(card._id, out n) ? n : 0;
        }

        // A unit's worth: its strength, more for what its ability does
        private static int Worth(Card card)
        {
            int worth = card.strength + (card.unique ? 4 : 0);
            switch (card.ability)
            {
                case Ability.Spy: return worth + 8;
                case Ability.Medic: return worth + 6;
                case Ability.UnitHorn:
                case Ability.ScorchClose:
                case Ability.ScorchRange:
                case Ability.ScorchSiege:
                case Ability.Scorch: return worth + 5;
                case Ability.Muster:
                case Ability.TightBond:
                case Ability.MoraleBoost:
                case Ability.Berserker: return worth + 3;
                default: return worth;
            }
        }

        // The order units are taken in (lowest first)
        private static int Rank(Card card, int tier, SeededRandom random)
        {
            int noise = random.Next(8);
            switch (tier)
            {
                case 1: return Worth(card) * 2 + noise;         // the plainest first
                case 2: return noise * 3 - Worth(card) / 3;     // mostly chance
                default: return noise - Worth(card) * 2;        // the strongest first
            }
        }

        // FNV-1a: the same number for the same id on every platform (string.GetHashCode is not)
        private static int Hash(string key)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in key)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }
    }
}
