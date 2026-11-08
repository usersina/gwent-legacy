using Gwent.Rules;

namespace Gwent.Core
{
    // The names the screens show for cards and factions, in the current language (the Cards and UI tables). The Rules
    // assembly cannot reach the localization package, so Card.DisplayName and Factions.DisplayName/Ability stay as the
    // English a rule's message or a test compares; what a player reads goes through here.
    public static class CardText
    {
        // The card's name, as its table entry (named by the card's id string) says it: without the copy number some
        // entries carry ("Nekker 3" is a Nekker)
        public static string LocalName(this Card card)
        {
            return string.IsNullOrEmpty(card._idstr) ? card.DisplayName : Loc.T(Loc.Cards, card._idstr);
        }

        public static string LocalName(this Faction faction)
        {
            string key = FactionKey(faction);
            return key == null ? faction.DisplayName() : Loc.T(Loc.UI, key);
        }

        // The faction's perk, in the current language (null for a faction that has none)
        public static string LocalAbility(this Faction faction)
        {
            string key = FactionKey(faction);
            return key == null ? null : Loc.T(Loc.UI, key + ".ability");
        }

        private static string FactionKey(Faction faction)
        {
            switch (faction)
            {
                case Faction.NorthernRealms: return "faction.northern_realms";
                case Faction.Nilfgaard: return "faction.nilfgaard";
                case Faction.Scoiatael: return "faction.scoiatael";
                case Faction.Monsters: return "faction.monsters";
                case Faction.Skellige: return "faction.skellige";
                default: return null;
            }
        }
    }
}
