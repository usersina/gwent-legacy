using System;
using System.Linq;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Core
{
    // Which collection the decks are built from and taken into battle from: the free one (every copy of every card: Play,
    // Multiplayer and the deck builder of the main menu) or the story's (the cards owned: StoryProgress). The story's map
    // sets it, the main menu sets it back. In the story the decks have their own folder (DeckStorage.Root), the battle
    // deck is the story save's own, a deck holds the copies owned and only the factions with a leader owned show.
    public static class DeckContext
    {
        public static StoryProgress Story { get; private set; }

        private static Action save;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            Story = null;
            save = null;
        }

        public static bool InStory { get { return Story != null; } }

        // How many copies of a card a deck may hold here
        public static ICardSupply Supply { get { return Story != null ? (ICardSupply)Story : AllCards.Instance; } }

        // The factions decks can be built for here, in the deck builder's order
        public static Faction[] Factions { get { return Story != null ? Story.OpenFactions.ToArray() : Rules.Factions.Playable; } }

        // The story's collection from now on; save: writes the story (its battle deck is part of its save)
        public static void Enter(StoryProgress story, Action save_story)
        {
            Story = story;
            save = save_story;
            DeckStorage.EnsureDirectories();
            DeckStorage.LoadBattleDeck();
        }

        // The free collection again
        public static void Leave()
        {
            if (Story == null)
                return;
            Story = null;
            save = null;
            DeckStorage.LoadBattleDeck();
        }

        public static void SaveStory()
        {
            if (save != null)
                save();
        }
    }
}
