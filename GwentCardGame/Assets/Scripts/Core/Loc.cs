using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Gwent.Core
{
    // The game's languages, through Unity's Localization package (Assets/Localization: the settings, the locales and the
    // string tables, made from Assets/Localization/Source by Gwent > Localization > Build Tables). The game starts in English
    // until a language is chosen in the settings, and keeps the one chosen.
    //
    // The tables are preloaded: once Ready is done, a lookup is answered at once, on the web too (which cannot wait for a
    // load on the main thread).
    public static class Loc
    {
        public const string UI = "UI";
        public const string Cards = "Cards";
        public const string Rules = "Rules";

        // The Localization package's own key: its PlayerPrefLocaleSelector reads it at start
        private const string LanguageKey = "selected-locale";

        // Once the locale is chosen and its tables are loaded
        public static async UniTask Ready()
        {
            await LocalizationSettings.InitializationOperation.ToUniTask();
        }

        // The entry of the table in the current language, its {0}, {1}... filled with the arguments; the key itself if the
        // table has no such entry (seen at once on screen, and in the log)
        public static string T(string table, string key, params object[] arguments)
        {
            string text = LocalizationSettings.StringDatabase.GetLocalizedString(table, key, arguments);
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning("Localization: no text for " + table + "/" + key);
                return key;
            }
            return text;
        }

        // The languages as their own names say them ("English", "Español"), in the settings' order
        public static IReadOnlyList<Locale> Languages { get { return LocalizationSettings.AvailableLocales.Locales; } }

        public static string NameOf(Locale locale)
        {
            return locale.Identifier.CultureInfo != null
                ? Capitalized(locale.Identifier.CultureInfo.NativeName)
                : locale.LocaleName;
        }

        public static int Current
        {
            get
            {
                IReadOnlyList<Locale> languages = Languages;
                for (int i = 0; i < languages.Count; i++)
                    if (languages[i] == LocalizationSettings.SelectedLocale)
                        return i;
                return 0;
            }
        }

        // Changes the language, kept for the next launches; changed runs once the new language's tables are loaded
        public static void Set(int index, Action changed = null)
        {
            Locale locale = Languages[index];
            if (locale == LocalizationSettings.SelectedLocale)
                return;
            PlayerPrefs.SetString(LanguageKey, locale.Identifier.Code);
            LocalizationSettings.SelectedLocale = locale;
            GameLog.Event(LogCategory.Ui, "language", "code", locale.Identifier.Code);
            if (changed != null)
                AfterLoad(changed).Forget();
        }

        private static async UniTaskVoid AfterLoad(Action changed)
        {
            await Ready();
            changed();
        }

        private static string Capitalized(string name)
        {
            return string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
