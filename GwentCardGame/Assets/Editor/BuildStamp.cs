using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Gwent.EditorTools
{
    // Stamps which source a build comes from (BuildInfo): the commit ("1a2b3c4d"), and when the working tree has changes
    // not committed, a hash of them too ("1a2b3c4d.5e6f70"), so that two builds match only when built from the same files.
    // Its second line is the build number, the count of commits ("332"): later builds have higher ones (the update check
    // compares them; the release workflow gives it, -buildNumber 332, as it gives Android's version code).
    // Written to Assets/Resources/BuildStamp.txt (ignored by git) as each build starts, and as the editor loads its scripts
    // (the editor's own play is the working tree's build). "unknown" without git. A stamp given on the command line
    // (-buildStamp 1a2b3c4d) is taken as it is: the release workflow gives its commit, its builds running in a container
    // where git may not (the checkout another user's) and where a platform's own files (Android's keystore) would count
    // as changes.
    public class BuildStamp : IPreprocessBuildWithReport
    {
        private const string StampPath = "Assets/Resources/BuildStamp.txt";

        public int callbackOrder { get { return -100; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            Write();
        }

        [InitializeOnLoadMethod]
        private static void OnScriptsLoaded()
        {
            if (!BuildPipeline.isBuildingPlayer)
                EditorApplication.delayCall += () => Write();
        }

        // Writes the stamp and the build number (only when they changed: no import otherwise); the stamp written
        public static string Write()
        {
            string stamp = Compute();
            string text = stamp + "\n" + Number() + "\n";
            string old = File.Exists(StampPath) ? File.ReadAllText(StampPath) : null;
            if (old != text)
            {
                File.WriteAllText(StampPath, text);
                AssetDatabase.ImportAsset(StampPath, ImportAssetOptions.ForceSynchronousImport);
            }
            return stamp;
        }

        // The build number: the one given (-buildNumber 332), else the count of commits up to this one; 0 without git
        public static int Number()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int at = Array.LastIndexOf(arguments, "-buildNumber");
            int number;
            if (at >= 0 && at + 1 < arguments.Length && int.TryParse(arguments[at + 1], out number) && number > 0)
                return number;
            return int.TryParse(Git("rev-list --count HEAD"), out number) ? number : 0;
        }

        // The commit, and a hash of what differs from it (changed files, new files not ignored); or the one given
        public static string Compute()
        {
            string given = Given(Environment.GetCommandLineArgs());
            if (given != null)
                return given;
            string commit = Git("rev-parse --short=8 HEAD");
            if (string.IsNullOrEmpty(commit))
                return "unknown";
            string changes = Git("status --porcelain --untracked-files=all");
            if (string.IsNullOrEmpty(changes))
                return commit;
            using (SHA1 sha = SHA1.Create())
            {
                StringBuilder all = new StringBuilder(Git("diff HEAD --binary"));
                foreach (string untracked in Git("ls-files --others --exclude-standard --full-name :/").Split('\n'))
                {
                    string file = untracked.Trim();
                    string path = Path.Combine(RepositoryRoot(), file);
                    if (file.Length > 0 && File.Exists(path))
                        all.Append(file).Append(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))));
                }
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString()));
                return commit + "." + BitConverter.ToString(hash, 0, 3).Replace("-", "").ToLowerInvariant();
            }
        }

        // The stamp after -buildStamp on that command line: a commit (its first 8 characters), or a stamp as this writes
        // them; null when none is given, or it is not one
        public static string Given(string[] arguments)
        {
            int at = Array.IndexOf(arguments, "-buildStamp");
            if (at < 0 || at + 1 >= arguments.Length)
                return null;
            string stamp = arguments[at + 1].Trim().ToLowerInvariant();
            if (stamp.Length == 0 || stamp.Length > 40 || !stamp.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c == '.'))
                return null;
            return stamp.Length == 40 ? stamp.Substring(0, 8) : stamp;
        }

        private static string root;

        private static string RepositoryRoot()
        {
            return root ?? (root = Git("rev-parse --show-toplevel") ?? ".");
        }

        private static string Git(string arguments)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo("git", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                };
                using (Process git = Process.Start(start))
                {
                    string output = git.StandardOutput.ReadToEnd();
                    git.WaitForExit(10000);
                    return git.ExitCode == 0 ? output.Trim() : null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("BuildStamp: git not available (" + e.Message + ")");
                return null;
            }
        }
    }
}
