using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Core
{
    // Deck files on disk: <persistentDataPath>/Decks/<faction>/<name>.json. Decks that ship with the game (the AI's,
    // offered as presets) are read from Resources, by a "resource://<path>" path. In a browser the files are stored in
    // the browser's storage after every change (Plugins/WebGL/GwentStorage.jslib).
    public static class DeckStorage
    {
        public const string ResourcePrefix = "resource://";

        public static bool IsResource(string path)
        {
            return path != null && path.StartsWith(ResourcePrefix);
        }

        // The deck file is there (or the deck ships with the game)
        public static bool Exists(string path)
        {
            if (IsResource(path))
                return Resources.Load<TextAsset>(path.Substring(ResourcePrefix.Length)) != null;
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static string Root
        {
            get { return Path.Combine(Application.persistentDataPath, "Decks"); }
        }

        public static string FactionFolder(Faction faction)
        {
            return Path.Combine(Root, faction.Code());
        }

        //----------------------------------------The battle deck----------------------------------------//
        // The settings' key of the deck taken into battle: a deck file's path or a preset's
        public const string BattleDeckKey = "playerDeckPath";

        // The deck taken into battle this session (null: the default deck)
        public static string BattleDeckPath;

        // The battle deck as saved in the settings ("" when there is none)
        public static string SavedBattleDeck { get { return PlayerPrefs.GetString(BattleDeckKey, ""); } }

        // Reads the battle deck back from the settings (ignored if the file is gone)
        public static void LoadBattleDeck()
        {
            BattleDeckPath = null;
            string path = SavedBattleDeck;
            if ((path.EndsWith(".json") || OpponentDecks.IsPreset(path)) && Exists(path))
                BattleDeckPath = path;
        }

        // Takes a deck into battle and saves the choice (null or "": the default deck)
        public static void SaveBattleDeck(string path)
        {
            if (string.IsNullOrEmpty(path))
                PlayerPrefs.DeleteKey(BattleDeckKey);
            else
                PlayerPrefs.SetString(BattleDeckKey, path);
            PlayerPrefs.Save();
            BattleDeckPath = string.IsNullOrEmpty(path) ? null : path;
        }

        //----------------------------------------Files----------------------------------------//
        public static void EnsureDirectories()
        {
            foreach (Faction faction in Factions.Playable)
                Directory.CreateDirectory(FactionFolder(faction));
        }

        // All deck files of a faction, sorted by file name
        public static List<string> DeckPaths(Faction faction)
        {
            List<string> paths = new List<string>();
            string folder = FactionFolder(faction);
            if (!Directory.Exists(folder))
                return paths;
            foreach (string path in Directory.GetFiles(folder, "*.json"))
                paths.Add(path);
            paths.Sort(System.StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        // Null if the file is missing or not a deck
        public static Deck Load(string path)
        {
            if (IsResource(path))
            {
                TextAsset asset = Resources.Load<TextAsset>(path.Substring(ResourcePrefix.Length));
                return asset != null ? CardLibrary.ParseDeck(asset.text, path) : null;
            }
            try
            {
                return CardLibrary.ParseDeck(File.ReadAllText(path), path);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException || e is System.ArgumentException)
            {
                Debug.LogWarning("Could not read deck " + path + ": " + e.Message);
                return null;
            }
        }

        // Writes atomically (temp file + replace) so a crash cannot leave half a deck behind
        public static bool Save(Deck deck, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(DeckFile.Of(deck)));
                if (File.Exists(path))
                    File.Replace(temp, path, null); // one step: the deck is never missing, even if the game dies here
                else
                    File.Move(temp, path);
                Stored();
                return true;
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException || e is System.ArgumentException)
            {
                Debug.LogError("Could not save deck " + path + ": " + e.Message);
                return false;
            }
        }

        public static bool Delete(string path)
        {
            try
            {
                File.Delete(path);
                Stored();
                return true;
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException || e is System.ArgumentException)
            {
                Debug.LogError("Could not delete deck " + path + ": " + e.Message);
                return false;
            }
        }

    #if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void GwentSyncFiles();
    #endif

        // Other files the game writes (replays) to the browser's storage too, as the decks' are
        public static void SyncWritten()
        {
            Stored();
        }

        // The files written reach the browser's storage (elsewhere they are on disk already)
        private static void Stored()
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            GwentSyncFiles();
    #endif
        }

        // Same file, whatever the separators / case (Windows paths saved by older versions); a deck of the game's own by
        // its exact path (a "resource://" path is no file path: Windows refuses its colon)
        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            if (IsResource(a) || IsResource(b))
                return a == b;
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), System.StringComparison.OrdinalIgnoreCase);
        }

        public static string PathFor(Faction faction, string file_name)
        {
            return Path.Combine(FactionFolder(faction), file_name + ".json");
        }
    }
}
