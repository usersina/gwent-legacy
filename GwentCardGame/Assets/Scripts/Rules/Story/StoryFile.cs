using System;
using System.Collections.Generic;

namespace Gwent.Rules
{
    // The story's content as Resources/Story/story.json writes it (tools/story-content.py makes it from The Witcher 3's own
    // data: the Witcher wiki's card sources and Gwent players, witcher3map's markers): its regions, the places on their
    // maps (the Gwent players, the shops, cards to find, tournaments), the Gwent quests, the starter deck and the pool of
    // random cards. Every copy of every card comes from one of them (StoryContent.Problems checks it).
    [Serializable]
    public class StoryFile
    {
        public List<StoryRegion> Regions = new List<StoryRegion>();
        public List<StoryPlace> Places = new List<StoryPlace>();
        public List<StoryQuest> Quests = new List<StoryQuest>();
        public List<int> Starter = new List<int>();     // The Witcher 3's starter deck and its four leaders
        public List<int> Pool = new List<int>();        // the cards the players of no particular renown give, one a player
        public List<StoryMap> Maps = new List<StoryMap>();
    }

    // A region: its map (none for a single place, as the royal palace in Vizima) and what opens it (StoryContent.Condition)
    [Serializable]
    public class StoryRegion
    {
        public string Id, Name, Map, Opens;
    }

    public enum StoryPlaceKind { Player, Shop, Found, Event, Tournament }

    // A place on a region's map: a Gwent player (who may sell cards too), a shop, a card to find, a card or deck given (an
    // event), a tournament. Lat and Lng are witcher3map's coordinates (StoryMap turns them into the map's).
    [Serializable]
    public class StoryPlace
    {
        public string Id, Region, Name, Trade, Kind, Faction, Appears, Deck;
        public int Tier;                // 1 to 3: how hard they play (Easy, Normal; Hard for a master) and what their decks hold
        public int Leader;              // the leader they play (0: their deck's own choice)
        public float Lat, Lng;
        public bool Approximate;        // placed by hand, not on one of the map's markers
        public bool Placed;             // on the map at all (the royal palace has no map)
        public int Crowns;              // what a first win pays
        public int Fee;                 // a tournament's entry fee
        public bool Random;             // a player of no particular renown: a first win gives a card from the pool
        public bool Master;             // one of The Witcher 3's masters of the game, or a tournament: the last tier plays Hard
        public List<StorySale> Sells = new List<StorySale>();
        public List<int> Prize = new List<int>();   // what a first win (or finding, or the event) gives
        public List<int> Gives = new List<int>();   // an event's deck (Count Monnier's Skellige deck)
        public List<StoryRound> Rounds = new List<StoryRound>();

        public StoryPlaceKind Type
        {
            get
            {
                switch (Kind)
                {
                    case "shop": return StoryPlaceKind.Shop;
                    case "found": return StoryPlaceKind.Found;
                    case "event": return StoryPlaceKind.Event;
                    case "tournament": return StoryPlaceKind.Tournament;
                    default: return StoryPlaceKind.Player;
                }
            }
        }

        // A battle against them: a player, or a tournament's rounds
        public bool Plays { get { return Type == StoryPlaceKind.Player || Type == StoryPlaceKind.Tournament; } }

        // A place found or given once, by visiting it
        public bool Taken { get { return Type == StoryPlaceKind.Found || Type == StoryPlaceKind.Event; } }

        // The last tier's masters play Hard; its innkeepers, merchants and craftsmen Normal, with that tier's decks
        public AiDifficulty Difficulty { get { return Tier <= 1 ? AiDifficulty.Easy : Tier >= 3 && Master ? AiDifficulty.Hard : AiDifficulty.Normal; } }

        // The faction of their deck (Neutral for a place without one)
        public Faction DeckFaction
        {
            get
            {
                Faction faction;
                return Factions.TryParse(Faction, out faction) ? faction : Rules.Faction.Neutral;
            }
        }

        // The decks a tournament takes (Neutral: any), as Count Monnier's takes Skellige decks only
        public Faction OnlyDecks
        {
            get
            {
                Faction faction;
                return Factions.TryParse(Deck, out faction) ? faction : Rules.Faction.Neutral;
            }
        }
    }

    // A card a shop sells, once (a merchant's stock, as in The Witcher 3)
    [Serializable]
    public class StorySale
    {
        public int Card, Price;
    }

    // A tournament's round: its player, what winning it gives the first time
    [Serializable]
    public class StoryRound
    {
        public string Id, Name, Faction;
        public int Leader, Crowns;
        public List<int> Prize = new List<int>();

        public Faction DeckFaction
        {
            get
            {
                Faction faction;
                return Factions.TryParse(Faction, out faction) ? faction : Rules.Faction.Neutral;
            }
        }
    }

    // A Gwent quest: the places to beat (or find), in order; its reward once every step is done
    [Serializable]
    public class StoryQuest
    {
        public string Id, Name;
        public List<string> Steps = new List<string>();
        public List<int> Reward = new List<int>();
    }

    // A map's bounds in witcher3map's coordinates: a flat map (Velen, Toussaint) or one in the web's Mercator projection
    // (White Orchard, Skellige). Its image is witcher3map's tiles of one zoom level, stitched (tools/story-maps.py):
    // Rows of 256 pixels tall, cropped to Left, Top, Right, Bottom of the stitched image.
    [Serializable]
    public class StoryMap
    {
        public string Id;
        public float South, West, North, East;
        public bool Mercator;
        public int Zoom, Rows, Left, Top, Right, Bottom;

        // Where witcher3map's coordinates fall on the map's image: from its left and from its top, 0 to 1. A flat map
        // counts 2^zoom pixels a unit from its bottom row up (Leaflet's CRS.Simple); a Mercator map's image is the bottom
        // left of a world 256 * 2^zoom pixels wide.
        public (float x, float y) Position(float lat, float lng)
        {
            double scale = Math.Pow(2, Zoom);
            double x, y;
            if (Mercator)
            {
                double world = 256 * scale;
                double latitude = Math.Max(-85.0511, Math.Min(85.0511, lat)) * Math.PI / 180;
                x = world * (lng / 360.0 + 0.5);
                y = world * (0.5 - Math.Log(Math.Tan(Math.PI / 4 + latitude / 2)) / (2 * Math.PI)) - (world - Rows * 256.0);
            }
            else
            {
                x = lng * scale;
                y = Rows * 256.0 - lat * scale;
            }
            return ((float)((x - Left) / Math.Max(1, Right - Left)), (float)((y - Top) / Math.Max(1, Bottom - Top)));
        }
    }
}
