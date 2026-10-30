using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Gwent.Rules
{
    public static class DeckRules
    {
        public const int MinUnits = 22;
        public const int MaxSpecials = 10;
        public const int MaxNameLength = 30;
        public const string EmptyDeckName = "__emptyname__";

        //----------------------------------------Validation----------------------------------------//
        public static List<string> Validate(Deck deck, CardDatabase db)
        {
            List<string> errors = new List<string>();
            if (deck == null)
            {
                errors.Add("Deck could not be read");
                return errors;
            }
            if (Array.IndexOf(Factions.Playable, deck.Faction) < 0)
                errors.Add("Unknown faction '" + deck.Faction.Code() + "'");
            if (!LeaderIds.BelongsTo(deck.Leader, deck.Faction))
                errors.Add("Leader does not belong to the deck's faction");

            int units = 0;
            int specials = 0;
            foreach (IGrouping<int, int> group in (deck.Cards ?? new List<int>()).GroupBy(x => x))
            {
                Card card;
                if (!db.TryGet(group.Key, out card) || card.IsLeader)
                {
                    errors.Add("Unknown card id " + group.Key);
                    continue;
                }
                int copies = group.Count();
                if (card.IsSpecial)
                    specials += copies;
                else
                    units += copies;
                if (!Fits(card, deck.Faction))
                    errors.Add(card.DisplayName + " does not belong to this faction");
                if (copies > MaxCopies(card))
                    errors.Add(TooManyCopies(card));
            }
            if (units < MinUnits)
                errors.Add("Needs at least " + MinUnits + " unit cards (has " + units + ")");
            if (specials > MaxSpecials)
                errors.Add("At most " + MaxSpecials + " special cards (has " + specials + ")");
            return errors;
        }

        public static bool IsValid(Deck deck, CardDatabase db)
        {
            return Validate(deck, db).Count == 0;
        }

        // Whether a card can go in a deck of the faction: its own units, the neutral ones and the special cards, but
        // Mardroeme, a special card, only in Skellige's
        public static bool Fits(Card card, Faction faction)
        {
            if (card._id == CardIds.Mardroeme)
                return faction == Faction.Skellige;
            return card.IsSpecial || card.faction == Faction.Neutral || card.faction == faction;
        }

        // As in The Witcher 3, a deck holds at most the copies of a card that exist in the game: one of a hero or of a
        // single card such as Dandelion, up to five Mahakaman Defenders. The summoned cards (the Bovine Defense Force,
        // Hemdall, the Berserkers' bears) have none.
        public static int MaxCopies(Card card)
        {
            return card.unique ? Math.Min(1, card.copies) : card.copies;
        }

        public static string TooManyCopies(Card card)
        {
            string name = card.DisplayName;
            int max = MaxCopies(card);
            if (max == 0)
                return name + " can't be put in a deck";
            if (card.unique)
                return name + " is a hero: only one copy per deck";
            return max == 1 ? "There is only one " + name : "There are only " + max + " copies of " + name;
        }

        // Drops unknown card ids and copies beyond what exists, and fixes an invalid leader, so a hand-edited deck
        // (or one saved before the copy limits) can still be loaded. With a supply, copies beyond it too (a story deck
        // keeps the copies owned) and a leader not owned (the faction's first owned one takes its place).
        public static Deck Sanitize(Deck deck, CardDatabase db, ICardSupply supply = null)
        {
            if (deck == null)
                return null;
            supply = supply ?? AllCards.Instance;
            Dictionary<int, int> kept = new Dictionary<int, int>();
            deck.Cards = (deck.Cards ?? new List<int>()).Where(id =>
            {
                Card card;
                if (!db.TryGet(id, out card) || card.IsLeader)
                    return false;
                int count;
                kept.TryGetValue(id, out count);
                if (count >= Math.Min(MaxCopies(card), supply.Copies(card)))
                    return false;
                kept[id] = count + 1;
                return true;
            }).ToList();
            if (Array.IndexOf(Factions.Playable, deck.Faction) >= 0 && !LeaderIds.BelongsTo(deck.Leader, deck.Faction))
                deck.Leader = LeaderIds.DefaultFor(deck.Faction);
            if (Array.IndexOf(Factions.Playable, deck.Faction) >= 0 && supply.Copies(db.Get(deck.Leader)) == 0)
                deck.Leader = DefaultLeader(deck.Faction, db, supply);
            return deck;
        }

        // The faction's first leader the supply has (its first of all when it has none)
        public static int DefaultLeader(Faction faction, CardDatabase db, ICardSupply supply = null)
        {
            int[] leaders;
            if (supply != null && LeaderIds.ByFaction.TryGetValue(faction, out leaders))
                foreach (int leader in leaders)
                    if (supply.Copies(db.Get(leader)) > 0)
                        return leader;
            return LeaderIds.DefaultFor(faction);
        }

        //----------------------------------------Deck names----------------------------------------//
        // "  My   Deck " => "My Deck" (control characters removed, length capped)
        public static string NormalizeDisplayName(string text)
        {
            if (text == null)
                return "";
            StringBuilder builder = new StringBuilder();
            foreach (char c in text)
                if (!char.IsControl(c))
                    builder.Append(c);
            string[] words = builder.ToString().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string name = string.Join(" ", words);
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength).TrimEnd() : name;
        }

        // The name of a deck's nth copy ("The Monster Horde (copy)", then "(copy 2)"...): the deck's own name is shortened,
        // never the suffix, so the whole fits MaxNameLength and the copies' names (and files) stay apart
        public static string CopyName(string name, int n)
        {
            string suffix = n <= 1 ? " (copy)" : " (copy " + n + ")";
            string own = NormalizeDisplayName(name);
            if (own.Length + suffix.Length > MaxNameLength)
                own = own.Substring(0, Math.Max(0, MaxNameLength - suffix.Length)).TrimEnd();
            return own + suffix;
        }

        // Display name => safe file name without extension ("My Deck!" => "my_deck"), null if nothing usable remains
        public static string ToFileName(string display_name)
        {
            string name = NormalizeDisplayName(display_name).ToLowerInvariant();
            StringBuilder builder = new StringBuilder();
            foreach (char c in name)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
                    builder.Append(c);
                else if (c == ' ' || c == '_')
                    builder.Append('_');
            }
            // "__empty__" / "__emptyname__" are reserved for the built-in empty deck
            if (builder.ToString().StartsWith("__", StringComparison.Ordinal))
                return null;
            string file_name = builder.ToString().Trim('_');
            return file_name.Length == 0 ? null : file_name;
        }

        // Two decks clash when they would share a file or only differ by letter case
        public static bool NamesCollide(string first, string second)
        {
            if (string.Equals(NormalizeDisplayName(first), NormalizeDisplayName(second), StringComparison.OrdinalIgnoreCase))
                return true;
            string first_file = ToFileName(first);
            return first_file != null && first_file == ToFileName(second);
        }
    }
}
