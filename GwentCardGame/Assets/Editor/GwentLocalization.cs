using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gwent.Core;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Gwent.EditorTools
{
    // The game's languages (Gwent > Localization > Build Tables): makes the Localization package's settings, the locales and
    // the string tables in Assets/Localization from the texts in Assets/Localization/Source, one CSV a table (key,en,es,ru: the
    // key, then the text in each language). The CSVs are where texts are written and translated; the tables are made from
    // them, as many times as needed (an entry gone from its CSV leaves its table).
    //
    // The game starts in English until a language is chosen in the settings (Loc keeps it in PlayerPrefs): the startup
    // selectors are the command line (-language=es), then the language chosen, then English.
    public static class GwentLocalization
    {
        private const string Root = "Assets/Localization";
        private const string LocalesFolder = Root + "/Locales";
        private const string TablesFolder = Root + "/Tables";
        private const string SourceFolder = Root + "/Source";
        private const string SettingsPath = Root + "/LocalizationSettings.asset";

        // The column of each language in the CSVs, and the language the game starts in
        private static readonly string[] Codes = { "en", "es", "ru" };
        private const string StartCode = "en";

        private static readonly string[] Tables = { Loc.UI, Loc.Cards, Loc.Rules };

        // The game's fonts are static atlases (ASCII and Latin-1); the letters they lack (Cyrillic) are drawn by a dynamic
        // fallback beside each, made from a font that has them: Alegreya Sans its own, Cormorant SC for Cinzel (none of its own)
        private static readonly (string Font, string Source)[] Fallbacks =
        {
            ("Assets/Fonts/Cinzel SDF.asset", "Assets/Fonts/CormorantSC-SemiBold.ttf"),
            ("Assets/Fonts/AlegreyaSans-Regular SDF.asset", "Assets/Fonts/AlegreyaSans-Regular.ttf"),
            ("Assets/Fonts/AlegreyaSans-Medium SDF.asset", "Assets/Fonts/AlegreyaSans-Medium.ttf"),
        };

        [MenuItem("Gwent/Localization/Build Tables")]
        public static void BuildTables()
        {
            LocalizationSettings settings = Settings();
            Dictionary<string, Locale> locales = Codes.ToDictionary(code => code, Locale);
            Selectors(settings);
            int entries = 0;
            foreach (string table in Tables)
                entries += Fill(table, locales);
            FontFallbacks();
            AssetDatabase.SaveAssets();
            Debug.Log("Localization: " + entries + " entries in " + Tables.Length + " tables, " + Codes.Length + " languages");
        }

        // Each font's fallback ("<font> Cyrillic SDF.asset"), made once at the font's own size, padding and render mode; its
        // glyphs are drawn as the game needs them, and cleared from the build (the source font goes with it)
        private static void FontFallbacks()
        {
            foreach ((string font_path, string source_path) in Fallbacks)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(font_path);
                string path = font_path.Replace(" SDF.asset", " Cyrillic SDF.asset");
                TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fallback == null)
                {
                    Font source = AssetDatabase.LoadAssetAtPath<Font>(source_path);
                    fallback = TMP_FontAsset.CreateFontAsset(source, (int)font.faceInfo.pointSize, font.atlasPadding, font.atlasRenderMode,
                        font.atlasWidth, font.atlasHeight, AtlasPopulationMode.Dynamic, true);
                    fallback.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(fallback, path);
                    fallback.atlasTextures[0].name = fallback.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(fallback.atlasTextures[0], fallback);
                    fallback.material.name = fallback.name + " Material";
                    AssetDatabase.AddObjectToAsset(fallback.material, fallback);
                }
                SerializedObject serialized = new SerializedObject(fallback); // not public in this TextMeshPro
                serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (font.fallbackFontAssetTable == null)
                    font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(fallback))
                    font.fallbackFontAssetTable.Add(fallback);
                EditorUtility.SetDirty(fallback);
                EditorUtility.SetDirty(font);
            }
        }

        private static LocalizationSettings Settings()
        {
            LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings != null)
                return settings;
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Root);
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "LocalizationSettings";
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            return settings;
        }

        private static Locale Locale(string code)
        {
            Locale locale = LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code));
            if (locale != null)
                return locale;
            Directory.CreateDirectory(LocalesFolder);
            locale = UnityEngine.Localization.Locale.CreateLocale(code); // the string overload names the asset, the identifier one does not
            if (string.IsNullOrEmpty(locale.name))
                locale.name = code;
            AssetDatabase.CreateAsset(locale, LocalesFolder + "/" + locale.name + ".asset");
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        // The command line, then the language chosen (Loc), then English: never the system's
        private static void Selectors(LocalizationSettings settings)
        {
            List<IStartupLocaleSelector> selectors = settings.GetStartupLocaleSelectors();
            selectors.Clear();
            selectors.Add(new CommandLineLocaleSelector());
            selectors.Add(new PlayerPrefLocaleSelector());
            selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(StartCode) });
            EditorUtility.SetDirty(settings);
        }

        // The table's entries from its CSV, in every language; returns how many keys it has
        private static int Fill(string name, Dictionary<string, Locale> locales)
        {
            string source = SourceFolder + "/" + name + ".csv";
            List<string[]> rows = Csv(File.ReadAllText(source, Encoding.UTF8));
            string[] header = rows[0];
            Dictionary<string, int> columns = Codes.ToDictionary(code => code, code => System.Array.IndexOf(header, code));
            foreach (KeyValuePair<string, int> column in columns)
                if (column.Value < 0)
                    throw new System.Exception(source + " has no " + column.Key + " column");

            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(name);
            if (collection == null)
            {
                Directory.CreateDirectory(TablesFolder);
                collection = LocalizationEditorSettings.CreateStringTableCollection(name, TablesFolder, locales.Values.ToList());
            }
            collection.SetPreloadTableFlag(true); // the game looks texts up at once, on the web too (Loc)

            HashSet<string> keys = new HashSet<string>();
            for (int r = 1; r < rows.Count; r++)
            {
                string key = rows[r][0];
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!keys.Add(key))
                    throw new System.Exception(source + ": the key " + key + " is there twice");
            }
            // Entries gone from the CSV leave the table
            foreach (SharedTableData.SharedTableEntry entry in collection.SharedData.Entries.ToList())
                if (!keys.Contains(entry.Key))
                    collection.RemoveEntry(entry.Key);

            foreach (KeyValuePair<string, Locale> locale in locales)
            {
                StringTable table = collection.GetTable(locale.Value.Identifier) as StringTable
                    ?? collection.AddNewTable(locale.Value.Identifier) as StringTable;
                int column = columns[locale.Key];
                for (int r = 1; r < rows.Count; r++)
                {
                    string[] row = rows[r];
                    if (string.IsNullOrEmpty(row[0]))
                        continue;
                    string text = column < row.Length ? row[column] : "";
                    StringTableEntry entry = table.GetEntry(row[0]) ?? table.AddEntry(row[0], text);
                    entry.Value = text;
                }
                EditorUtility.SetDirty(table);
            }
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(collection);
            return keys.Count;
        }

        // RFC 4180: commas between fields, a field in double quotes may hold commas, line breaks and "" for a quote
        private static List<string[]> Csv(string text)
        {
            List<string[]> rows = new List<string[]>();
            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                        quoted = false;
                    else
                        field.Append(c);
                }
                else if (c == '"')
                    quoted = true;
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                }
                else
                    field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }
    }
}
