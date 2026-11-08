using System.Collections.Generic;
using Gwent.Core;
using Gwent.Rules;

namespace Gwent.Battle
{
    // What a card's description box says (DescriptionBox shows it), as The Witcher 3's: the ability's name and icon, then
    // what it does. A special card's box is the card's own; a leader's is its "Leader Ability", a hero's says what a hero
    // is (neither has an icon); a plain unit has none.
    //
    // The texts are keys of the Cards table, looked up when the box is drawn so that it follows the language: a special
    // card's title is the card's own name (its id string), its text that plus ".text".
    public class CardDescription
    {
        public readonly string TitleKey;
        public readonly string TextKey;
        public readonly string Icon; // under Resources/Cards/DescriptionIcon, or null

        public string Title { get { return Loc.T(Loc.Cards, TitleKey); } }
        public string Text { get { return Loc.T(Loc.Cards, TextKey); } }

        // A leader's ability, as its description box and How to play say it (null for an id that is no leader)
        public static string LeaderAbility(int leader_id)
        {
            string key;
            return Leaders.TryGetValue(leader_id, out key) ? Loc.T(Loc.Cards, key) : null;
        }

        private CardDescription(string title_key, string text_key, string icon = null)
        {
            TitleKey = title_key;
            TextKey = text_key;
            Icon = icon;
        }

        // null for a plain unit
        public static CardDescription For(Card card)
        {
            CardDescription description;
            if (card.faction == Faction.Special)
                return Specials.TryGetValue(card._idstr, out description) ? description : null;
            if (card.ability == Ability.Leader)
                return Leaders.TryGetValue(card._id, out string key) ? new CardDescription("ability.leader.title", key) : null;
            if (card.ability != Ability.None)
                return Abilities.TryGetValue(card.ability, out description) ? description : null;
            int avenger = AbilityRules.AvengerOf(card._id);
            if (avenger != -1)
                return avenger == CardIds.Hemdall ? AvengerHemdall : AvengerCow;
            if (card.unique)
                return Hero;
            if (card.row == Row.Agile)
                return Agile;
            return null;
        }

        private static readonly CardDescription Hero = new CardDescription("ability.hero.title", "ability.hero.text");

        private static readonly CardDescription Agile = new CardDescription("ability.agile.title", "ability.agile.text", "agile"); // Francesca, Hope of the Aen Seidhe moves it later

        // The Cow's and Kambi's: a sentence each, since the name and the "he"/"it" after it change with the card
        private static readonly CardDescription AvengerHemdall = new CardDescription("ability.avenger.title", "ability.avenger.hemdall", "avenger");
        private static readonly CardDescription AvengerCow = new CardDescription("ability.avenger.title", "ability.avenger.cow", "avenger");

        private static readonly CardDescription Mardroeme = Special("mardroeme");

        private static readonly CardDescription CommanderHorn = Special("commander_horn");

        // Dandelion: his horn doubles the others in his row (not himself), and a Commander's Horn may still join it
        private static readonly CardDescription UnitHorn = new CardDescription("commander_horn", "ability.unit_horn.text", "commander_horn");

        // A special card's own box: titled with its name, the icon is named as the card
        private static CardDescription Special(string id)
        {
            return new CardDescription(id, id + ".text", id);
        }

        // Special cards, by their id string
        private static readonly Dictionary<string, CardDescription> Specials = new Dictionary<string, CardDescription>
        {
            { "biting_frost", Special("biting_frost") },
            { "impenetrable_fog", Special("impenetrable_fog") },
            { "torrential_rain", Special("torrential_rain") },
            { "skellige_storm", Special("skellige_storm") },
            { "clear_weather", Special("clear_weather") },
            { "mardroeme", Mardroeme }, // in a row's horn slot
            { "commander_horn", CommanderHorn },
            { "decoy", Special("decoy") },
            { "scorch", Special("scorch") },
        };

        // Units' abilities
        private static readonly Dictionary<Ability, CardDescription> Abilities = new Dictionary<Ability, CardDescription>
        {
            { Ability.Spy, Unit("spy", "spy") },
            { Ability.Medic, Unit("medic", "medic") },
            { Ability.Muster, Unit("muster", "muster") },
            { Ability.MoraleBoost, Unit("morale_boost", "morale_boost") },
            { Ability.TightBond, Unit("tight_bond", "tight_bond") },
            { Ability.UnitHorn, UnitHorn },
            { Ability.ScorchClose, RowScorch("ability.scorch_close.text") },
            { Ability.ScorchRange, RowScorch("ability.scorch_range.text") },
            { Ability.ScorchSiege, RowScorch("ability.scorch_siege.text") },
            { Ability.Scorch, RowScorch("ability.scorch.text") }, // the Clan Dimun Pirate
            { Ability.Berserker, Unit("berserker", "berserker") },
            { Ability.Mardroeme, Mardroeme }, // Ermion
        };

        // An ability of its own: "ability.<id>.title" and ".text" in the table, the icon named by the id
        private static CardDescription Unit(string id, string icon)
        {
            return new CardDescription("ability." + id + ".title", "ability." + id + ".text", icon);
        }

        // The Scorch units' boxes are titled with the Scorch card's name
        private static CardDescription RowScorch(string text_key)
        {
            return new CardDescription("scorch", text_key, "scorch");
        }

        // Leaders' abilities, by card id (LeaderIds): the key of the Cards table
        private static readonly Dictionary<int, string> Leaders = new Dictionary<int, string>
        {
            { LeaderIds.EmhyrEmperorOfNilfgaard, "leader.emhyr_emperor" },
            { LeaderIds.EmhyrHisImperialMajesty, "leader.emhyr_majesty" },
            { LeaderIds.EmhyrInvaderOfTheNorth, "leader.emhyr_invader" },
            { LeaderIds.EmhyrTheRelentless, "leader.emhyr_relentless" },
            { LeaderIds.EmhyrTheWhiteFlame, "leader.emhyr_white_flame" },
            { LeaderIds.EredinTheTreacherous, "leader.eredin_treacherous" },
            { LeaderIds.EredinBringerOfDeath, "leader.eredin_bringer" },
            { LeaderIds.EredinCommanderOfTheRedRiders, "leader.eredin_commander" },
            { LeaderIds.EredinDestroyerOfWorlds, "leader.eredin_destroyer" },
            { LeaderIds.EredinKingOfTheWildHunt, "leader.eredin_king" },
            { LeaderIds.FoltestKingOfTemeria, "leader.foltest_king" },
            { LeaderIds.FoltestLordCommander, "leader.foltest_lord" },
            { LeaderIds.FoltestSonOfMedell, "leader.foltest_son" },
            { LeaderIds.FoltestTheSiegemaster, "leader.foltest_siegemaster" },
            { LeaderIds.FoltestTheSteelForged, "leader.foltest_steel" },
            { LeaderIds.FrancescaDaisyOfTheValley, "leader.francesca_daisy" },
            { LeaderIds.FrancescaHopeOfTheAenSeidhe, "leader.francesca_hope" },
            { LeaderIds.FrancescaPurebloodElf, "leader.francesca_pureblood" },
            { LeaderIds.FrancescaQueenOfDolBlathanna, "leader.francesca_queen" },
            { LeaderIds.FrancescaTheBeautiful, "leader.francesca_beautiful" },
            { LeaderIds.CrachAnCraite, "leader.crach" },
            { LeaderIds.KingBran, "leader.bran" },
        };
    }
}
