using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Gwent.Updates
{
    // Updates the game to the build of the feed, as the update dialog's Update asks:
    // - in a browser, the page reloads (the newest build loads with it);
    // - on Android, the APK is downloaded, checked (size, SHA-256) and handed to Android's installer (AndroidUpdate),
    //   which asks the player to confirm; the game is replaced once installed;
    // - on a computer, the archive is downloaded beside the game, checked, unpacked, and a script swaps the files once the
    //   game has quit, then starts it again (DesktopUpdate);
    // - anywhere else (the editor), the release's page opens.
    // What can't be done (no network, a folder the game may not write to, a download that doesn't match) is told
    // (Problem), with the release's page to download it by hand (OfferPage). A download already done and checked is used
    // again (an install cancelled, then asked again).
    public sealed class UpdateInstaller
    {
        public enum Stage { Ready, Downloading, Checking, Unpacking, Confirming, Restarting, Failed }

        private readonly UpdateFeed feed;
        private readonly UpdatePlatform platform;
        private CancellationTokenSource cancel;
        private UnityWebRequest request;

        public Stage State { get; private set; }
        public long Received { get; private set; }
        public string Problem { get; private set; }
        public bool OfferPage { get; private set; }

        public UpdateInstaller(UpdateFeed feed, UpdatePlatform platform)
        {
            this.feed = feed;
            this.platform = platform;
        }

        // The archive this platform downloads (null: the web, or none in the release)
        public UpdateFile File { get { return platform == UpdatePlatform.Web ? null : feed.For(platform); } }

        // What Update does here, as its button says it
        public string ActionLabel
        {
            get
            {
                if (OfferPage)
                    return "Download page";
                switch (platform)
                {
                    case UpdatePlatform.Web: return "Reload";
                    case UpdatePlatform.Android: return File != null ? "Install" : "Download page";
                    case UpdatePlatform.Windows:
                    case UpdatePlatform.MacOS:
                    case UpdatePlatform.Linux: return File != null ? "Update" : "Download page";
                    default: return "Download page";
                }
            }
        }

        public bool Busy { get { return State == Stage.Downloading || State == Stage.Checking || State == Stage.Unpacking || State == Stage.Restarting; } }

        public void Start()
        {
            if (Busy)
                return;
            Problem = null;
            cancel = new CancellationTokenSource();
            Run(cancel.Token).Forget();
        }

        // Stops a download (the dialog closed, or Cancel)
        public void Cancel()
        {
            if (cancel != null)
                cancel.Cancel();
        }

        private float confirmingSince;

        // Back in the game while Android was asking (its confirmation left without an answer): Install may be pressed again
        public void Resumed()
        {
            if (State == Stage.Confirming && Time.realtimeSinceStartup - confirmingSince > 1f)
                State = Stage.Ready;
        }

        // Android's answer, read while it asks the player: cancelled (Update may be pressed again) or failed
        public void Poll()
        {
            if (State != Stage.Confirming)
                return;
            int status = AndroidUpdate.Status;
            if (status == AndroidUpdate.Cancelled)
            {
                State = Stage.Ready;
                Problem = "The update was cancelled.";
            }
            else if (status > AndroidUpdate.Installed)
                Fail("Android didn't install it: " + AndroidUpdate.Message, true);
        }

        private async UniTaskVoid Run(CancellationToken token)
        {
            UpdateFile file = File;
            GameLog.Event(LogCategory.Game, "update_start", "platform", platform.ToString(), "build", feed.build);
            if (OfferPage || platform == UpdatePlatform.None || platform != UpdatePlatform.Web && file == null)
            {
                Application.OpenURL(UpdateCheck.ReleasePage);
                return;
            }
            try
            {
                switch (platform)
                {
                    case UpdatePlatform.Web:
                        State = Stage.Restarting;
                        Reload();
                        break;
                    case UpdatePlatform.Android:
                        await InstallAndroid(file, token);
                        break;
                    default:
                        await InstallDesktop(file, token);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                State = Stage.Ready;
                Problem = null;
            }
            catch (Exception e)
            {
                Fail("The update failed: " + e.Message, true);
                GameLog.Event(LogCategory.Game, "update_failed", "error", e.ToString());
            }
        }

        //----------------------------------------Android----------------------------------------//
        private async UniTask InstallAndroid(UpdateFile file, CancellationToken token)
        {
            string apk = Path.Combine(Application.temporaryCachePath, "GwentLegacy-update.apk");
            if (!await Fetch(file, apk, token))
                return;
            // Not allowed to install yet: Android's page to allow it first (an install started without it is interrupted by
            // Android's settings, and its session never says it ended)
            if (!AndroidUpdate.CanInstall)
            {
                AndroidUpdate.OpenInstallSettings();
                State = Stage.Ready;
                Problem = "Allow installs from Gwent Legacy in Android's settings, then tap Install again.";
                return;
            }
            State = Stage.Confirming;
            confirmingSince = Time.realtimeSinceStartup;
            string problem = AndroidUpdate.Install(apk);
            if (problem != null)
                Fail("Android couldn't start installing it: " + problem, true);
        }

        //----------------------------------------Computers----------------------------------------//
        private async UniTask InstallDesktop(UpdateFile file, CancellationToken token)
        {
            string problem;
            DesktopUpdate.Layout? found = DesktopUpdate.Find(platform, Application.dataPath, out problem);
            if (found == null)
            {
                Fail(problem, true);
                return;
            }
            DesktopUpdate.Layout layout = found.Value;
            string archive = Path.Combine(layout.Staging, file.name);
            try
            {
                Directory.CreateDirectory(layout.Staging);
                if (Directory.Exists(layout.NewFiles))
                    Directory.Delete(layout.NewFiles, true);
                string old = Path.Combine(layout.Staging, "old");
                if (Directory.Exists(old))
                    Directory.Delete(old, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Fail("The game's folder can't be written to (" + layout.Target + "): download the new version by hand.", true);
                return;
            }
            if (!await Fetch(file, archive, token))
                return;

            State = Stage.Unpacking;
            await UniTask.RunOnThreadPool(() => Unpack(archive, layout.NewFiles), cancellationToken: token);
            System.IO.File.Delete(archive);
            string content = DesktopUpdate.ContentRoot(layout.NewFiles);
            List<KeyValuePair<string, string>> entries = DesktopUpdate.Entries(layout, content);
            if (entries.Count == 0 || !entries.Exists(e => e.Value == layout.Launch))
            {
                Fail("The downloaded version doesn't hold the game (" + layout.Launch + ").", true);
                return;
            }

            bool windows = platform == UpdatePlatform.Windows;
            string script = Path.Combine(Path.GetTempPath(), "gwent-update" + (windows ? ".cmd" : ".sh"));
            string log = Path.Combine(Application.persistentDataPath, "update.log");
            int process = Process.GetCurrentProcess().Id;
            System.IO.File.WriteAllText(script, windows ? DesktopUpdate.BatchScript(layout, content, entries, process, log)
                : DesktopUpdate.ShellScript(layout, content, entries, process, log));
            GameLog.Event(LogCategory.Game, "update_restart", "target", layout.Target, "entries", entries.Count, "script", script);
            DesktopUpdate.Start(platform, script);
            State = Stage.Restarting;
            await UniTask.Delay(800, DelayType.Realtime); // "Restarting" shows
            Application.Quit();
        }

        // tar keeps Linux's executable permission, ditto macOS's app (its permissions and links); Windows unzips itself
        private void Unpack(string archive, string folder)
        {
            Directory.CreateDirectory(folder);
            if (archive.EndsWith(".tar.gz"))
                Run("tar", "-xzf \"" + archive + "\" -C \"" + folder + "\"");
            else if (platform == UpdatePlatform.MacOS)
                Run("ditto", "-x -k \"" + archive + "\" \"" + folder + "\"");
            else
                System.IO.Compression.ZipFile.ExtractToDirectory(archive, folder);
        }

        private static void Run(string program, string arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using (Process run = Process.Start(start))
            {
                string errors = run.StandardError.ReadToEnd();
                run.WaitForExit();
                if (run.ExitCode != 0)
                    throw new IOException(program + " failed (" + run.ExitCode + "): " + errors.Trim());
            }
        }

        //----------------------------------------Downloading----------------------------------------//
        // The file at that path, downloaded unless there already, then checked: false (with the problem told) when it
        // can't be had or doesn't match the release
        private async UniTask<bool> Fetch(UpdateFile file, string path, CancellationToken token)
        {
            if (!(System.IO.File.Exists(path) && new FileInfo(path).Length == file.size))
            {
                State = Stage.Downloading;
                Received = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                using (request = new UnityWebRequest(file.url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(path) { removeFileOnAbort = true }, null))
                {
                    UnityWebRequestAsyncOperation sent = request.SendWebRequest();
                    while (!sent.isDone)
                    {
                        if (token.IsCancellationRequested)
                        {
                            request.Abort();
                            throw new OperationCanceledException(token);
                        }
                        Received = (long)request.downloadedBytes;
                        await UniTask.Yield();
                    }
                    Received = (long)request.downloadedBytes;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Fail("The download failed: " + request.error + ".", true);
                        return false;
                    }
                }
                request = null;
            }

            State = Stage.Checking;
            string expected = file.sha256.ToLowerInvariant();
            string actual = await UniTask.RunOnThreadPool(() => Sha256(path), cancellationToken: token);
            if (new FileInfo(path).Length != file.size || actual != expected)
            {
                System.IO.File.Delete(path);
                GameLog.Event(LogCategory.Game, "update_mismatch", "expected", expected, "actual", actual);
                Fail("The download doesn't match the release (its checksum differs): try again later.", true);
                return false;
            }
            return true;
        }

        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = System.IO.File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private void Fail(string problem, bool offer_page)
        {
            State = Stage.Failed;
            Problem = problem;
            OfferPage = offer_page;
            GameLog.Event(LogCategory.Game, "update_problem", "problem", problem);
        }

        //----------------------------------------The web----------------------------------------//
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void GwentReload();
#endif

        private static void Reload()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            GwentReload();
#else
            Application.OpenURL(UpdateCheck.ReleasePage);
#endif
        }
    }
}
