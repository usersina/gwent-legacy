# Gwent Legacy documentation

[← Back to the project](../README.md)

The full details of the game and of how it is made. For a quick look at the project, see the [README](../README.md).

## Playing

- **[How to play](how-to-play.md)**: every control, on the desktop and on Android
  - [Starting a battle](how-to-play.md#starting-a-battle) · [The redraw](how-to-play.md#the-redraw) · [Your turn](how-to-play.md#your-turn) · [Looking at cards](how-to-play.md#looking-at-cards) · [Pausing, giving up and the end](how-to-play.md#pausing-giving-up-and-the-end)
  - [In a LAN battle](how-to-play.md#in-a-lan-battle) · [The deck builder](how-to-play.md#the-deck-builder) · [Your battle deck](how-to-play.md#your-battle-deck) · [Replays](how-to-play.md#replays) · [Settings](how-to-play.md#settings)
- **[Rules](rules.md)**: how the game settles what the cards leave open, and where it differs from The Witcher 3
  - [Turns and rounds](rules.md#turns-and-rounds) · [Leaders](rules.md#leaders) · [Cards](rules.md#cards) · [Skellige](rules.md#skellige) · [Differences from The Witcher 3](rules.md#differences-from-the-witcher-3)
- **[The AI](ai.md)**: the three difficulties and the AI's 11 decks
  - [Difficulties](ai.md#difficulties) · [How the difficulties compare](ai.md#how-the-difficulties-compare) · [The AI's decks](ai.md#the-ais-decks)
- **[Play on LAN](lan.md)**: battles between two devices on the same network
  - [What you need](lan.md#what-you-need) · [The lobby](lan.md#the-lobby) · [In the battle](lan.md#in-the-battle) · [The turn clock](lan.md#the-turn-clock) · [Talking](lan.md#talking)
  - [A lost connection](lan.md#a-lost-connection) · [Rematch](lan.md#rematch) · [The same build](lan.md#the-same-build) · [Networks and ports](lan.md#networks-and-ports) · [How it works](lan.md#how-it-works)
  - **[Fair play in LAN battles](lan-fair-play.md)**: what the game checks, what it can't, and the larger design (private decks)

## Making the game

- **[Development](development.md)**: getting started, the project's layout, where the game keeps its data
  - [Getting started](development.md#getting-started) · [Project layout](development.md#project-layout) · [Where the game keeps its data](development.md#where-the-game-keeps-its-data) · [AI agents (MCP)](development.md#ai-agents-mcp)
- **[Testing](testing.md)**: unit tests, the autoplay and monkey tests, LAN bots
  - [Unit tests](testing.md#unit-tests) · [Autoplay smoke test](testing.md#autoplay-smoke-test) · [Monkey test](testing.md#monkey-test) · [Touch controls in the editor](testing.md#touch-controls-in-the-editor) · [LAN tests and bots](testing.md#lan-tests-and-bots) · [From the command line](testing.md#from-the-command-line)
- **[The game log](game-log.md)**: every event as a line of JSON, for people and AI agents
  - [Where it is](game-log.md#where-it-is) · [What a line holds](game-log.md#what-a-line-holds) · [Events](game-log.md#events) · [Reading it with jq](game-log.md#reading-it-with-jq)
- **[Builds and releases](builds-and-releases.md)**: building every platform, the build number, releasing
  - [Building the game](builds-and-releases.md#building-the-game) · [Platform notes](builds-and-releases.md#platform-notes) · [The build number](builds-and-releases.md#the-build-number) · [Releases](builds-and-releases.md#releases)
- **[Changelog](../CHANGELOG.md)**: what each release brought

## About

- **[Credits](credits.md)**: the game's material, code, art, fonts and sound, and the license
