using System.Collections.Generic;
using System.Linq;

namespace Gwent.Rules
{
    // The story's content (StoryFile), indexed. A region's opening or a place's appearance is a condition: "" (always),
    // "beat:<place>" (beaten, found or given) or "quest:<quest>" (every step done).
    public class StoryContent
    {
        public StoryFile File { get; private set; }

        private readonly Dictionary<string, StoryRegion> regions = new Dictionary<string, StoryRegion>();
        private readonly Dictionary<string, StoryPlace> places = new Dictionary<string, StoryPlace>();
        private readonly Dictionary<string, StoryQuest> quests = new Dictionary<string, StoryQuest>();
        private readonly Dictionary<string, StoryMap> maps = new Dictionary<string, StoryMap>();

        public StoryContent(StoryFile file)
        {
            File = file;
            foreach (StoryRegion region in file.Regions)
                regions[region.Id] = region;
            foreach (StoryPlace place in file.Places)
                places[place.Id] = place;
            foreach (StoryQuest quest in file.Quests)
                quests[quest.Id] = quest;
            foreach (StoryMap map in file.Maps)
                maps[map.Id] = map;
        }

        public IList<StoryRegion> Regions { get { return File.Regions; } }
        public IList<StoryPlace> Places { get { return File.Places; } }
        public IList<StoryQuest> Quests { get { return File.Quests; } }

        // The region the story starts in
        public StoryRegion First { get { return File.Regions[0]; } }

        public StoryRegion Region(string id)
        {
            StoryRegion region;
            return id != null && regions.TryGetValue(id, out region) ? region : null;
        }

        public StoryPlace Place(string id)
        {
            StoryPlace place;
            return id != null && places.TryGetValue(id, out place) ? place : null;
        }

        public StoryQuest Quest(string id)
        {
            StoryQuest quest;
            return id != null && quests.TryGetValue(id, out quest) ? quest : null;
        }

        public StoryMap Map(string id)
        {
            StoryMap map;
            return id != null && maps.TryGetValue(id, out map) ? map : null;
        }

        public IEnumerable<StoryPlace> PlacesIn(StoryRegion region)
        {
            return File.Places.Where(place => place.Region == region.Id);
        }

        // The quests a place is a step of
        public IEnumerable<StoryQuest> QuestsOf(StoryPlace place)
        {
            return File.Quests.Where(quest => quest.Steps.Contains(place.Id));
        }

        // A condition's kind ("beat" or "quest") and what it names; false for "" (always met)
        public static bool Condition(string condition, out string kind, out string id)
        {
            kind = id = null;
            if (string.IsNullOrEmpty(condition))
                return false;
            int colon = condition.IndexOf(':');
            kind = colon > 0 ? condition.Substring(0, colon) : condition;
            id = colon > 0 ? condition.Substring(colon + 1) : "";
            return true;
        }

        //----------------------------------------Checks----------------------------------------//
        // What is wrong with the content, for the tests: every copy of every card has one source (the starter deck, a
        // sale, a prize, a quest's reward, an event's deck or the pool), as many as the card has copies; every condition
        // and quest step names a place or quest there is; every player can be given a deck; the pool has enough players
        // to give it; first wins pay for every shop and fee
        public List<string> Problems(CardDatabase db)
        {
            List<string> problems = new List<string>();
            Dictionary<int, int> sources = new Dictionary<int, int>();
            System.Action<IEnumerable<int>, string> count = (cards, from) =>
            {
                foreach (int card in cards)
                {
                    if (!db.Contains(card))
                        problems.Add(from + ": unknown card " + card);
                    int n;
                    sources.TryGetValue(card, out n);
                    sources[card] = n + 1;
                }
            };
            count(File.Starter, "the starter deck");
            count(File.Pool, "the pool");
            foreach (StoryPlace place in File.Places)
            {
                count(place.Sells.Select(sale => sale.Card), place.Id);
                count(place.Prize, place.Id);
                count(place.Gives, place.Id);
                foreach (StoryRound round in place.Rounds)
                    count(round.Prize, place.Id + "/" + round.Id);
            }
            foreach (StoryQuest quest in File.Quests)
                count(quest.Reward, quest.Id);
            foreach (Card card in db.All)
            {
                int n;
                sources.TryGetValue(card._id, out n);
                if (n != card.copies)
                    problems.Add(card.name + ": " + n + " sources for " + card.copies + " copies");
            }

            foreach (StoryRegion region in File.Regions)
            {
                CheckCondition(region.Opens, "region " + region.Id, problems);
                if (!string.IsNullOrEmpty(region.Map) && Map(region.Map) == null)
                    problems.Add("region " + region.Id + ": no map " + region.Map);
            }
            foreach (StoryPlace place in File.Places)
            {
                CheckCondition(place.Appears, place.Id, problems);
                if (Region(place.Region) == null)
                    problems.Add(place.Id + ": no region " + place.Region);
                if (place.Type == StoryPlaceKind.Player && System.Array.IndexOf(Factions.Playable, place.DeckFaction) < 0)
                    problems.Add(place.Id + ": no faction to play");
                foreach (StoryRound round in place.Rounds)
                    if (System.Array.IndexOf(Factions.Playable, round.DeckFaction) < 0)
                        problems.Add(place.Id + "/" + round.Id + ": no faction to play");
                if (place.Type == StoryPlaceKind.Tournament && place.Rounds.Count == 0)
                    problems.Add(place.Id + ": a tournament without rounds");
                if (place.Type == StoryPlaceKind.Shop && place.Sells.Count == 0)
                    problems.Add(place.Id + ": a shop that sells nothing");
                if (place.Taken && place.Prize.Count + place.Gives.Count == 0)
                    problems.Add(place.Id + ": gives nothing");
                if (place.Placed && Region(place.Region) != null && string.IsNullOrEmpty(Region(place.Region).Map))
                    problems.Add(place.Id + ": placed on a region without a map");
            }
            foreach (StoryQuest quest in File.Quests)
                foreach (string step in quest.Steps)
                    if (Place(step) == null)
                        problems.Add(quest.Id + ": no place " + step);

            int givers = File.Places.Count(place => place.Random);
            if (givers < File.Pool.Count)
                problems.Add(File.Pool.Count + " random cards for " + givers + " players to give them");
            int pays = File.Places.Sum(place => place.Crowns + place.Rounds.Sum(round => round.Crowns));
            int costs = File.Places.Sum(place => place.Fee + place.Sells.Sum(sale => sale.Price));
            if (pays < costs)
                problems.Add("first wins pay " + pays + " crowns; the shops and fees cost " + costs);
            return problems;
        }

        private void CheckCondition(string condition, string what, List<string> problems)
        {
            string kind, id;
            if (!Condition(condition, out kind, out id))
                return;
            if (kind == "beat" ? Place(id) == null : kind == "quest" ? Quest(id) == null : true)
                problems.Add(what + ": unknown condition " + condition);
        }
    }
}
