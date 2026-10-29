using System;
using System.Collections.Generic;

namespace Gwent.Rules
{
    // What a story's save holds (<persistentDataPath>/Story/story.json, written by StorySave): the cards owned, the crowns,
    // who was beaten and how often, what was bought, found and given, the quests whose reward was given, how far the pool
    // of random cards was drawn (its order comes from the seed: reloading never draws again), the tournament under way,
    // the region the map was left on and the story's battle deck. StoryProgress keeps it; only StoryProgress changes it.
    [Serializable]
    public class StoryState
    {
        public const int CurrentFormat = 1;

        public int Format = CurrentFormat;
        public int Seed;
        public int Crowns;
        public List<int> Cards = new List<int>();               // every copy owned, by card id (twice: two copies)
        public List<StoryWins> Wins = new List<StoryWins>();    // by place, or "<tournament>/<round>"
        public List<string> Taken = new List<string>();         // cards found, events given
        public List<string> Bought = new List<string>();        // "<shop>#<sale's index>"
        public List<string> Quests = new List<string>();        // quests completed, their reward given
        public int Drawn;                                       // cards drawn from the pool
        public string Region = "";
        public string Entered = "";                             // the tournament entered (its fee paid), "" for none
        public int Round;                                       // its next round
        public string BattleDeck = "";                          // the story deck taken into battle (a file's path)
    }

    [Serializable]
    public class StoryWins
    {
        public string Place;
        public int Count;
    }
}
