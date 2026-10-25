using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gwent.Updates;
using NUnit.Framework;

namespace Gwent.Updates.Tests
{
    // The update: the feed read and compared, its notes as the dialog shows them, where a computer's update goes, and the
    // swap script run for real (sh, on a computer that has it: Linux, macOS) over a fake game's folder
    public class UpdateTests
    {
        private const string Feed = @"{
  ""build"": 332,
  ""commit"": ""da4254ce"",
  ""date"": ""2026-10-25"",
  ""notes"": ""Intro line\none more.\n\n### Battles\n\n- **Zoom:** two fingers\n  zoom the board.\n- A `?` opens [the rules](docs/x.md).\n  - on a phone,\n    too\n"",
  ""linux"": { ""name"": ""GwentLegacy-Linux.tar.gz"", ""url"": ""https://example.org/l.tar.gz"", ""size"": 1000, ""sha256"": ""ab"" },
  ""android"": { ""name"": ""GwentLegacy-Android.apk"", ""url"": ""https://example.org/a.apk"", ""size"": 2000, ""sha256"": ""cd"" }
}";

        [Test]
        public void TheFeedIsRead()
        {
            UpdateFeed feed = UpdateFeed.Parse(Feed);
            Assert.That(feed, Is.Not.Null);
            Assert.That(feed.build, Is.EqualTo(332));
            Assert.That(feed.commit, Is.EqualTo("da4254ce"));
            Assert.That(feed.date, Is.EqualTo("2026-10-25"));
            Assert.That(feed.For(UpdatePlatform.Linux).url, Is.EqualTo("https://example.org/l.tar.gz"));
            Assert.That(feed.For(UpdatePlatform.Android).size, Is.EqualTo(2000));
            Assert.That(feed.For(UpdatePlatform.Windows), Is.Null, "no archive for a platform the feed doesn't list");
            Assert.That(feed.For(UpdatePlatform.None), Is.Null);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"build\": 0}")]
        [TestCase("{\"commit\": \"x\"}")]
        public void NoFeedInWhatIsNotOne(string text)
        {
            Assert.That(UpdateFeed.Parse(text), Is.Null);
        }

        [Test]
        public void OnlyALaterBuildIsNewer()
        {
            UpdateFeed feed = UpdateFeed.Parse(Feed);
            Assert.That(feed.IsNewerThan(331), Is.True);
            Assert.That(feed.IsNewerThan(332), Is.False);
            Assert.That(feed.IsNewerThan(400), Is.False);
            Assert.That(feed.IsNewerThan(0), Is.False, "a build without a number is never offered one");
        }

        [Test]
        public void TheNotesAreLinesWithoutMarks()
        {
            List<UpdateFeed.NoteLine> lines = UpdateFeed.Lines(UpdateFeed.Parse(Feed).notes);
            Assert.That(lines.Select(l => l.Kind), Is.EqualTo(new[]
            {
                UpdateFeed.NoteKind.Paragraph, UpdateFeed.NoteKind.Heading, UpdateFeed.NoteKind.Point, UpdateFeed.NoteKind.Point,
                UpdateFeed.NoteKind.SubPoint,
            }));
            Assert.That(lines[0].Text, Is.EqualTo("Intro line one more."));
            Assert.That(lines[1].Text, Is.EqualTo("Battles"));
            Assert.That(lines[2].Text, Is.EqualTo("Zoom: two fingers zoom the board."));
            Assert.That(lines[3].Text, Is.EqualTo("A ? opens the rules."));
            Assert.That(lines[4].Text, Is.EqualTo("on a phone, too"), "a point under a point, its lines joined");
        }

        [TestCase("da4254ce", "da4254ce")]
        [TestCase("da4254ce.5e6f70", "da4254ce")]
        [TestCase("unknown", null)]
        [TestCase("", null)]
        public void TheMenuLinksTheBuildsCommit(string build, string commit)
        {
            Assert.That(Gwent.Core.BuildInfo.CommitOf(build), Is.EqualTo(commit));
        }

        [TestCase("https://usersina.github.io/gwent-legacy/", "https://usersina.github.io/gwent-legacy/version.json")]
        [TestCase("https://usersina.github.io/gwent-legacy/index.html?x=1#y", "https://usersina.github.io/gwent-legacy/version.json")]
        [TestCase("http://127.0.0.1:8765/", "http://127.0.0.1:8765/version.json")]
        public void ABrowserReadsTheFeedBesideItsPage(string page, string feed)
        {
            Assert.That(UpdateCheck.PageFeed(page), Is.EqualTo(feed));
        }

