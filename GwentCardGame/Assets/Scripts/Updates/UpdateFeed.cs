using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Gwent.Updates
{
    // The platforms an update is made for (each has its own archive in the feed); None: the editor, or a platform the
    // releases don't build
    public enum UpdatePlatform { None, Windows, MacOS, Linux, Android, Web }

    // One archive of a release: its file name, its download link (the latest release's, which stays the same), its size
    // in bytes and its SHA-256, checked once downloaded
    [Serializable]
    public class UpdateFile
    {
        public string name;
        public string url;
        public long size;
        public string sha256;

        public bool Usable { get { return !string.IsNullOrEmpty(url) && size > 0 && !string.IsNullOrEmpty(sha256); } }
    }

    // The update feed: version.json, which the release workflow publishes beside the web build
    // (.github/scripts/version_json.py): the newest release's build number (the count of commits, as BuildInfo.Number),
    // its commit, its date, its notes (the newest section of CHANGELOG.md) and each platform's archive
    [Serializable]
    public class UpdateFeed
    {
        public int build;
        public string commit;
        public string date;
        public string notes;
        public UpdateFile windows;
        public UpdateFile macos;
        public UpdateFile linux;
        public UpdateFile android;
        public UpdateFile web;

        // The feed in that text, or null when it is none (not JSON, no build number)
        public static UpdateFeed Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                UpdateFeed feed = JsonUtility.FromJson<UpdateFeed>(json);
                return feed != null && feed.build > 0 ? feed : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // A later build than the one given (0: a build whose number is unknown is never offered one)
        public bool IsNewerThan(int build_number)
        {
            return build_number > 0 && build > build_number;
        }

        // The archive for a platform (null when the release has none for it; the web reloads its page instead)
        public UpdateFile For(UpdatePlatform platform)
        {
            UpdateFile file;
            switch (platform)
            {
                case UpdatePlatform.Windows: file = windows; break;
                case UpdatePlatform.MacOS: file = macos; break;
                case UpdatePlatform.Linux: file = linux; break;
                case UpdatePlatform.Android: file = android; break;
                case UpdatePlatform.Web: file = web; break;
                default: file = null; break;
            }
            return file != null && file.Usable ? file : null;
        }

        // The platform this game runs on
        public static UpdatePlatform Current
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer: return UpdatePlatform.Windows;
                    case RuntimePlatform.OSXPlayer: return UpdatePlatform.MacOS;
                    case RuntimePlatform.LinuxPlayer: return UpdatePlatform.Linux;
                    case RuntimePlatform.Android: return UpdatePlatform.Android;
                    case RuntimePlatform.WebGLPlayer: return UpdatePlatform.Web;
                    default: return UpdatePlatform.None;
                }
            }
        }

        //----------------------------------------The notes----------------------------------------//
        public enum NoteKind { Paragraph, Heading, Point, SubPoint }

        public struct NoteLine
        {
            public NoteKind Kind;
            public string Text;
        }

        // The notes as the dialog shows them: Markdown's headings ("### Battles"), points ("- ...", their lines after the
        // first indented; a point indented itself is a point under the one before) and paragraphs, each joined into one line,
        // without the bold and code marks
        public static List<NoteLine> Lines(string markdown)
        {
            List<NoteLine> lines = new List<NoteLine>();
            if (string.IsNullOrEmpty(markdown))
                return lines;
            NoteLine current = new NoteLine();
            bool open = false;
            foreach (string raw in markdown.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    Close(lines, ref current, ref open);
                    continue;
                }
                if (trimmed.StartsWith("#"))
                {
                    Close(lines, ref current, ref open);
                    lines.Add(new NoteLine { Kind = NoteKind.Heading, Text = Plain(trimmed.TrimStart('#').Trim()) });
                    continue;
                }
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    Close(lines, ref current, ref open);
                    bool under = line.StartsWith(" ") || line.StartsWith("\t");
                    current = new NoteLine { Kind = under ? NoteKind.SubPoint : NoteKind.Point, Text = trimmed.Substring(2).Trim() };
                    open = true;
                    continue;
                }
                if (open)
                    current.Text += " " + trimmed;
                else
                {
                    current = new NoteLine { Kind = NoteKind.Paragraph, Text = trimmed };
                    open = true;
                }
            }
            Close(lines, ref current, ref open);
            return lines;
        }

        private static void Close(List<NoteLine> lines, ref NoteLine current, ref bool open)
        {
            if (!open)
                return;
            current.Text = Plain(current.Text);
            lines.Add(current);
            open = false;
        }

        // Without Markdown's marks: **bold**, `code`, [text](link)
        private static string Plain(string text)
        {
            text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
            return text.Replace("**", "").Replace("`", "");
        }
    }
}
