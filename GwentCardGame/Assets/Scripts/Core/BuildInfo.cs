using System.Linq;
using UnityEngine;

namespace Gwent.Core
{
    // Which build of the game this is: its number (Number), its version and the source it was built from, "1.0.0+1a2b3c4d" (the commit), or
    // "1.0.0+1a2b3c4d.5e6f70" when it had changes not committed yet (a hash of them). Two devices play each other on LAN
    // only with the same build (LanLink, LanDiscovery). Stamped by the editor (EditorTools.BuildStamp) into
    // Resources/BuildStamp.txt as each build starts and as the editor loads its scripts; not kept in git.
    public static class BuildInfo
    {
        private static string id;
        private static int number = -1;

        // The whole build id ("1.0.0+1a2b3c4d")
        public static string Id
        {
            get
            {
                if (id == null)
                    Load();
                return id;
            }
        }

        // The build number: the count of commits it was built from (the release's on GitHub); a later build has a higher
        // one (the update check compares them). 0 when unknown
        public static int Number
        {
            get
            {
                if (number < 0)
                    Load();
                return number;
            }
        }

        // The commit it was built from ("1a2b3c4d", without the hash of changes a local build adds), null when unknown
        public static string Commit { get { return CommitOf(Build); } }

        // The commit of a build ("1a2b3c4d" of "1a2b3c4d" or "1a2b3c4d.5e6f70"), null when it holds none ("unknown")
        public static string CommitOf(string build)
        {
            if (string.IsNullOrEmpty(build))
                return null;
            int dot = build.IndexOf('.');
            string commit = dot >= 0 ? build.Substring(0, dot) : build;
            return commit.Length >= 7 && commit.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f') ? commit : null;
        }

        // The commit's page on GitHub, null when unknown
        public static string CommitUrl { get { return Commit != null ? "https://github.com/usersina/gwent-legacy/commit/" + Commit : null; } }

        // "Build 332 (1a2b3c4d)", as the main menu shows it; "Build 1a2b3c4d" without a number
        public static string Label { get { return "Build " + (Number > 0 ? Number + " (" + Build + ")" : Build); } }

        // The stamp: the source on its first line, the number on its second (EditorTools.BuildStamp)
        private static void Load()
        {
            TextAsset stamp = Resources.Load<TextAsset>("BuildStamp");
            string[] lines = stamp != null ? stamp.text.Split('\n') : new string[0];
            string source = lines.Length > 0 ? lines[0].Trim() : "";
            id = Application.version + "+" + (source.Length > 0 ? source : "unknown");
            number = lines.Length > 1 && int.TryParse(lines[1].Trim(), out int read) && read > 0 ? read : 0;
            if (stamp != null)
                Resources.UnloadAsset(stamp);
        }

        // The part after the version, as players compare it ("1a2b3c4d", "1a2b3c4d.5e6f70")
        public static string Build { get { return Short(Id); } }

        // The build part of another device's id (its version too, when the versions differ)
        public static string Short(string build_id)
        {
            if (string.IsNullOrEmpty(build_id))
                return "?";
            int plus = build_id.IndexOf('+');
            if (plus < 0)
                return build_id;
            string version = build_id.Substring(0, plus);
            return version == Application.version ? build_id.Substring(plus + 1) : build_id;
        }
    }
}