        //----------------------------------------Where a computer's update goes----------------------------------------//
        [Test]
        public void LinuxAndWindowsReplaceTheGamesFolder()
        {
            string problem;
            DesktopUpdate.Layout? linux = DesktopUpdate.Find(UpdatePlatform.Linux, "/home/p/Games/GwentLegacy-Linux/GwentLegacy_Data", out problem);
            Assert.That(linux, Is.Not.Null, problem);
            Assert.That(linux.Value.Target, Is.EqualTo("/home/p/Games/GwentLegacy-Linux"));
            Assert.That(linux.Value.Launch, Is.EqualTo("GwentLegacy.x86_64"));
            Assert.That(linux.Value.Staging, Is.EqualTo("/home/p/Games/GwentLegacy-Linux/.gwent-update"));
            DesktopUpdate.Layout? windows = DesktopUpdate.Find(UpdatePlatform.Windows, "/games/Gwent/GwentLegacy_Data", out problem);
            Assert.That(windows.Value.Launch, Is.EqualTo("GwentLegacy.exe"));
            Assert.That(DesktopUpdate.Find(UpdatePlatform.Linux, "/somewhere/Assets", out problem), Is.Null, "the editor's folder is no game's");
            Assert.That(problem, Is.Not.Null);
        }

        [Test]
        public void MacOSReplacesTheAppWhereItIs()
        {
            string problem;
            DesktopUpdate.Layout? mac = DesktopUpdate.Find(UpdatePlatform.MacOS, "/Applications/Gwent Legacy.app/Contents", out problem);
            Assert.That(mac, Is.Not.Null, problem);
            Assert.That(mac.Value.Target, Is.EqualTo("/Applications"));
            Assert.That(mac.Value.Launch, Is.EqualTo("Gwent Legacy.app"));
            Assert.That(DesktopUpdate.Find(UpdatePlatform.MacOS, "/private/var/folders/x/AppTranslocation/1/d/Gwent Legacy.app/Contents", out problem), Is.Null,
                "a translocated app runs from a read-only copy");
            Assert.That(DesktopUpdate.Find(UpdatePlatform.Android, "/data/app", out problem), Is.Null);
        }

        [Test]
        public void TheArchivesOwnFolderHoldsTheEntries()
        {
            string root = Temporary();
            string linux = Directory.CreateDirectory(Path.Combine(root, "linux")).FullName;
            Directory.CreateDirectory(Path.Combine(linux, "GwentLegacy-Linux", "GwentLegacy_Data"));
            Assert.That(DesktopUpdate.ContentRoot(linux), Is.EqualTo(Path.Combine(linux, "GwentLegacy-Linux")));
            string mac = Directory.CreateDirectory(Path.Combine(root, "mac")).FullName;
            Directory.CreateDirectory(Path.Combine(mac, "Gwent Legacy.app", "Contents"));
            Assert.That(DesktopUpdate.ContentRoot(mac), Is.EqualTo(mac), "the app is the entry itself");
            string windows = Directory.CreateDirectory(Path.Combine(root, "windows")).FullName;
            File.WriteAllText(Path.Combine(windows, "GwentLegacy.exe"), "");
            Directory.CreateDirectory(Path.Combine(windows, "GwentLegacy_Data"));
            Assert.That(DesktopUpdate.ContentRoot(windows), Is.EqualTo(windows));
            Directory.Delete(root, true);
        }

        [Test]
        public void AMacAppKeepsItsName()
        {
            string root = Temporary();
            Directory.CreateDirectory(Path.Combine(root, "Gwent Legacy.app"));
            DesktopUpdate.Layout layout = new DesktopUpdate.Layout { Platform = UpdatePlatform.MacOS, Target = "/Applications", Launch = "Gwent (old).app" };
            List<KeyValuePair<string, string>> entries = DesktopUpdate.Entries(layout, root);
            Assert.That(entries.Single().Key, Is.EqualTo("Gwent Legacy.app"));
            Assert.That(entries.Single().Value, Is.EqualTo("Gwent (old).app"));
            Directory.Delete(root, true);
        }

        [Test]
        public void TheWindowsScriptWaitsSwapsAndStarts()
        {
            DesktopUpdate.Layout layout = new DesktopUpdate.Layout { Platform = UpdatePlatform.Windows, Target = @"C:\Games\Gwent 100%", Launch = "GwentLegacy.exe" };
            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("GwentLegacy.exe", "GwentLegacy.exe"),
                new KeyValuePair<string, string>("GwentLegacy_Data", "GwentLegacy_Data"),
            };
            string script = DesktopUpdate.BatchScript(layout, layout.NewFiles, entries, 4242, @"C:\log.txt");
            StringAssert.Contains("tasklist /FI \"PID eq 4242\"", script);
            StringAssert.Contains("cd /d \"C:\\Games\\Gwent 100%%\"", script, "a percent sign doubled for cmd");
            StringAssert.Contains("move \"GwentLegacy_Data\" \"%stage%\\old\\\" >NUL || goto restore", script);
            StringAssert.Contains("move \"%source%\\GwentLegacy.exe\" \".\\GwentLegacy.exe\" >NUL || goto restore", script);
            StringAssert.Contains("start \"\" \".\\GwentLegacy.exe\"", script);
            Assert.That(script.Split('\n').All(line => line.EndsWith("\r") || line.Length == 0), "cmd's line ends");
        }

