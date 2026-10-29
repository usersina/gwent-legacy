# Development

[Docs](README.md) › Development

## Contents

- [Getting started](#getting-started)
- [Project layout](#project-layout)
- [Where the game keeps its data](#where-the-game-keeps-its-data)
- [AI agents (MCP)](#ai-agents-mcp)

## Getting started

Open `GwentCardGame/` with **Unity 6.6 (6000.6.5f1)**. The project was upgraded from 2019.4; the last 2019.4 commit is `a228947`.
Rendering uses the Universal Render Pipeline (URP 17.6, settings in `Assets/Settings`). The game is all UI, so HDR, MSAA, shadows
and post-processing are off.

The packages it needs (UniTask, LitMotion, ParticleEffectForUGUI, Unity Transport...) come through the package manager
(`Packages/manifest.json`). Then see [Testing](testing.md) and [Builds and releases](builds-and-releases.md).

## Project layout

Everything in `Assets/Scripts` but the rules is the `Gwent.Game` assembly; the editor tools in `Assets/Editor` are
`Gwent.EditorTools`.

| Folder | What's inside |
| --- | --- |
| `Assets/Scripts/Rules` | The game rules as plain C# with no Unity dependency (`Gwent.Rules` assembly): card data (factions, rows and abilities are enums; the JSON files are read through `CardEntry` and `DeckFile`, which keep their codes), strength calculation (weather, tight bond, morale, horns), scorch, muster, medic, agile, rounds, deck validation and the AI's decisions (`AiBrain`, with Hard's look-ahead in `AiSearch` and `BattleSim`). `Match` runs a whole battle without a screen: the sides act through commands, which it checks, and each step is an event a screen shows at its own pace, each side seeing it its own way; `MatchWire` writes them for the network, `FairPlay` checks a LAN battle's record, and `MatchRecord` keeps a battle as a replay (its setup, its moves and a fingerprint of its events, which a build that plays them otherwise does not match) |
| `Assets/Scripts/Battle` | The battle (`Gwent.Battle`): `BattleController` shows the match's events and turns the player's input into commands, `AiPlayer` carries out the AI's decisions at the difficulty of the settings (a story opponent's own; Hard thinks on another thread while the turn's notice shows); `Board/`: `BoardView` draws the board from the model, `CardLane` lays out the hand and the rows in The Witcher 3's order, and `BoardPresenter` plays each play as The Witcher 3 does (the showcase, the card gliding in from the board's edge, a hero's fire, ability flares, scorch flames, the medic's sign, cards flying from the deck, a lost round's gem breaking) before the turn passes; `Cards/`, `Field/`, `Pickers/`: the card objects, the rows, weather, leaders, decks and side panels, and the redraw and card pickers. A LAN battle's own pieces: `ChatView` (the quick chat and the chat window), `TurnFuse` (the turn clock's last seconds), `LanNotice` (the other player away) |
| `Assets/Scripts/Net` | LAN play: `LanLink` (the link, over Unity Transport), `LanDiscovery` (finding battles on the network), `LanSession` (a LAN battle from the lobby to its end: the host's match, the guest's view, coming back after a lost connection, the turn clock, rematches, talk, fair play), `LanSave` (a battle kept on disk while it is on), `Chat` and `PlayerNames` (what can be said and named); `LanProbe` and `LanBot` test the connection and play whole LAN battles (see [Testing](testing.md#lan-tests-and-bots)) |
| `Assets/Scripts/Replays` | Replays (`Gwent.Replays`): `ReplayLibrary` (the replays saved on disk: saving, listing, deleting) and `ReplayPlayer` (the replay watched: its pace, a move or a round forward and back, across the battle screen's loads); the battle screen shows it (`BattleController.Replay.cs`, `ReplaySource`, `ReplayBar`) and the main menu lists them (`ReplaysPage`) |
| `Assets/Scripts/Rules/Story` | The story's rules, plain C# too: `StoryFile` (the content, `Resources/Story/story.json`: regions, the places on their maps, the Gwent quests, the starter deck, the pool of random cards), `StoryState` (a save), `StoryProgress` (what a win, a purchase or a find gives, what is open and shown, the quests, the tournaments' runs), `DeckGenerator` (the opponents' decks, from their faction and tier). The deck rules take an `ICardSupply`: every copy (`AllCards`), or the story's copies owned |
| `Assets/Scripts/Story` | The story (`Gwent.Story`): `StoryGame` (the story played, its save: `StorySave`), `StoryScreen` (the map scene: `MapView` drags and zooms The Witcher 3's map, the markers on it), `StorySheet` (a place: Play, Buy), `StoryPages` (the journal, the collection, travel, the start), `StoryBattle` (a battle of the story, from the map and back; its outcome saved as soon as the match is over). `Core/DeckContext` points the deck builder and the battle at the story's collection, decks and battle deck |
| `Assets/Scripts/DeckBuilding` | The deck builder (`Gwent.DeckBuilding`): `DeckBuilder`, the card grids, the faction header, the deck's summary and the leader carousel (`LeaderChoice`) |
| `Assets/Scripts/Menus` | Main menu, opponent picker, How to play, settings screen, pause menu, the LAN lobby (`Gwent.Menus`), in UI Toolkit: each binds its UXML document (`Assets/UI/Toolkit`) |
| `Assets/Scripts/UI` | The interface (`Gwent.UI`): theme (`Resources/UITheme.asset`: fonts, colors, sprites, the UI Toolkit documents), `UIScreens` (a document on a Panel Renderer: menus at sort order 400, dialogs at 600, above the uGUI layer at 500; keyboard focus), the menus' controls (`MenuEntry`, `DialogButton`, `Switch`, `Stepper`, `ElementTooltip`), the uGUI kit the battle and the deck builder use, the overlay (Escape and the dialogs' stack, confirmations, toasts), the card carousel, the cursor |
| `Assets/Scripts/Core` | Settings, audio, input, deck files and the battle deck, the card database, the AI's decks, logging, the build's number (`BuildInfo`), the cards' full art (`CardArt`) and the choice of censored art (`CensoredArt`) (`Gwent.Core`) |
| `Assets/Editor/UISpriteGenerator.cs` | Draws the UI sprites (`Assets/UI/Sprites`) and the board pieces (`Assets/UI/Board`: portrait frame, card shadow, the glow ring of the battle's highlights, row frames, the rows' emblems shown on empty rows, the table; and `Assets/Resources/UI/Rules`: the How to play page's ability medallions, **Gwent > UI > Regenerate Rules Page Art**; the gems and score discs are The Witcher 3's art): **Gwent > UI > Regenerate Sprites and Theme**. The effects' particle textures: **Gwent > UI > Regenerate Particle Art** (`ParticleArtGenerator.cs`) |
| `Assets/UI/Toolkit` | The menus' UXML documents and `Gwent.uss`: The Witcher 3's look as USS (the theme's colors as variables, the entries' and buttons' states with transitions), the `GwentTheme.tss` theme and the `Menus` panel settings |
| `Assets/Content` | Loaded on demand through Addressables, a bundle apiece: each card's full art (`CardArt`, shown by `CardArt.Show`) and the music (`Music`). `Assets/Editor/GwentContent.cs` makes the Addressables groups from these folders (**Gwent > Content > Update Addressables**; every build runs it) |
| `Assets/Resources/Cards` | `cards_db.json` (every card's stats, including how many copies The Witcher 3 has of it), the pictures on the board's cards, the card backs and icons (their sizes per platform: `Assets/Editor/CardTextureImport.cs`, see [Builds and releases](builds-and-releases.md#platform-notes)) |
| `Assets/Resources/Decks` | `player_default.json` (the built-in deck), `__empty__.json`, and the AI's 11 decks in `AI/` |
| `Assets/Resources/Story`, `Assets/Content/Story` | The story's content (`story.json`) and maps (The Witcher 3's, as witcher3map extracted them: see `Assets/Content/Story/CREDITS.md`). Both are made by tools: `tools/story-content.py fetch <cache>` reads the Witcher wiki (each card's source, the Gwent players) and witcher3map's markers, `tools/story-content.py build <cache> <draft.json> GwentCardGame/Assets/Resources/Story/story.json` joins them with the hand-kept tables in the tool (the regions, the players, the quests) and checks that every copy of every card has one source; `tools/story-maps.py <tiles>` stitches witcher3map's tiles into the maps and writes their geometry into `story.json`. `Assets/Editor/StoryScene.cs` makes the map scene |
| `Assets/Resources/W3` | The Witcher 3's interface art: notices' emblems, ability effects, the end of battle emblems (see `CREDITS.md`) |
| `Assets/Tests/EditMode` | Unit tests for the rules and the AI's difficulties, a fuzz test that plays hundreds of random battles, and the story's (its content, a play-through to Collect 'Em All, and a balance report run by name: see [Testing](testing.md#unit-tests)) |
| `Assets/Tests/Net` | LAN tests: two players on this computer over its loopback network (finding a game, joining it, the messages, a link's end) |
| `Assets/Tests/Content` | Checks of the content: every card has its art and the description box it should (with its icon), every track its label, each group holds its whole folder, nothing is also in Resources |
| `Assets/Editor/GwentAutoplay.cs` | End-to-end smoke test that plays full battles |
| `Assets/Editor/GwentMonkey.cs` | Monkey test: battles under a storm of random input |
| `Assets/Editor/GwentBuild.cs`, `BuildStamp.cs` | The release builds, and the build's number they carry (see [Builds and releases](builds-and-releases.md)) |

## Where the game keeps its data

- **Decks**: `Application.persistentDataPath/Decks/<faction>/<file name>.json`, the file name being the deck's name in lower case
  with anything unsafe removed (`My Deck!` is `my_deck.json`). On the web, in the browser.
- **Settings** (and your LAN name): `PlayerPrefs`.
- **A LAN battle under way**: `Application.persistentDataPath/lan-battle-host.bin` or `lan-battle-guest.bin`, gone once the
  battle is over (see [Play on LAN](lan.md#a-lost-connection)).
- **Replays**: `Application.persistentDataPath/Replays/battle-<date>.replay` (`-guest` for a LAN guest's), a `MatchRecord`
  each (about 1 to 2 KB), saved from a battle's end screen and kept until deleted. On the web, in the browser.
- **The story**: `Application.persistentDataPath/Story/story.json` (a `StoryState`: the cards owned, the crowns, who was
  beaten, what was bought, the battle deck), written after every change; its decks in `Story/Decks/<faction>/`. Start over
  moves both to `Story/Earlier/<date>/`. On the web, in the browser.
- **The game log**: in the editor and development builds only (see [The game log](game-log.md)).

## AI agents (MCP)

The project includes the experimental `com.unity.pipeline` package, which lets the Unity CLI talk to a running editor.
To use it from an MCP client such as Claude Code, register the CLI's MCP server and open the project in the editor:

```bash
claude mcp add --scope user unity-editor-mcp -- /absolute/path/to/unity mcp
```

`unity status` lists the editors the server can reach.
