using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Debug = UnityEngine.Debug;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gwent.EditorTools
{
    // Release builds for every platform this editor has the build support of, ready to hand out:
    // Builds/<version>/ holds GwentLegacy-Linux.tar.gz, -Windows.zip, -macOS.zip, -Android.apk and -Web.zip, with
    // build-report.txt (the names carry no version: the GitHub release keeps the same download links from one version to
    // the next). Menu Gwent > Build, or from the command line (the editor closed):
    //   Unity -batchmode -projectPath GwentCardGame -executeMethod Gwent.EditorTools.GwentBuild.BuildAllBatch
    // The run starts at once, the editor focused or not (in the background it may not update for a long time), and goes
    // through every platform in one go: script reloads (a platform switch asks for one) wait until it is over. The current
    // platform is built first, the others after it, and the editor is switched back to it at the end. The targets left
    // are also kept in SessionState, should a reload come anyway: the run then picks up again after it.
    //
    // Android: signed with a release key when GWENT_KEYSTORE (the .keystore file), GWENT_KEYSTORE_PASS, GWENT_KEY_ALIAS and
    // GWENT_KEY_PASS are set in the environment, or GameCI's -androidKeystoreName (relative to the project),
    // -androidKeystorePass, -androidKeyaliasName and -androidKeyaliasPass are on the command line; the project's settings
    // are put back afterwards (no keystore path or password stays in it). Otherwise with Unity's debug key, which only
    // installs over a build signed on the same machine.
    public static class GwentBuild
    {
        private const string QueueKey = "GwentBuild.Queue";
        private const string ReportKey = "GwentBuild.Report";
        private const string BatchKey = "GwentBuild.Batch";
        private const string HomeKey = "GwentBuild.Home"; // the platform the editor was on
        private const string SucceededKey = "GwentBuild.Succeeded"; // a build of the run succeeded
        private const string FailedKey = "GwentBuild.Failed";       // one failed
        private const string ProductFile = "GwentLegacy";

        private class Target
        {
            public string Name;
            public BuildTarget Build;
            public BuildTargetGroup Group;
            public string Label;        // in the archive's name
            public string Folder;       // under Builds/<version>/
            public string Player;       // the file BuildPlayer writes, in that folder
        }

        private static readonly Target[] Targets =
        {
            new Target { Name = "linux", Label = "Linux", Build = BuildTarget.StandaloneLinux64, Group = BuildTargetGroup.Standalone, Folder = "linux", Player = ProductFile + ".x86_64" },
            new Target { Name = "windows", Label = "Windows", Build = BuildTarget.StandaloneWindows64, Group = BuildTargetGroup.Standalone, Folder = "windows", Player = ProductFile + ".exe" },
            new Target { Name = "macos", Label = "macOS", Build = BuildTarget.StandaloneOSX, Group = BuildTargetGroup.Standalone, Folder = "macos", Player = PlayerSettings.productName + ".app" }, // as Finder and the Dock show it
            new Target { Name = "android", Label = "Android", Build = BuildTarget.Android, Group = BuildTargetGroup.Android, Folder = "android", Player = ProductFile + ".apk" },
            new Target { Name = "web", Label = "Web", Build = BuildTarget.WebGL, Group = BuildTargetGroup.WebGL, Folder = "web", Player = "" },
        };

        private static string Version { get { return PlayerSettings.bundleVersion; } }
        private static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", Version)); } }

        //----------------------------------------Entry points----------------------------------------//
        [MenuItem("Gwent/Build/All platforms")]
        public static void BuildAll() { Start(Targets.Select(t => t.Name), false); }

        [MenuItem("Gwent/Build/Linux")]
        public static void BuildLinux() { Start(new[] { "linux" }, false); }

        [MenuItem("Gwent/Build/Windows")]
        public static void BuildWindows() { Start(new[] { "windows" }, false); }

        [MenuItem("Gwent/Build/macOS")]
        public static void BuildMac() { Start(new[] { "macos" }, false); }

        [MenuItem("Gwent/Build/Android")]
        public static void BuildAndroid() { Start(new[] { "android" }, false); }

        [MenuItem("Gwent/Build/Web")]
        public static void BuildWeb() { Start(new[] { "web" }, false); }

        // -executeMethod Gwent.EditorTools.GwentBuild.BuildAllBatch [-buildTargets linux,windows]: exits 0 when every build
        // succeeded
        public static void BuildAllBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-buildTargets");
            IEnumerable<string> names = index >= 0 && index + 1 < args.Length ? args[index + 1].Split(',') : Targets.Select(t => t.Name);
            Start(names, true);
        }

        private static void Start(IEnumerable<string> names, bool batch)
        {
            // The current platform first, then the others of its group (no switch between them), then the rest in order
            BuildTarget home = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup home_group = BuildPipeline.GetBuildTargetGroup(home);
            List<string> queue = names.Select(n => n.Trim().ToLowerInvariant()).Distinct().Where(n => Targets.Any(t => t.Name == n))
                .OrderBy(n => Targets.First(t => t.Name == n).Build == home ? 0 : Targets.First(t => t.Name == n).Group == home_group ? 1 : 2)
                .ThenBy(n => Array.FindIndex(Targets, t => t.Name == n)).ToList();
            Directory.CreateDirectory(Root);
            SessionState.SetString(QueueKey, string.Join(",", queue));
            SessionState.SetString(ReportKey, "Gwent Legacy " + Version + ", built " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " with Unity " + Application.unityVersion + "\n");
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(HomeKey, (int)home);
            SessionState.SetBool(SucceededKey, false);
            SessionState.SetBool(FailedKey, false);

            EditorApplication.LockReloadAssemblies();
            try
            {
                Continue();
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        // After a domain reload in the middle of a run
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, "")))
                EditorApplication.delayCall += Continue;
        }

        //----------------------------------------The run----------------------------------------//
        private static void Continue()
        {
            while (true)
            {
                List<string> queue = SessionState.GetString(QueueKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (queue.Count == 0)
                {
                    Finish();
                    return;
                }
                // Taken off the queue first: a build that crashes the run is not tried again forever
                Target target = Targets.First(t => t.Name == queue[0]);
                queue.RemoveAt(0);
                SessionState.SetString(QueueKey, string.Join(",", queue));
                Report(Build(target));
            }
        }

        private static string Build(Target target)
        {
            if (!BuildPipeline.IsBuildTargetSupported(target.Group, target.Build))
                return target.Name + ": SKIPPED, the editor has no build support for it (Unity Hub > Installs > Add modules)";

            string folder = Path.Combine(Root, target.Folder);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            // On the target's platform before anything else: the Addressables content is built with the player (into
            // the active platform's folder, Library/com.unity.addressables/aa/<platform>), from the groups as they are now
            if (EditorUserBuildSettings.activeBuildTarget != target.Build)
                EditorUserBuildSettings.SwitchActiveBuildTarget(target.Group, target.Build);
            GwentContent.Update();
            SkipAddressablesReportPrompt();
            Prepare(target);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = target.Build,
                targetGroup = target.Group,
                locationPathName = target.Player.Length > 0 ? Path.Combine(folder, target.Player) : folder,
                options = BuildOptions.None,
            };
            Stopwatch watch = Stopwatch.StartNew();
            BuildReport report;
            string signing = Signing(target);
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                Restore(target);
            }
            BuildSummary summary = report.summary;
            string line = target.Name + ": " + summary.result + " in " + watch.Elapsed.TotalMinutes.ToString("F1") + " min, " + summary.totalErrors + " errors, " + summary.totalWarnings + " warnings";
            // What it warned about, each message once by its first line (the counts alone say nothing)
            foreach (string problem in report.steps.SelectMany(step => step.messages)
                .Where(message => message.type != LogType.Log)
                .Select(message => "  " + message.type.ToString().ToLowerInvariant() + ": " + message.content.Split('\n')[0].Trim())
                .Distinct().Take(40))
                line += "\n" + problem;
            SessionState.SetBool(summary.result == BuildResult.Succeeded ? SucceededKey : FailedKey, true); // not read back from the text: a warning may say "Failed"
            if (summary.result != BuildResult.Succeeded)
                return line;

            string archive = Package(target, folder);
            return line + "\n  " + Path.GetFileName(archive) + " (" + (new FileInfo(archive).Length / 1048576.0).ToString("F1") + " MB)" + signing + Content(folder);
        }

        // The Addressables bundles the player carries (StreamingAssets/aa; inside the .apk on Android, so not counted there)
        private static string Content(string folder)
        {
            string aa = Directory.GetDirectories(folder, "aa", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == "StreamingAssets");
            if (aa == null)
                return "";
            FileInfo[] bundles = new DirectoryInfo(aa).GetFiles("*.bundle", SearchOption.AllDirectories);
            return "\n  Addressables: " + bundles.Length + " bundles, " + (bundles.Sum(b => b.Length) / 1048576.0).ToString("F1") + " MB (loaded on demand)";
        }

        // Before its first content build in an editor (never in batch mode), Addressables asks in a dialog whether to turn
        // on its build report, and the run would wait for an answer: answered "no", as Edit > Preferences > Addressables
        // has it by default (a per-user setting; the flag is internal to Addressables)
        private static void SkipAddressablesReportPrompt()
        {
            System.Reflection.PropertyInfo informed = typeof(UnityEditor.AddressableAssets.Settings.ProjectConfigData).GetProperty(
                "UserHasBeenInformedAboutBuildReportSettingPreBuild", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (informed != null)
                informed.SetValue(null, true);
        }

        // The settings each platform's build depends on, applied every time (some are this machine's editor settings)
        private static void Prepare(Target target)
        {
            switch (target.Build)
            {
                case BuildTarget.StandaloneOSX:
                    SetMacArchitecture(OSArchitecture.ARM64); // Apple Silicon (Unity deprecates Intel support)
                    break;
                case BuildTarget.Android:
                    EditorUserBuildSettings.buildAppBundle = false; // an .apk, to install directly
                    // A release installs over the one before only with a version code at least as high (the release
                    // workflow passes the number of commits on main); a build number given alone is the version code too
                    // (test builds that update one another: -buildNumber 10001, then 10002)
                    savedVersionCode = PlayerSettings.Android.bundleVersionCode;
                    int version_code;
                    if (int.TryParse(Argument("-androidVersionCode") ?? Argument("-buildNumber"), out version_code) && version_code > 0)
                        PlayerSettings.Android.bundleVersionCode = version_code;
                    string keystore = Argument("-androidKeystoreName");
                    if (!string.IsNullOrEmpty(keystore))
                        keystore = Path.GetFullPath(Path.Combine(Application.dataPath, "..", keystore));
                    else
                        keystore = Environment.GetEnvironmentVariable("GWENT_KEYSTORE");
                    PlayerSettings.Android.useCustomKeystore = !string.IsNullOrEmpty(keystore) && File.Exists(keystore);
                    if (PlayerSettings.Android.useCustomKeystore)
                    {
                        PlayerSettings.Android.keystoreName = keystore;
                        PlayerSettings.Android.keystorePass = Argument("-androidKeystorePass") ?? Environment.GetEnvironmentVariable("GWENT_KEYSTORE_PASS");
                        PlayerSettings.Android.keyaliasName = Argument("-androidKeyaliasName") ?? Environment.GetEnvironmentVariable("GWENT_KEY_ALIAS");
                        PlayerSettings.Android.keyaliasPass = Argument("-androidKeyaliasPass") ?? Environment.GetEnvironmentVariable("GWENT_KEY_PASS");
                    }
                    break;
            }
        }

        // UnityEditor.OSXStandalone.UserBuildSettings.architecture, through reflection: the class comes with the macOS
        // build support, and a machine without it (a CI image for another platform) must still compile this script
        private static void SetMacArchitecture(OSArchitecture architecture)
        {
            Type settings = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
            System.Reflection.PropertyInfo property = settings != null ? settings.GetProperty("architecture") : null;
            if (property == null)
                throw new Exception("No macOS build support: cannot choose the app's architecture");
            property.SetValue(null, architecture);
        }

        // The project's settings as they were: the keystore path and the version code are saved in ProjectSettings (the
        // passwords never are)
        private static int savedVersionCode;

        private static void Restore(Target target)
        {
            if (target.Build != BuildTarget.Android)
                return;
            PlayerSettings.Android.bundleVersionCode = savedVersionCode;
            if (!PlayerSettings.Android.useCustomKeystore)
            {
                AssetDatabase.SaveAssets();
                return;
            }
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = "";
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasName = "";
            PlayerSettings.Android.keyaliasPass = "";
            AssetDatabase.SaveAssets();
        }

        // A "-name value" argument of the command line, null when absent or empty; the last one when it is given twice (GameCI
        // passes its own -androidVersionCode and keystore arguments, empty when unset, before the custom parameters)
        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = args.Length - 2; i >= 0; i--)
                if (args[i] == name && !string.IsNullOrEmpty(args[i + 1]) && !args[i + 1].StartsWith("-"))
                    return args[i + 1];
            return null;
        }

        private static string Signing(Target target)
        {
            if (target.Build != BuildTarget.Android)
                return "";
            return PlayerSettings.Android.useCustomKeystore ? ", signed with the release key" : ", signed with Unity's debug key (no GWENT_KEYSTORE set)";
        }

        //----------------------------------------Archives----------------------------------------//
        private static string Package(Target target, string folder)
        {
            string name = ProductFile + "-" + target.Label;
            switch (target.Build)
            {
                case BuildTarget.Android:
                {
                    string apk = Path.Combine(Root, name + ".apk");
                    File.Copy(Path.Combine(folder, target.Player), apk, true);
                    return apk;
                }
                case BuildTarget.StandaloneLinux64:
                {
                    // tar keeps the executable permission
                    string archive = Path.Combine(Root, name + ".tar.gz");
                    Run("tar", "-czf \"" + archive + "\" -C \"" + Root + "\" --transform \"s,^" + target.Folder + "," + name + ",\""
                        + " --exclude \"*" + BurstBackup + "\" --exclude \"*" + BurstDebug + "\" " + target.Folder);
                    return archive;
                }
                case BuildTarget.StandaloneOSX:
                {
                    // zip -y keeps the app's executable permission and symbolic links; without zip (a bare build machine),
                    // tar keeps them too (and macOS opens a .tar.gz on a double click)
                    string archive = Path.Combine(Root, name + ".zip");
                    File.Delete(archive);
                    try
                    {
                        Run("zip", "-qry \"" + archive + "\" \"" + target.Player + "\"", folder);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        archive = Path.Combine(Root, name + ".tar.gz");
                        Run("tar", "-czf \"" + archive + "\" \"" + target.Player + "\"", folder);
                    }
                    return archive;
                }
                default:
                {
                    string archive = Path.Combine(Root, name + ".zip");
                    File.Delete(archive);
                    using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                    {
                        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                        {
                            string entry = Path.GetRelativePath(folder, file).Replace('\\', '/');
                            if (!entry.Contains(BurstBackup) && !entry.Contains(BurstDebug))
                                zip.CreateEntryFromFile(file, entry, System.IO.Compression.CompressionLevel.Optimal);
                        }
                    }
                    return archive;
                }
            }
        }

        // What Burst leaves beside a player that Unity says not to hand out: its backup (the generated code's listing,
        // since LitMotion brought Burst code) and its debug information
        private const string BurstBackup = "_BackUpThisFolder_ButDontShipItWithYourGame";
        private const string BurstDebug = "_BurstDebugInformation_DoNotShip";

        private static void Run(string program, string arguments, string directory = null)
        {
            ProcessStartInfo start = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                WorkingDirectory = directory ?? Root,
            };
            using (Process process = Process.Start(start))
            {
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new Exception(program + " failed (" + process.ExitCode + "): " + errors);
            }
        }

        //----------------------------------------Report----------------------------------------//
        private static void Report(string line)
        {
            Debug.Log("[GwentBuild] " + line);
            SessionState.SetString(ReportKey, SessionState.GetString(ReportKey, "") + line + "\n");
        }

        private static void Finish()
        {
            // Back to the platform the editor was on (its imports are cached: a matter of seconds)
            BuildTarget home = (BuildTarget)SessionState.GetInt(HomeKey, (int)EditorUserBuildSettings.activeBuildTarget);
            if (EditorUserBuildSettings.activeBuildTarget != home)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(home), home);
            string report = SessionState.GetString(ReportKey, "");
            File.WriteAllText(Path.Combine(Root, "build-report.txt"), report);
            SessionState.EraseString(QueueKey);
            Debug.Log("[GwentBuild] done:\n" + report);
            bool batch = SessionState.GetBool(BatchKey, false);
            if (batch)
                EditorApplication.Exit(SessionState.GetBool(SucceededKey, false) && !SessionState.GetBool(FailedKey, false) ? 0 : 1);
        }
    }
}
