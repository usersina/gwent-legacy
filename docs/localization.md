# Localization

[Docs](README.md) › Localization

The game speaks English, Spanish (neutral Latin American Spanish) and Russian, through Unity's Localization package
(`com.unity.localization`). It starts in English, never in the system's language; **Settings > Game > Language** changes it at
once, and the game keeps the choice. For a build or a test, `-language=es` (or `ru`) on the command line picks the language at start.

## Contents

- [What is translated](#what-is-translated)
- [Where the texts live](#where-the-texts-live)
- [Adding or changing a text](#adding-or-changing-a-text)
- [Adding a language](#adding-a-language)
- [Checking it](#checking-it)

## What is translated

The interface (menus, battle, deck builder, settings, LAN lobby, the story's screens), the cards (names and descriptions) and the
How to play page. Left in English on purpose:

- **The story's content** (`Resources/Story/story.json`: the places, the quests, the players' lines).
- **What the rules assembly says** (`Gwent.Rules` cannot reach the localization package): the messages of `DeckRules`,
  `LeaderRules`, `StoryProgress` and the like, and `Card.DisplayName` and `Factions.DisplayName/Ability`, which tests compare.
  What a player reads goes through `CardText` (`card.LocalName()`, `faction.LocalName()`, `faction.LocalAbility()`) instead.
- **Everything that travels or is kept**: the LAN messages (`MatchWire`, `FairPlay`), replays (`MatchRecord`; the names a replay
  holds are kept as saved), the game log, `PlayerPrefs` keys, deck file names. Two players on a LAN may use different languages:
  only a quick chat line's number travels, and each device says it in its own language.
- **Names that code looks up** (`name="Item Play"` in UXML, the rows of the settings): an element keeps its English name in every
  language, and its text is set apart from it.

## Where the texts live

```text
Assets/Localization/
  Source/UI.csv, Cards.csv, Rules.csv   ← where texts are written and translated
  Tables/                               ← made from Source by Build Tables
  Locales/, LocalizationSettings.asset
```

Each CSV is one table and has a column per language after the key, `key,en,es,ru` (RFC 4180, UTF-8: a text with a comma or a line break goes in quotes):

| Table | `Loc` | What it holds | Keys |
| --- | --- | --- | --- |
| `UI` | `Loc.UI` | Everything on a screen but the cards and the rules page | `screen.element`: `menu.play`, `battle.pass`, `settings.language` |
| `Cards` | `Loc.Cards` | The cards' names, the abilities' and special cards' descriptions, the leaders' abilities | A card's name is its `_idstr` in `cards_db.json` (the JSON is not touched); descriptions are `ability.spy.text`, `leader.foltest_king`… |
| `Rules` | `Loc.Rules` | The How to play page | `rules.<topic>.name`, `.intro`; `rules.<topic>.<entry>.title`, `.text` |

**Gwent > Localization > Build Tables** (`GwentLocalization.cs`) makes the settings, the locales, the tables and the fonts' fallbacks (see below) from the CSVs
and logs how many entries it made. Run it every time a CSV changes; the tables it writes are committed. An entry gone from its CSV
leaves its table. The tables are preloaded, so a lookup is answered at once, on the web too (which cannot wait for a load);
the main menu waits for `Loc.Ready()` before it shows.

## Adding or changing a text

1. Add a row to the right CSV: a key, then the text in every language. Keys are lower case with dots and underscores.
2. Show it with `Loc.T(Loc.UI, "menu.play")`; `{0}`, `{1}` are filled by the arguments (`Loc.T(Loc.UI, "end.cards", count)`).
   A UXML text can be bound instead: `<Bindings><l:LocalizedString property="text" table="UI" entry="menu.play" /></Bindings>`
   inside the element (keep its `name`).
3. Run **Build Tables**.

What to know:

- **One key per wording.** Where a gender, a number or the input changes the whole sentence (`battle.leader_hint` and
  `battle.leader_hint_touch`, `rules.board.discard.text.touch` and `.pointer`), write a key for each; do not leave a hole in a
  sentence for a word. A text is a whole sentence, never put together from pieces.
- **Placeholders** are `{0}` for a value and `{Enter}` for a key drawn as a keycap. Keep every one in the translation; do not mix
  the two in a text that takes arguments (`Loc.T` with no arguments does not format).
- **A missing key** shows as the key itself and logs `Localization: no text for <table>/<key>`.
- **Text that is also an identifier** (a row named after its label, a tab named after its title): keep an English id for the
  name, and look the shown text up apart (see `RulesPage`: `Entry.Id` and `Entry.TitleKey`).
- **Cards**: a screen shows `card.LocalName()`, not `card.name`. A new card needs its row in `Cards.csv`; a card with a
  description needs its text keys in `CardDescription`.
- **Comments in the code** stay in English; texts are only in the CSVs.

## Adding a language

1. Add its column to the three CSVs (`key,en,es,ru,fr`), translating every row.
2. Add its code to `Codes` in `Assets/Editor/GwentLocalization.cs` (`{ "en", "es", "ru", "fr" }`) and run **Build Tables**. The language
   appears in Settings under its own name (`CultureInfo.NativeName`).
3. Make sure the game's fonts can draw its letters. The font assets in `Assets/Fonts` are static atlases of Latin letters; each has
   a dynamic fallback, `<font> Cyrillic SDF.asset`, made by Build Tables from a font that has the letters (`Fallbacks` in
   `GwentLocalization.cs`: Alegreya Sans for itself, Cormorant SC for Cinzel, which has no Cyrillic). A script none of them covers
   (Greek, Chinese…) needs a font file with it and a fallback of its own there; check a font's letters before choosing it.
4. Add its code to `Languages` in `LocalizationTests`: every row must then have a text in it, with the English's placeholders.

## Checking it

`Assets/Tests/Content/LocalizationTests.cs` runs with the EditMode tests (see [Testing](testing.md#unit-tests)) and reads the CSVs,
not the built tables, so a text is checked before Build Tables is run. It fails when:

- a CSV's header is not `key,en,es,ru`, a row has another shape or an empty text in any language, or repeats a key;
- a translation loses or adds a placeholder (`{0}`, `{Enter}`…);
- the code looks up (`Loc.T(Loc.<Table>, "literal.key")`) a key its table lacks, or the rules page lists a topic or entry whose
  keys the `Rules` table lacks, or a quick chat line has no text. A key put together at run time (`"battle." + name`) is not seen;
- a card has no name in `Cards.csv`, or a description key `Cards.csv` lacks (`CardDescriptionTests`).

By hand: switch the language in Settings and walk the screens; `-language=es` or `-language=ru` starts a build in Spanish or Russian.
