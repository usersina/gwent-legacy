using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;

namespace Gwent.Story
{
    // A battle of the story, from its map to the battle scene and back: who is played (a player, or a tournament's round),
    // with their deck (DeckGenerator), name and difficulty. Its outcome goes into the story once, as soon as the match is
    // over (Conclude), before the end screen shows: closing the game then loses nothing, and Play again cannot win twice.
    public sealed class StoryBattle
    {
        public static StoryBattle Current { get; private set; }

        // The place the map opens on when it comes back (the marker of the last battle)
        public static string Back { get; private set; }

        // The battle just left, for the map to tell what it led to (the players it brought out)
        public static StoryBattle Last { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            Current = null;
            Back = null;
            Last = null;
        }

        public readonly StoryPlace Place;
        public readonly StoryRound Round;    // a tournament's round, or null

        public bool Concluded { get; private set; }
        public bool Won { get; private set; }
        public StoryReward Reward { get; private set; }

        private StoryBattle(StoryPlace place, StoryRound round)
        {
            Place = place;
            Round = round;
        }

        public string Name { get { return Round != null ? Round.Name : Place.Name; } }

        // Who plays the story's battles, on the board as in The Witcher 3
        public const string PlayerName = "Geralt";

        public AiDifficulty Difficulty { get { return Place.Difficulty; } }

        public Deck Opponent(CardDatabase db)
        {
            return DeckGenerator.For(Place, Round, db);
        }

        // A battle against a player, or a tournament's next round (the tournament entered, its fee paid)
        public static void Start(StoryPlace place)
        {
            StoryProgress story = StoryGame.Progress;
            StoryRound round = null;
            if (place.Type == StoryPlaceKind.Tournament)
            {
                story.Enter(place);
                StoryGame.Save();
                round = story.NextRound(place);
            }
            Current = new StoryBattle(place, round);
            Back = place.Id;
            GameLog.Event(LogCategory.Game, "story_battle", "place", place.Id, "round", round != null ? round.Id : "");
            LoadingScreen.Load(SceneNames.Game);
        }

        // The battle's outcome into the story (once): a win's reward; a tournament's round lost or drawn ends its run
        public StoryReward Conclude(bool won)
        {
            if (Concluded)
                return Reward;
            Concluded = true;
            Won = won;
            StoryProgress story = StoryGame.Progress;
            if (won)
                Reward = Round != null ? story.WinRound(Place) : story.Win(Place);
            else if (Round != null)
                story.LoseRound(Place);
            StoryGame.Save();
            GameLog.Event(LogCategory.Game, "story_outcome", "place", Place.Id, "won", won, "cards", Reward != null ? Reward.Cards.Count : 0,
                "crowns", Reward != null ? Reward.Crowns : 0);
            return Reward;
        }

        // A tournament with a round still to play in this run (after a round won)
        public bool RoundsLeft { get { return Round != null && StoryGame.Progress.Entered(Place); } }

        // Back to the map (the battle's scene left; a paused battle stays paused under the loading screen)
        public static void Leave()
        {
            Last = Current;
            Current = null;
            LoadingScreen.Load(SceneNames.Story);
        }

        // Where the battle's way out leads: the map in the story, else the main menu
        public static string ExitScene { get { return Current != null ? SceneNames.Story : SceneNames.Menu; } }

        // The map has opened (or the main menu): the battle is over for good; what it led to, told once
        public static StoryBattle Forget()
        {
            StoryBattle last = Last ?? Current;
            Current = null;
            Last = null;
            return last;
        }
    }
}
