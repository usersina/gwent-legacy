using Gwent.Core;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Story
{
    // The story's content (Resources/Story/story.json, made by tools/story-content.py), read once, like the cards
    public static class StoryLibrary
    {
        private const string ResourcePath = "Story/story";
        private static StoryContent content;

        public static StoryContent Get()
        {
            if (content == null)
                content = new StoryContent(JsonUtility.FromJson<StoryFile>(Resources.Load<TextAsset>(ResourcePath).text));
            return content;
        }

        // A story over a save: its rules (StoryProgress) with the cards
        public static StoryProgress Over(StoryState state)
        {
            return new StoryProgress(Get(), state, CardLibrary.Get());
        }
    }
}
