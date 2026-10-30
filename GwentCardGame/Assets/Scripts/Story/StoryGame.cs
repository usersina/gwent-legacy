using System.Linq;
using Gwent.Core;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Story
{
    // The story being played: read from its save once (StorySave), saved after every change, and the collection decks are
    // built from while it is on (DeckContext). A new story starts with The Witcher 3's starter deck, which is also its
    // first deck in the deck builder and its battle deck.
    public static class StoryGame
    {
        public const string StarterDeckName = "Starter deck";

        public static StoryProgress Progress { get; private set; }

        // Why the save could not be read (it is left as it is: Start over sets it aside), or null
        public static string Problem { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            Progress = null;
            Problem = null;
        }

        // The story saved (null: none yet, or one that can't be read: Problem)
        public static StoryProgress Load()
        {
            if (Progress != null)
                return Progress;
            string problem;
            StoryState state = StorySave.Load(out problem);
            Problem = problem;
            if (state != null)
                Progress = StoryLibrary.Over(state);
            return Progress;
        }

        // A new story (the one before set aside, never deleted)
        public static StoryProgress Begin()
        {
            DeckContext.Leave();
            StorySave.SetAside();
            Problem = null;
            Progress = StoryLibrary.Over(StoryProgress.NewState(StoryLibrary.Get(), Rng.Shared.Next()));
            Enter();
            Deck starter = StarterDeck(Progress);
            string path = DeckStorage.PathFor(starter.Faction, DeckRules.ToFileName(starter.Name));
            if (DeckStorage.Save(starter, path))
                DeckStorage.SaveBattleDeck(path); // saves the story too
            else
                Save();
            GameLog.Event(LogCategory.Game, "story_begun", "seed", Progress.State.Seed);
            return Progress;
        }

        // The story's collection for the deck builder and the battles from now on
        public static void Enter()
        {
            if (Progress != null && DeckContext.Story != Progress)
                DeckContext.Enter(Progress, Save);
        }

        public static void Save()
        {
            if (Progress != null)
                StorySave.Save(Progress.State);
        }

        // The Witcher 3's starter deck: its Northern Realms cards and the leader it gives first
        public static Deck StarterDeck(StoryProgress story)
        {
            CardDatabase db = CardLibrary.Get();
            return new Deck
            {
                Faction = Faction.NorthernRealms,
                Name = StarterDeckName,
                Leader = DeckRules.DefaultLeader(Faction.NorthernRealms, db, story),
                Cards = story.Content.File.Starter.Where(id => !db.Get(id).IsLeader && DeckRules.Fits(db.Get(id), Faction.NorthernRealms)).ToList(),
            };
        }
    }
}
