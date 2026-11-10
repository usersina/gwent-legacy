using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Gwent.Net;
using NUnit.Framework;
using UnityEngine;

namespace Gwent.Content.Tests
{
    // The tables are made from Assets/Localization/Source/<Table>.csv ("key,en,es"), so these read the CSVs, not the built
    // assets: a text added in a CSV is checked before Build Tables is run. A key used in the code that no table has would
    // show on screen as the key itself.
    public class LocalizationTests
    {
        private static readonly string[] TableNames = { "UI", "Cards", "Rules" };

        private struct Row
        {
            public string Key, English, Spanish, Russian;
        }

        private static string Source(string table)
        {
            return Path.Combine(Application.dataPath, "Localization", "Source", table + ".csv");
        }

        // The rows of a CSV (RFC 4180: a field in quotes may hold commas, line breaks and doubled quotes), without the header.
        // A row of a different length is kept as it is for the shape test to report.
        private static List<List<string>> Parse(string table)
        {
            string text = File.ReadAllText(Source(table), Encoding.UTF8);
            List<List<string>> rows = new List<List<string>>();
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
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                    field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows.Skip(1).ToList();
        }

        private static List<Row> Rows(string table)
        {
            return Parse(table).Where(columns => columns.Count >= 3).Select(columns => new Row
            {
                Key = columns[0],
                English = columns[1],
                Spanish = columns[2],
                Russian = columns.Count > 3 ? columns[3] : ""
            }).ToList();
        }

        private static HashSet<string> Keys(string table)
        {
            return new HashSet<string>(Rows(table).Select(row => row.Key));
        }

        [Test]
        public void EveryRowHasAKeyAndLanguages()
        {
            foreach (string table in TableNames)
            {
                int expected = (table == "UI" || table == "Cards") ? 4 : 3;
                List<List<string>> rows = Parse(table);
                Assert.IsEmpty(rows.Where(columns => columns.Count != expected).Select(columns => string.Join("|", columns)).ToList(),
                    table + ": rows that are not " + (expected == 4 ? "key,en,es,ru" : "key,en,es"));
                Assert.IsEmpty(Rows(table).Where(row => string.IsNullOrWhiteSpace(row.Key) || string.IsNullOrWhiteSpace(row.English)).Select(row => row.Key).ToList(),
                    table + ": rows with no key or no English");
                Assert.IsEmpty(Rows(table).Where(row => string.IsNullOrWhiteSpace(row.Spanish)).Select(row => row.Key).ToList(), table + ": rows with no Spanish");
                if (table == "UI" || table == "Cards")
                    Assert.IsEmpty(Rows(table).Where(row => string.IsNullOrWhiteSpace(row.Russian)).Select(row => row.Key).ToList(), table + ": rows with no Russian");
            }
        }

        [Test]
        public void NoKeyIsTwiceInATable()
        {
            foreach (string table in TableNames)
            {
                List<string> twice = Rows(table).GroupBy(row => row.Key).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
                Assert.IsEmpty(twice, table + ": keys that appear more than once");
            }
        }

        // {0} is filled with the arguments, {Enter} and the like are drawn as keys: a translation that loses one, or makes one up,
        // shows broken or throws when formatted
        [Test]
        public void TheSpanishKeepsTheEnglishsPlaceholders()
        {
            Regex placeholder = new Regex(@"\{[^{}]+\}");
            List<string> differ = new List<string>();
            foreach (string table in TableNames)
                foreach (Row row in Rows(table))
                {
                    string[] english = placeholder.Matches(row.English).Cast<Match>().Select(match => match.Value).OrderBy(value => value).ToArray();
                    string[] spanish = placeholder.Matches(row.Spanish).Cast<Match>().Select(match => match.Value).OrderBy(value => value).ToArray();
                    if (!english.SequenceEqual(spanish))
                        differ.Add(table + "/" + row.Key + ": " + string.Join(" ", english) + " vs " + string.Join(" ", spanish));
                }
            Assert.IsEmpty(differ, "Rows whose placeholders differ between the languages");
        }

        [Test]
        public void TheRussianKeepsTheEnglishsPlaceholders()
        {
            Regex placeholder = new Regex(@"\{[^{}]+\}");
            List<string> differ = new List<string>();
            foreach (string table in new[] { "UI", "Cards" })
                foreach (Row row in Rows(table))
                {
                    string[] english = placeholder.Matches(row.English).Cast<Match>().Select(match => match.Value).OrderBy(value => value).ToArray();
                    string[] russian = placeholder.Matches(row.Russian).Cast<Match>().Select(match => match.Value).OrderBy(value => value).ToArray();
                    if (!english.SequenceEqual(russian))
                        differ.Add(table + "/" + row.Key + ": " + string.Join(" ", english) + " vs " + string.Join(" ", russian));
                }
            Assert.IsEmpty(differ, "Rows whose placeholders differ between English and Russian");
        }

