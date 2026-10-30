# Gwent Legacy

<p align="center">
  <img src="Screenshots/banner.jpg" alt="The main menu: Geralt and Ciri playing Gwent">
</p>

Gwent, the card game of The Witcher 3: Wild Hunt (the original in-game version, not the standalone Gwent), remade in Unity for
Windows, macOS, Linux, Android and the web.

**[Play it in your browser](https://usersina.github.io/gwent-legacy/)**, or [download it](#download).

## About

This is a fan remake of Gwent, the famous card game of The Witcher 3. I'm aware that CD PROJEKT RED made an official standalone
Gwent, but it plays very differently. I'm enthusiastic about the original version and its mechanics, which is one of the reasons I
got so immersed in the world of The Witcher 3. So here it is, made nearly from scratch in the Unity game engine.

- **The Witcher 3's rules**, all five factions, Skellige (Blood and Wine) included, covered by automated tests
- **An AI opponent** with three difficulties (Easy, Normal, Hard) and 11 decks
- **A deck builder** with full deck editing
- **Multiplayer on your network** (Multiplayer > Local network): two players on the same network, on any mix of Windows, macOS,
  Linux and Android, with chat, a turn clock and rematches (online, through a server: not yet)

## Download

The latest release:

| Windows                                                                                                      | macOS                                                                                                      | Linux                                                                                                            | Android                                                                                                      | Web                                                                                                                                                                        |
| ------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Download (.zip)](https://github.com/usersina/gwent-legacy/releases/latest/download/GwentLegacy-Windows.zip) | [Download (.zip)](https://github.com/usersina/gwent-legacy/releases/latest/download/GwentLegacy-macOS.zip) | [Download (.tar.gz)](https://github.com/usersina/gwent-legacy/releases/latest/download/GwentLegacy-Linux.tar.gz) | [Download (.apk)](https://github.com/usersina/gwent-legacy/releases/latest/download/GwentLegacy-Android.apk) | [Play in the browser](https://usersina.github.io/gwent-legacy/) · [Download (.zip)](https://github.com/usersina/gwent-legacy/releases/latest/download/GwentLegacy-Web.zip) |

Windows and macOS ask once before running a game that isn't signed by a known developer
([how to allow it](docs/builds-and-releases.md#platform-notes)). The web version plays everything but multiplayer; its download
is a folder to put on any static host.

## Screenshots

<h3 align="center">Northern Realms vs Monsters, the deciding round</h3>

![Northern Realms against Monsters in the third round](Screenshots/battle.jpg)

<h3 align="center">The redraw, and the end of the battle</h3>

<p align="center">
  <img src="Screenshots/redraw.jpg" width="49%" alt="Choosing a card to redraw at the start of the battle">
  <img src="Screenshots/victory.jpg" width="49%" alt="The victory screen with every round's score">
</p>

<h3 align="center">Choosing an opponent, building a deck</h3>

<p align="center">
  <img src="Screenshots/opponents.jpg" width="49%" alt="The opponent picker: 11 decks across the five factions">
  <img src="Screenshots/deck-builder.jpg" width="49%" alt="The deck builder on a Northern Realms preset">
</p>

## Getting started

**Playing**: **Play** > **Single match**, then pick an opponent (or **Story**: The Witcher 3's Gwent across its lands, collecting the cards). Click a card, then a row, to play it; hold Space (or the button) to pass the round.
The main menu's **How to play** teaches the rules, and [How to play](docs/how-to-play.md) lists every control.

**Developing**: open `GwentCardGame/` with **Unity 6.6 (6000.6.5f1)**. Run the tests from Window > General > Test Runner >
EditMode, and build every platform with **Gwent > Build > All platforms**. See [Development](docs/development.md).

## Documentation

The [docs](docs/README.md) hold the full details:

| Page                                               | What's in it                                                                                     |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| [How to play](docs/how-to-play.md)                 | Every control, on the desktop and on Android                                                     |
| [Rules](docs/rules.md)                             | How the game settles what the cards leave open, and where it differs from The Witcher 3          |
| [The AI](docs/ai.md)                               | The three difficulties and the AI's decks                                                        |
| [Play on LAN](docs/lan.md)                         | The lobby, the turn clock, chat, lost connections, rematches, [fair play](docs/lan-fair-play.md) |
| [Development](docs/development.md)                 | The project's layout and where the game keeps its data                                           |
| [Testing](docs/testing.md)                         | Unit tests, the autoplay and monkey tests, LAN bots                                              |
| [The game log](docs/game-log.md)                   | Every event as a line of JSON                                                                    |
| [Builds and releases](docs/builds-and-releases.md) | Building every platform, the build number, releasing                                             |
| [Changelog](CHANGELOG.md)                          | What each release brought                                                                        |

## Credits and license

I don't claim ownership of the game idea, the card art or any other material from The Witcher 3: Wild Hunt; all credit goes to its
owner, CD PROJEKT RED. This is a non-commercial fan project.

The source code is released under the [MIT License](LICENSE); it does not cover the Gwent/Witcher card art, names and other
CD PROJEKT RED material. The card art and rules come from [matt77hias/Gwent](https://github.com/matt77hias/Gwent); everything else
the game builds on is in [Credits](docs/credits.md).
