using System;
using System.IO;
using Gwent.Core;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Story
{
    // The story's save, one: <persistentDataPath>/Story/story.json (a StoryState), written after every change, whole
    // (a temp file then a replace: a crash never leaves half a save), and kept in the browser's storage on the web as
    // the decks are. A save that can't be read is never written over: its problem is told, and only Start over moves it
    // aside (story-<date>.json beside it), so nothing is lost by a damaged file or a newer version's save.
    public static class StorySave
    {
        private const string FileName = "story.json";

        // Another folder for the tools (the autoplay's story is not the player's)
        public static string FolderOverride;

        public static string Folder { get { return FolderOverride ?? Path.Combine(Application.persistentDataPath, "Story"); } }

        private static string FilePath { get { return Path.Combine(Folder, FileName); } }

        public static bool Exists { get { return File.Exists(FilePath); } }

        // The save, or null: none yet (problem null), or one that can't be read (problem says why; the file stays)
        public static StoryState Load(out string problem)
        {
            problem = null;
            if (!Exists)
                return null;
            try
            {
                StoryState state = JsonUtility.FromJson<StoryState>(File.ReadAllText(FilePath));
                if (state == null || state.Cards == null || state.Cards.Count == 0)
                    problem = "The story's save could not be read";
                else if (state.Format > StoryState.CurrentFormat)
                    problem = "The story was saved by a newer version of the game";
                else
                    return state;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                problem = "The story's save could not be read";
                Debug.LogWarning("[story] " + FilePath + ": " + e.Message);
            }
            GameLog.Event(LogCategory.Game, "story_unreadable", "problem", problem);
            return null;
        }

        public static bool Save(StoryState state)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(state));
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null); // one step: the save is never missing, even if the game dies here
                else
                    File.Move(temp, FilePath);
                DeckStorage.SyncWritten();
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogError("[story] The story could not be saved: " + e.Message);
                return false;
            }
        }

        // Start over: the save and the story's decks moved aside (Earlier/<date>, never deleted), so a new story begins
        public static bool SetAside()
        {
            string decks = Path.Combine(Folder, "Decks");
            if (!Exists && !Directory.Exists(decks))
                return true;
            try
            {
                string aside = Path.Combine(Folder, "Earlier", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(aside);
                if (Exists)
                    File.Move(FilePath, Path.Combine(aside, FileName));
                if (Directory.Exists(decks))
                    Directory.Move(decks, Path.Combine(aside, "Decks"));
                DeckStorage.SyncWritten();
                GameLog.Event(LogCategory.Game, "story_set_aside", "folder", aside);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError("[story] The old story could not be set aside: " + e.Message);
                return false;
            }
        }
    }
}
