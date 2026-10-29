# Testing

[Docs](README.md) › Testing

## Contents

- [Unit tests](#unit-tests)
- [Autoplay smoke test](#autoplay-smoke-test)
- [Monkey test](#monkey-test)
- [Touch controls in the editor](#touch-controls-in-the-editor)
- [LAN tests and bots](#lan-tests-and-bots)
- [From the command line](#from-the-command-line)

## Unit tests

Window > General > Test Runner > EditMode > Run All.

- **The rules**, including a fuzz test (`RulesFuzzTests`, the harness is `RulesFuzzer`): 400 battles between random and absurd
  decks (all spies, all medics, musters, weather spam, decoys, a handful of cards...) making random moves the way the battle does,
  with every unit's strength checked against an independent copy of The Witcher 3's strength formula, and card conservation,
  scores, horn slots, the weather board, round ends and round winners checked after every move; the AI makes some of those moves,
  at every difficulty.
- **The AI**: `AiDifficultyTests` play the AI's 11 decks against each other: Normal must beat Easy and Hard must beat Normal, no
  card may be lost or made, and every deck's active leader must come into play. `AiBrainTests` check that every difficulty only
  makes legal moves, and that Hard's move does not depend on which of your unseen cards are in your hand.
- **The story**: `StoryTests` check its content (every card copy has one source, every place lies on its map, every opponent's
  deck is valid) and play it through to Collect 'Em All; `StorySaveTests` check its save. `StoryBalanceTests` is a report, run
  by name (it takes about three minutes): it plays the story through the easiest opponent first, and has the Normal AI play the
  strongest deck the cards owned make against each opponent's deck at its level, 8 battles each. It writes each opponent's win
  rate and each region's and level's to `Logs/story-balance.txt`. On 2026-10-29: about 74% in Velen's first level, 58% in its
  second, 57% against the last level's innkeepers and merchants, 39% against the masters and the tournaments (the Hard AI).
- **The content** (`Gwent.Content.Tests`, run with them): the Addressables content, and that every card has the description box it
  should, with its icon.
- **LAN** (`Gwent.Net.Tests`): two players on this computer over its loopback network, with real sockets (see
  [LAN tests and bots](#lan-tests-and-bots)).

## Autoplay smoke test

Menu **Gwent > Run Autoplay Smoke Test** (or Ctrl+Shift+Alt+G). It plays one battle with the default deck and one with a generated
deck for every leader, with a random player against the AI (cycling through the AI's 11 decks and its three difficulties), and
fails on errors in the log, on a game that stops progressing, on cards appearing or disappearing, or on a board that shows other
cards than the game.

Parts of it go through real input (a virtual mouse and keyboard at human speed): the redraw (clicks, keys, Space), card selection,
holding Space to pass, the pause menu, the question a hand on the deck asks (giving up), playing a card with the keyboard, the
touch controls (long presses on the leader, its button and a hand card; taps), the game speed and difficulty settings, the card
picker's keys (and the question its × asks), a discard pile opened in the carousel, and choosing then using the leader.

Then the deck builder opens on a test deck it made: real clicks add and remove cards (the counts follow, a hero already in the deck
is no longer offered, an 11th special is refused), go through the weather and specials tabs, change the leader, switch factions and
pick the deck in the dropdown, and its API is checked (presets: picked, never renamed, deleted or changed, an edit saved as a copy;
renaming, deleting decks, invalid and corrupt files). Last, the main menu: its settings and How to play, then **Play**, the opponent
picker and a battle with the deck chosen in the builder, the fallback to the default deck when the battle deck is invalid, and a
preset as the battle deck.

The run restores the battle deck, the opponent and the settings it changes. The report is written to `Logs/autoplay-report.txt`.

## Monkey test

Menu **Gwent > Run Monkey Test** (10 minutes). Battles between random AI decks played under a storm of random input: a virtual
mouse and keyboard click, double-click, right-click, hold, drag and scroll on whatever the screen offers and mash the game's keys
and others at any moment (during plays, banners, the redraw, pickers, the pause menu, the end screen), switching the touch
controls, the game speed and the difficulty now and then; when nothing moves for a while, a random legal move or an Escape keeps
it going.

It fails on errors in the log, cards appearing or disappearing, a board that shows other cards than the game when idle, and a game
that stops progressing. It restores every setting and deck file it may change. The report is written to `Logs/monkey-report.txt`.

## Touch controls in the editor

Menu **Gwent > Simulate Touch Controls** plays with the phone's controls using the mouse (a click is a tap, a held click is a
finger held on a card).

## LAN tests and bots

The unit tests (`Assets/Tests/Net`) put two players on this computer over its loopback network, on ports apart from the game's:
finding a game and telling whether it can be joined, a host in a battle answering only its own guest's search, messages both
ways whole and in order, another version and a second guest turned away, a guest leaving and the next one taken, a join where no
game is, and the rules for names, messages and networks.

A build can also test the connection between real devices, and play whole LAN battles with the AI on both sides. Desktop: a
command-line flag; Android: the launch extras `lan` and `address`
(`adb shell am start ... --es lan botjoin --es address 192.168.1.20`).

| Flag | What it does |
| --- | --- |
| `-lan host`, `-lan join [address]` | The connection test (`LanProbe`): the host answers searches and sends back whatever its guests send; the guest finds a game (or joins the address) and checks round trips, a large message and a burst of small ones, each step logged and shown on screen. `-lan stay [address]` joins and stays linked, to see how a lost player is noticed |
| `-lan bothost`, `-lan botjoin [address]` | A whole LAN battle played by the AI on both devices from what each screen shows (`LanBot`) |
| `-lan botrejoin` | Takes up the battle kept on disk (`-lanRejoinAs host` or `guest` when one computer keeps both) |
| `-lan botsolo` | Battles against the AI alone (`-soloBattles`, 3 by default), summed up at the end: a single player test on a device |

The bots' options: `-lanRematches` (rematches to ask for), `-lanGiveUpAfter` (gives up after that many of its moves),
`-lanStallAfter` (stops moving after that many: the turn clock's test, with the host's `-lanTurnTime` in seconds),
`-lanRedrawWait`, `-lanEmoteEvery` and `-lanChatEvery` (seconds between lines), `-lanChatBurst`, `-lanMute 1`, `-lanTalkOpen`.
Everything they do is logged as `[lan]` lines; in batch mode the game quits a few seconds after the last battle ends.

## From the command line

Close the project in the editor first. With the [Unity CLI](https://docs.unity.com) signed in (`unity auth`):

```bash
unity test GwentCardGame --mode EditMode --output editmode-results.xml
```

The story's balance report, by name:

```bash
<path-to>/6000.6.5f1/Editor/Unity -batchmode -nographics -projectPath GwentCardGame -runTests -testPlatform EditMode -testFilter Gwent.Rules.Tests.StoryBalanceTests.HowHardTheStoryIs -testResults balance.xml
```

The autoplay test needs the editor to stay open after the method returns, so it runs the editor directly (the CLI's `run` always
adds `-quit`):

```bash
<path-to>/6000.6.5f1/Editor/Unity -batchmode -nographics -projectPath GwentCardGame -executeMethod Gwent.EditorTools.GwentAutoplay.RunBatch -autoplayReport autoplay-report.txt -logFile autoplay.log
```