        // The code's lookups: Loc.T(Loc.<Table>, <key>...). The keys that sit as literals in the first argument are checked (also
        // either side of a "?:"); an argument that puts a key together ("battle." + ...) is only known when it runs, so it is not
        private static IEnumerable<(string Table, string Key, string File)> NamedKeys()
        {
            Regex lookup = new Regex(@"Loc\.T\(\s*Loc\.(UI|Cards|Rules)\s*,\s*");
            foreach (string file in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
            {
                string code = File.ReadAllText(file);
                foreach (Match match in lookup.Matches(code))
                {
                    string argument = FirstArgument(code, match.Index + match.Length);
                    if (argument.Contains("+"))
                        continue;
                    foreach (Match literal in Regex.Matches(argument, "\"([^\"]+)\""))
                        yield return (match.Groups[1].Value, literal.Groups[1].Value, Path.GetFileName(file));
                }
            }
        }

        // The code from start up to the first ',' or ')' outside any brackets and strings
        private static string FirstArgument(string code, int start)
        {
            int depth = 0;
            bool inString = false;
            for (int i = start; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    if (c == '\\')
                        i++;
                    else if (c == '"')
                        inString = false;
                }
                else if (c == '"')
                    inString = true;
                else if (c == '(' || c == '[' || c == '{')
                    depth++;
                else if ((c == ')' || c == ']' || c == '}') && depth > 0)
                    depth--;
                else if (depth == 0 && (c == ',' || c == ')'))
                    return code.Substring(start, i - start);
            }
            return code.Substring(start);
        }

        [Test]
        public void EveryKeyTheCodeLooksUpIsInItsTable()
        {
            Dictionary<string, HashSet<string>> tables = TableNames.ToDictionary(name => name, Keys);
            List<string> missing = NamedKeys().Where(named => !tables[named.Table].Contains(named.Key)).Select(named => named.Table + "/" + named.Key + " (" + named.File + ")").Distinct().ToList();
            Assert.IsEmpty(missing, "Keys the code looks up that the table lacks");
            Assert.Greater(NamedKeys().Count(), 100, "The scan found hardly any lookups: has Loc.T changed shape?");
        }

        // The rules page builds its keys from the topics and entries it lists (E("Spy", "abilities.spy", ...) is rules.abilities.spy.title
        // and .text), so the lists are read from the source
        [Test]
        public void EveryKeyTheRulesPageBuildsIsInTheRulesTable()
        {
            string code = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "Menus", "RulesPage.cs"));
            HashSet<string> keys = Keys("Rules");
            List<string> wanted = new List<string>();
            foreach (Match entry in Regex.Matches(code, @"\bE\(""[^""]+"", ""([^""]+)"""))
                wanted.AddRange(new[] { "rules." + entry.Groups[1].Value + ".title", "rules." + entry.Groups[1].Value + ".text" });
            foreach (Match entry in Regex.Matches(code, @"\bEV\(""[^""]+"", ""([^""]+)"""))
                wanted.AddRange(new[] { "rules." + entry.Groups[1].Value + ".title", "rules." + entry.Groups[1].Value + ".text.touch", "rules." + entry.Groups[1].Value + ".text.pointer" });
            foreach (Match topic in Regex.Matches(code, @"\n\s+Key = ""([^""]+)"""))
                wanted.Add("rules." + topic.Groups[1].Value + ".name");
            foreach (Match intro in Regex.Matches(code, @"IntroKey = ""([^""]+)""( \+ input)?"))
            {
                if (intro.Groups[2].Success)
                    wanted.AddRange(new[] { intro.Groups[1].Value + "touch", intro.Groups[1].Value + "pointer" });
                else
                    wanted.Add(intro.Groups[1].Value);
            }
            // A key put together ("rules." + ...) is not a key by itself: the lookups with a whole one
            foreach (Match key in Regex.Matches(code, @"Loc\.T\(Loc\.Rules, ""([^""]+)""(?!\s*\+)"))
                wanted.Add(key.Groups[1].Value);
            Assert.Greater(wanted.Count, 100, "The scan found hardly any rules: has RulesPage changed shape?");
            Assert.IsEmpty(wanted.Where(key => !keys.Contains(key)).Distinct().ToList(), "Keys the rules page builds that the Rules table lacks");
        }

        // Chat.Line says a quick line by its number
        [Test]
        public void EveryQuickChatLineIsInTheUITable()
        {
            HashSet<string> keys = Keys("UI");
            Assert.IsEmpty(Enumerable.Range(0, Chat.Lines.Length).Select(number => "chat.quick_" + number).Where(key => !keys.Contains(key)).ToList(), "Quick lines the UI table lacks");
        }
    }
}
