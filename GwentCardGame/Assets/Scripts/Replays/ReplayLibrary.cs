using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gwent.Core;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Replays
{
    // The replays saved on this device: a file per battle in the game's data folder (Replays/), each a MatchRecord. A
    // battle is saved from its end screen (Save replay: against the AI, and on LAN as the host or, once its fair play
    // check passed, the guest); it stays until deleted.
    public static class ReplayLibrary
    {
        private const string Extension = ".replay";

        // A replay and its file
        public sealed class Entry
        {
            public string File;
            public MatchRecord Record;
        }

        // Another folder for the tools (the autoplay's battles are not the player's)
        public static string FolderOverride;

        private static string Folder { get { return FolderOverride ?? Path.Combine(Application.persistentDataPath, "Replays"); } }

        // A battle just over, saved (null: it could not be: not over, a full disk)
        public static Entry Save(Match match, bool seat, RecordKind kind, string my_name, string their_name)
        {
            MatchRecord record = MatchRecord.Of(match, seat, kind, my_name, their_name);
            if (record == null)
                return null;
            record.Build = BuildInfo.Number;
            record.BuildId = BuildInfo.Id;
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, "battle-" + record.Saved.ToString("yyyyMMdd-HHmmss-fff") + (seat ? "" : "-guest") + Extension);
                Write(file, record);
                DeckStorage.SyncWritten();
                GameLog.Event(LogCategory.Game, "replay_saved", "kind", kind, "events", record.Events, "file", Path.GetFileName(file));
                return new Entry { File = file, Record = record };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[replays] The battle could not be saved: " + e.Message);
                return null;
            }
        }

        // Every replay saved, the newest first (a file that can't be read is left out)
        public static List<Entry> All()
        {
            List<Entry> entries = new List<Entry>();
            if (!Directory.Exists(Folder))
                return entries;
            foreach (string file in Directory.GetFiles(Folder, "*" + Extension))
            {
                try
                {
                    entries.Add(new Entry { File = file, Record = MatchRecord.FromBytes(File.ReadAllBytes(file)) });
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[replays] " + Path.GetFileName(file) + " could not be read: " + e.Message);
                }
            }
            return entries.OrderByDescending(entry => entry.Record.Saved).ToList();
        }

        public static void Delete(Entry entry)
        {
            try
            {
                if (File.Exists(entry.File))
                    File.Delete(entry.File);
                DeckStorage.SyncWritten();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[replays] The replay could not be removed: " + e.Message);
            }
        }

        // Whole, or not at all: never a half-written replay
        private static void Write(string file, MatchRecord record)
        {
            string temp = file + ".new";
            File.WriteAllBytes(temp, record.ToBytes());
            if (File.Exists(file))
                File.Delete(file);
            File.Move(temp, file);
        }
    }
}
