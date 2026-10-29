using Gwent.Core;
using Gwent.Rules;
using Gwent.Story;

namespace Gwent.Battle
{
    // A battle of the story (StoryBattle): against a player of its map, or a tournament's round, with their deck, name and
    // difficulty, the player's story deck (DeckContext); its outcome goes into the story the moment the match is over,
    // before the last round's animations and the end screen (closing the game then loses nothing). Its ways out lead
    // back to the map.
    public partial class BattleController
    {
        private StoryBattle story;      // a battle of the story (null: Play's, a LAN battle, a replay)

        public StoryBattle Story { get { return story; } }

        // How well the AI plays: a story opponent's own level, else the settings'
        public AiDifficulty AiDifficulty { get { return story != null ? story.Difficulty : GameSettings.AiDifficulty; } }

        // The match over: its outcome into the story, once (from Update, the end and giving up)
        private void ConcludeStory()
        {
            if (story != null && !story.Concluded && match != null && match.IsOver)
                story.Conclude(match.Result == RoundWinner.Player);
        }
    }
}