        //----------------------------------------The swap, run----------------------------------------//
        [Test]
        public void TheShellScriptSwapsTheGamesFilesOnly()
        {
            if (!File.Exists("/bin/sh"))
                Assert.Ignore("no sh here");
            string root = Temporary();
            string game = Directory.CreateDirectory(Path.Combine(root, "Gwent Game's")).FullName; // a space and a quote
            Write(game, "GwentLegacy.x86_64", "#!/bin/sh\necho started > \"$(dirname \"$0\")/started.txt\"\n");
            Write(Path.Combine(game, "GwentLegacy_Data"), "old.txt", "old");
            Write(game, "UnityPlayer.so", "old player");
            Write(game, "notes of mine.txt", "the player's own file");

            DesktopUpdate.Layout layout = new DesktopUpdate.Layout { Platform = UpdatePlatform.Linux, Target = game, Launch = "GwentLegacy.x86_64" };
            string content = Path.Combine(layout.NewFiles, "GwentLegacy-Linux");
            Write(content, "GwentLegacy.x86_64", "#!/bin/sh\necho started > \"$(dirname \"$0\")/started.txt\"\n");
            Write(Path.Combine(content, "GwentLegacy_Data"), "new.txt", "new");
            Write(content, "UnityPlayer.so", "new player");
            Write(content, "libnew.so", "a file the old version lacked");
            List<KeyValuePair<string, string>> entries = DesktopUpdate.Entries(layout, DesktopUpdate.ContentRoot(layout.NewFiles));
            Assert.That(entries.Count, Is.EqualTo(4));

            RunScript(layout, DesktopUpdate.ContentRoot(layout.NewFiles), entries, root);

            Assert.That(File.Exists(Path.Combine(game, "GwentLegacy_Data", "new.txt")), "the new data folder in place");
            Assert.That(File.Exists(Path.Combine(game, "GwentLegacy_Data", "old.txt")), Is.False, "the old one gone, not merged");
            Assert.That(File.ReadAllText(Path.Combine(game, "UnityPlayer.so")), Is.EqualTo("new player"));
            Assert.That(File.Exists(Path.Combine(game, "libnew.so")));
            Assert.That(File.ReadAllText(Path.Combine(game, "notes of mine.txt")), Is.EqualTo("the player's own file"), "other files stay");
            Assert.That(Directory.Exists(layout.Staging), Is.False, "the staging folder removed");
            Assert.That(WaitFor(Path.Combine(game, "started.txt")), "the game started again");
            Directory.Delete(root, true);
        }

        [Test]
        public void AFailedSwapPutsTheOldVersionBack()
        {
            if (!File.Exists("/bin/sh"))
                Assert.Ignore("no sh here");
            string root = Temporary();
            string game = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
            Write(game, "GwentLegacy.x86_64", "#!/bin/sh\necho started > \"$(dirname \"$0\")/started.txt\"\n");
            Write(Path.Combine(game, "GwentLegacy_Data"), "old.txt", "old");
            DesktopUpdate.Layout layout = new DesktopUpdate.Layout { Platform = UpdatePlatform.Linux, Target = game, Launch = "GwentLegacy.x86_64" };
            Write(layout.NewFiles, "GwentLegacy.x86_64", "new");
            Write(Path.Combine(layout.NewFiles, "GwentLegacy_Data"), "new.txt", "new");
            List<KeyValuePair<string, string>> entries = DesktopUpdate.Entries(layout, layout.NewFiles);
            entries.Add(new KeyValuePair<string, string>("missing in the download", "missing in the download")); // its move fails

            RunScript(layout, layout.NewFiles, entries, root);

            Assert.That(File.Exists(Path.Combine(game, "GwentLegacy_Data", "old.txt")), "the old data back");
            Assert.That(File.ReadAllText(Path.Combine(game, "GwentLegacy.x86_64")), Does.StartWith("#!/bin/sh"), "the old executable back");
            Assert.That(WaitFor(Path.Combine(game, "started.txt")), "the old version started again");
            Directory.Delete(root, true);
        }

        // The script for a process that has already ended, run to its end
        private static void RunScript(DesktopUpdate.Layout layout, string content, List<KeyValuePair<string, string>> entries, string root)
        {
            Process gone = Process.Start(new ProcessStartInfo("/bin/sh", "-c true") { UseShellExecute = false });
            gone.WaitForExit();
            string script = Path.Combine(root, "update.sh");
            File.WriteAllText(script, DesktopUpdate.ShellScript(layout, content, entries, gone.Id, Path.Combine(root, "update.log")));
            using (Process run = Process.Start(new ProcessStartInfo("/bin/sh", "\"" + script + "\"") { UseShellExecute = false }))
                Assert.That(run.WaitForExit(20000), "the script ended");
        }

        private static bool WaitFor(string file)
        {
            for (int i = 0; i < 50 && !File.Exists(file); i++)
                System.Threading.Thread.Sleep(100);
            return File.Exists(file);
        }

        private static void Write(string folder, string name, string text)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name), text);
        }

        private static string Temporary()
        {
            string path = Path.Combine(Path.GetTempPath(), "gwent-update-test-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
