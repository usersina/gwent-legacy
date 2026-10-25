using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Gwent.Updates
{
    // A computer's update, once the new version's archive is downloaded and checked: unpacked beside the game (in
    // .gwent-update/new, in the folder the game's files are replaced in), then a script takes over. It waits for the game
    // to quit, moves the game's own files aside (into .gwent-update/old), moves the new ones in, puts the old ones back if
    // one of them can't be moved, starts the game again and removes .gwent-update. Only the names the new version has are
    // touched (other files in that folder stay), and the settings and decks live elsewhere (PlayerPrefs, the data folder).
    //
    // What is replaced where (Layout): on Windows and Linux, the entries of the game's folder (the one holding the
    // executable and its _Data folder; Linux's archive holds that folder, Windows' its contents); on macOS, the app
    // itself, in the folder it sits in (its archive holds the app).
    public static class DesktopUpdate
    {
        public const string StagingName = ".gwent-update";

        // Where an update of this game goes: the folder whose entries are replaced, and the executable or app to start again
        public struct Layout
        {
            public UpdatePlatform Platform;
            public string Target;       // the folder whose entries are replaced
            public string Launch;       // the executable (Windows, Linux) or the app (macOS) started again, in Target
            public string Staging => Path.Combine(Target, StagingName);
            public string NewFiles => Path.Combine(Staging, "new");
        }

        // The layout for a game whose data folder is there (Application.dataPath: ".../GwentLegacy_Data" on Windows and
        // Linux, ".../Gwent Legacy.app/Contents" on macOS); null with the reason when it can't update itself
        public static Layout? Find(UpdatePlatform platform, string data_path, out string problem)
        {
            problem = null;
            data_path = data_path.TrimEnd('/', '\\');
            switch (platform)
            {
                case UpdatePlatform.Windows:
                case UpdatePlatform.Linux:
                {
                    string folder = Path.GetDirectoryName(data_path);
                    string data = Path.GetFileName(data_path);
                    if (folder == null || !data.EndsWith("_Data"))
                    {
                        problem = "The game's files are not where a release puts them.";
                        return null;
                    }
                    string executable = data.Substring(0, data.Length - "_Data".Length) + (platform == UpdatePlatform.Windows ? ".exe" : ".x86_64");
                    return new Layout { Platform = platform, Target = folder, Launch = executable };
                }
                case UpdatePlatform.MacOS:
                {
                    string app = Path.GetDirectoryName(data_path);
                    if (app == null || !app.EndsWith(".app"))
                    {
                        problem = "The game's files are not where a release puts them.";
                        return null;
                    }
                    // macOS runs an app it doesn't trust yet from a read-only copy until it is moved (App Translocation)
                    if (app.Contains("/AppTranslocation/"))
                    {
                        problem = "macOS runs this copy from a read-only place: move Gwent Legacy to Applications, open it from there, and update.";
                        return null;
                    }
                    return new Layout { Platform = platform, Target = Path.GetDirectoryName(app), Launch = Path.GetFileName(app) };
                }
                default:
                    problem = "This platform updates another way.";
                    return null;
            }
        }

        // The folder of the unpacked archive that holds the new entries: the archive's only folder when it has nothing
        // else (Linux's "GwentLegacy-Linux"), unless that is the app (macOS)
        public static string ContentRoot(string unpacked)
        {
            string[] files = Directory.GetFiles(unpacked);
            string[] folders = Directory.GetDirectories(unpacked);
            if (files.Length == 0 && folders.Length == 1 && !folders[0].EndsWith(".app"))
                return folders[0];
            return unpacked;
        }

        // The entries to move: each one's name in the new version, and the name it takes in the target (the same, but for
        // macOS's app, which keeps the name it has now)
        public static List<KeyValuePair<string, string>> Entries(Layout layout, string content_root)
        {
            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();
            IEnumerable<string> names = Directory.GetFileSystemEntries(content_root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
            foreach (string name in names)
            {
                string target = layout.Platform == UpdatePlatform.MacOS && name.EndsWith(".app") ? layout.Launch : name;
                entries.Add(new KeyValuePair<string, string>(name, target));
            }
            return entries;
        }

        //----------------------------------------The scripts----------------------------------------//
        // The script for Linux and macOS (sh): waits for that process to end, swaps the entries, starts the game again
        public static string ShellScript(Layout layout, string content_root, List<KeyValuePair<string, string>> entries, int process_id, string log)
        {
            StringBuilder s = new StringBuilder();
            s.Append("#!/bin/sh\n");
            s.Append("# Gwent Legacy's update (Gwent.Updates.DesktopUpdate): once the game has quit, its files are swapped for the new\n");
            s.Append("# version's, the old ones put back if one can't be moved; then the game starts again\n");
            s.Append("exec >>").Append(Q(log)).Append(" 2>&1\n");
            s.Append("echo \"update: $(date)\"\n");
            s.Append("while kill -0 ").Append(process_id).Append(" 2>/dev/null; do sleep 0.3; done\n");
            s.Append("cd ").Append(Q(layout.Target)).Append(" || exit 1\n");
            s.Append("stage=").Append(Q(layout.Staging)).Append("\n");
            s.Append("source=").Append(Q(content_root)).Append("\n");
            s.Append("mkdir -p \"$stage/old\"\n");
            s.Append("ok=1\n");
            foreach (KeyValuePair<string, string> entry in entries)
                s.Append("if [ $ok = 1 ] && { [ -e ").Append(Q(entry.Value)).Append(" ] || [ -L ").Append(Q(entry.Value)).Append(" ]; }; then mv ")
                    .Append(Q(entry.Value)).Append(" \"$stage/old/\" || ok=0; fi\n");
            foreach (KeyValuePair<string, string> entry in entries)
                s.Append("if [ $ok = 1 ]; then mv \"$source/\"").Append(Q(entry.Key)).Append(" ./").Append(Q(entry.Value)).Append(" || ok=0; fi\n");
            s.Append("if [ $ok = 0 ]; then\n");
            s.Append("  echo \"update: failed, the old version put back\"\n");
            foreach (KeyValuePair<string, string> entry in entries)
                s.Append("  if [ -e \"$stage/old/\"").Append(Q(entry.Value)).Append(" ]; then rm -rf ./").Append(Q(entry.Value))
                    .Append("; mv \"$stage/old/\"").Append(Q(entry.Value)).Append(" ./; fi\n");
            s.Append("fi\n");
            if (layout.Platform == UpdatePlatform.MacOS)
                s.Append("open ./").Append(Q(layout.Launch)).Append("\n");
            else
                s.Append("chmod +x ./").Append(Q(layout.Launch)).Append(" 2>/dev/null\nnohup ./").Append(Q(layout.Launch)).Append(" >/dev/null 2>&1 &\n");
            s.Append("rm -rf \"$stage\"\n");
            s.Append("echo \"update: done (ok=$ok)\"\n");
            return s.ToString();
        }

        // The script for Windows (cmd): the same steps
        public static string BatchScript(Layout layout, string content_root, List<KeyValuePair<string, string>> entries, int process_id, string log)
        {
            StringBuilder s = new StringBuilder();
            s.Append("@echo off\r\n");
            s.Append("rem Gwent Legacy's update (Gwent.Updates.DesktopUpdate): once the game has quit, its files are swapped for the new\r\n");
            s.Append("rem version's, the old ones put back if one can't be moved; then the game starts again\r\n");
            s.Append("set \"stage=").Append(B(layout.Staging)).Append("\"\r\n");
            s.Append("set \"source=").Append(B(content_root)).Append("\"\r\n");
            s.Append("set \"log=").Append(B(log)).Append("\"\r\n");
            s.Append("echo update: %date% %time% >> \"%log%\"\r\n");
            s.Append(":wait\r\n");
            s.Append("tasklist /FI \"PID eq ").Append(process_id).Append("\" /NH 2>NUL | find \"").Append(process_id).Append("\" >NUL\r\n");
            s.Append("if not errorlevel 1 (ping -n 2 127.0.0.1 >NUL & goto wait)\r\n");
            s.Append("cd /d \"").Append(B(layout.Target)).Append("\" || exit /b 1\r\n");
            s.Append("mkdir \"%stage%\\old\" 2>NUL\r\n");
            foreach (KeyValuePair<string, string> entry in entries)
                s.Append("if exist \"").Append(B(entry.Value)).Append("\" move \"").Append(B(entry.Value)).Append("\" \"%stage%\\old\\\" >NUL || goto restore\r\n");
            foreach (KeyValuePair<string, string> entry in entries)
                s.Append("move \"%source%\\").Append(B(entry.Key)).Append("\" \".\\").Append(B(entry.Value)).Append("\" >NUL || goto restore\r\n");
            s.Append("goto launch\r\n");
            s.Append(":restore\r\n");
            s.Append("echo update: failed, the old version put back >> \"%log%\"\r\n");
            foreach (KeyValuePair<string, string> entry in entries)
                s.Append("if exist \"%stage%\\old\\").Append(B(entry.Value)).Append("\" (rmdir /s /q \"").Append(B(entry.Value)).Append("\" 2>NUL & del /f /q \"")
                    .Append(B(entry.Value)).Append("\" 2>NUL & move \"%stage%\\old\\").Append(B(entry.Value)).Append("\" \".\\\" >NUL)\r\n");
            s.Append(":launch\r\n");
            s.Append("start \"\" \".\\").Append(B(layout.Launch)).Append("\"\r\n");
            s.Append("rmdir /s /q \"%stage%\"\r\n");
            s.Append("echo update: done >> \"%log%\"\r\n");
            return s.ToString();
        }

        // Starts the script (it outlives the game) from a folder it doesn't move
        public static void Start(UpdatePlatform platform, string script)
        {
            ProcessStartInfo start = platform == UpdatePlatform.Windows
                ? new ProcessStartInfo("cmd.exe", "/c \"\"" + script + "\"\"")
                : new ProcessStartInfo("/bin/sh", "\"" + script + "\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = Path.GetTempPath();
            Process.Start(start);
        }

        // A word for sh: in single quotes (a single quote in it closed, escaped, reopened)
        private static string Q(string text)
        {
            return "'" + text.Replace("'", "'\\''") + "'";
        }

        // A path for cmd, in double quotes there: its separators Windows' own, its percent signs doubled (Windows paths hold no
        // double quotes)
        private static string B(string text)
        {
            return text.Replace('/', '\\').Replace("%", "%%");
        }
    }
}
