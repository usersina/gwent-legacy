using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Gwent.Updates
{
    // Whether a newer build of the game is out: the update feed (UpdateFeed) read once a session, as the main menu first
    // opens, unless Settings > Check for updates is off. Never in the editor (its builds aren't released) unless a feed is
    // given. The feed is the game's page's (FeedUrl; in a browser, the one beside the page), or for tests and tools another
    // one: -updateFeed <url> on the command line, or the first line of update-feed.txt in the game's data folder
    // (Application.persistentDataPath; a file:// one works too). Found is raised when a newer build is known (Available).
    public static class UpdateCheck
    {
        public const string FeedUrl = "https://usersina.github.io/gwent-legacy/version.json";
        public const string ReleasePage = "https://github.com/usersina/gwent-legacy/releases/latest";
        private const int TimeoutSeconds = 15;

        private static bool started;

        // A feed for the tools (the autoplay): it wins over the others, in the editor too
        public static string FeedOverride;

        // The newer build, once found (null: none, or not known yet)
        public static UpdateFeed Available { get; private set; }

        public static event Action Found;

        // Looks for a newer build, once a session
        public static void Begin()
        {
            if (started || !GameSettings.CheckForUpdates)
                return;
            string url = Feed();
            if (url == null)
                return;
            started = true;
            Run(url).Forget();
        }

        // Forgets what was found: the next Begin looks again (the tools)
        public static void Reset()
        {
            started = false;
            Available = null;
        }

        // The feed to read, null for none (the editor without one given)
        public static string Feed()
        {
            if (!string.IsNullOrEmpty(FeedOverride))
                return FeedOverride;
            string[] arguments = Environment.GetCommandLineArgs();
            int at = Array.LastIndexOf(arguments, "-updateFeed");
            if (at >= 0 && at + 1 < arguments.Length && arguments[at + 1].Length > 0)
                return arguments[at + 1];
            try
            {
                string file = Path.Combine(Application.persistentDataPath, "update-feed.txt");
                if (File.Exists(file))
                {
                    string line = File.ReadAllText(file).Trim().Split('\n')[0].Trim();
                    if (line.Length > 0)
                        return line;
                }
            }
            catch (Exception)
            {
                // no data folder to read: the game's page then
            }
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return PageFeed(Application.absoluteURL);
            return Application.isEditor ? null : FeedUrl;
        }

        // In a browser, the feed beside the page (its site's own: GitHub Pages publishes it with the web build; a copy
        // served elsewhere without one shows no notice)
        public static string PageFeed(string page)
        {
            if (string.IsNullOrEmpty(page))
                return FeedUrl;
            int end = page.IndexOfAny(new[] { '?', '#' });
            if (end >= 0)
                page = page.Substring(0, end);
            return page.Substring(0, page.LastIndexOf('/') + 1) + "version.json";
        }

        private static async UniTaskVoid Run(string url)
        {
            // Not from a cache on the way (a browser's, the page's CDN): the feed as it is now
            string fetched = url.StartsWith("http") ? url + (url.Contains("?") ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() : url;
            using (UnityWebRequest request = UnityWebRequest.Get(fetched))
            {
                request.timeout = TimeoutSeconds;
                try
                {
                    await request.SendWebRequest();
                }
                catch (UnityWebRequestException)
                {
                    // offline, or the page unreachable: no notice (said in the log)
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    GameLog.Event(LogCategory.Game, "update_check", "result", "unreachable", "error", request.error);
                    return;
                }
                UpdateFeed feed = UpdateFeed.Parse(request.downloadHandler.text);
                if (feed == null)
                {
                    GameLog.Event(LogCategory.Game, "update_check", "result", "unreadable");
                    return;
                }
                bool newer = feed.IsNewerThan(BuildInfo.Number);
                GameLog.Event(LogCategory.Game, "update_check", "result", newer ? "newer" : "current", "latest", feed.build, "this", BuildInfo.Number);
                if (!newer)
                    return;
                Available = feed;
                Found?.Invoke();
            }
        }
    }
}
