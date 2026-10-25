using UnityEngine;

namespace Gwent.Updates
{
    // Android's update: the checked APK handed to Android's installer by the game's Java side
    // (Plugins/Android/GwentUpdateInstaller.java), which then asks the player to confirm
    public static class AndroidUpdate
    {
        private const string JavaClass = "com.usersina.gwentlegacy.update.GwentUpdateInstaller";

        // The install's state, as the Java side keeps it
        public const int None = int.MinValue;
        public const int Waiting = -1000;
        public const int PendingUser = -1;
        public const int Installed = 0;
        public const int Cancelled = 3;

        // Whether Android lets the game install apps (allowed once by the player, in Android's settings)
        public static bool CanInstall
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (AndroidJavaClass unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaClass installer = new AndroidJavaClass(JavaClass))
                    return installer.CallStatic<bool>("canInstall", activity);
#else
                return false;
#endif
            }
        }

        // Opens Android's page where the player allows the game to install apps
        public static void OpenInstallSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaClass installer = new AndroidJavaClass(JavaClass))
                installer.CallStatic("openInstallSettings", activity);
#endif
        }

        // Starts the install: null once handed over, else what went wrong
        public static string Install(string apk)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (AndroidJavaClass unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaClass installer = new AndroidJavaClass(JavaClass))
            {
                string problem = installer.CallStatic<string>("install", activity, apk);
                return string.IsNullOrEmpty(problem) ? null : problem;
            }
#else
            return "Not on Android";
#endif
        }

        public static int Status
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (AndroidJavaClass installer = new AndroidJavaClass(JavaClass))
                    return installer.CallStatic<int>("status");
#else
                return None;
#endif
            }
        }

        public static string Message
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (AndroidJavaClass installer = new AndroidJavaClass(JavaClass))
                    return installer.CallStatic<string>("message");
#else
                return "";
#endif
            }
        }
    }
}
