# Play on LAN

[Docs](README.md) › Play on LAN

Two players on the same network battle each other: main menu > **Play** > **Multiplayer** > **Local network**. Its other way, **Online**
(through a server), is not there yet ([issue #14](https://github.com/usersina/gwent-legacy/issues/14)); in a browser, neither
is Local network: a browser can't host or find games on a network.

## Contents

- [What you need](#what-you-need)
- [The lobby](#the-lobby)
- [In the battle](#in-the-battle)
- [The turn clock](#the-turn-clock)
- [Talking](#talking)
- [A lost connection](#a-lost-connection)
- [Rematch](#rematch)
- [The same build](#the-same-build)
- [Networks and ports](#networks-and-ports)
- [How it works](#how-it-works)

## What you need

- **The same network**: the same Wi-Fi, a cable, or a phone's hotspot. Friends on the same VPN (Tailscale, ZeroTier,
  Hamachi...) can join from anywhere with the host's VPN address.
- **Any mix of Windows, macOS, Linux and Android.** Not the web version: a browser can't take part in a LAN battle. There is no
  iOS build.
- **The same build of the game** on both devices (see [The same build](#the-same-build)).

## The lobby

- **At the top, who you are**: your leader, your name (up to 16 letters, numbers, spaces and - _ . '; the other player sees it)
  and the battle deck you play ([your battle deck](how-to-play.md#your-battle-deck)), with the way to the deck builder and back.
- **Battles on your network** show as they are found, a live dot saying the lobby is searching: each with its host's name,
  faction and leader. Click one to join it. A battle of another build shows too, and can't be joined.
- **Join by address** joins a host your network hides (the host's screen shows its address).
- **Host a battle** opens your room: both players face to face, and your address to give. **Start the battle** once someone
  has joined; **Stop hosting** (or, as a guest, **Leave**) goes back.
- **Details**, by the host's address, lists all its networks (a VPN's address lets friends on that VPN join from anywhere) and
  the ports.
- A battle still on when the game closed (Android closing it in the background, a crash) is offered first: **Rejoin** it, or
  **Abandon** it. It is kept for 6 hours.
- Esc (the Android back button) goes back a step: out of a room, then out of the lobby to Multiplayer, then to the main menu.

## In the battle

- The host's game runs the battle; each screen shows only its own player's hand, the other's cards face down.
- While the other player decides (their redraw, who goes first), it shows as their turn.
- You can give up at any time (a hand on your deck, as against the AI): it counts as a loss.
- A phone's screen stays on in a room and in a battle (in any battle, against the AI too): asleep, its player would be away.

## The turn clock

Each turn has a clock, so that nobody can stall a battle (only in a LAN battle, never against the AI):

- **60 seconds a turn**, 30 for the redraw. A choice the turn brings (a medic's revival, a leader's pick) leaves at least 15.
- It counts only once your screen has shown everything (the opponent's plays and their animations are not your time), and only
  while both devices are linked: it holds while either is away, and a player coming back has at least 15 seconds.
- In the last 15 seconds, a fuse burns along the middle of the board (during the redraw, along its band), the seconds left at
  its start.
- **Out of time in a turn, you lose the round**: a gem breaks, every time. Out of time in the redraw, you keep your hand, as if you
  had pressed **Done**.

## Talking

Only in a LAN battle, and after it on the end screen:

- **Quick chat**: E (or a click on your portrait) pops up The Witcher 3's quick lines beside your portrait, numbered; 1 to 8 (or a
  click) says one.
- **The chat window**: T (or your portrait's "..." badge) opens it at the foot of the screen, over your hand: what was said this
  session (quick lines too, rematches included) and a message to type (up to 100 characters; accents are fine). The speaker mutes
  the other player for the session, the × hides the window.
- What either player says pops up in a bubble from their portrait on both screens.

## A lost connection

- A lost connection shows over the board: the other player greys out and the battle waits for them, reconnecting by itself.
  After a minute counting down (01:00, 00:59...), the host can claim the win and the guest can give up.
- A host whose address changed (a phone whose Wi-Fi came back with another address) is found again on the network: the guest
  follows it there.
- A game closed in the middle of a battle can go back to it: **Local network** says **Rejoin**, and the lobby offers it, for 6
  hours. The host takes the battle up where it was, on the same port, and its guest joins it again.

## Rematch

Once the battle is over, either player can ask for a **Rematch** on the end screen. When both have, the next battle begins with
the same decks, a new coin toss and a new shuffle.

## The same build

Both devices need the very same build of the game: the same version, built from the same source. The main menu shows yours in
its bottom right corner ("Version 1.0.0 · Build 1a2b3c4d"), and the lobby at its bottom left. A battle of another build shows in the list but can't be joined, and joining one by address says
which build it runs. See [Builds and releases](builds-and-releases.md#the-build-number) for where the number comes from.

## Networks and ports

- The game uses the UDP ports **26946 to 26959**: **26946** to find battles, **26947** for the battle itself (when another program
  holds it, the next free one up to 26956; **Details** shows which) and **26957** for the answers to a search (up to 26959 when
  another copy of the game on the device searches too).
- **Windows and macOS** ask once whether to let the game through: allow it on your private network.
- **Linux** firewalls don't ask, and most let nothing in: open the ports once. Without that, nobody finds or joins a battle the
  computer hosts, and its own list of battles stays empty.
  - firewalld (Fedora, Arch...): `sudo firewall-cmd --permanent --add-port=26946-26959/udp`, then `sudo firewall-cmd --reload`
    (for the default zone: when your network is in another, `firewall-cmd --get-active-zones` names it, add `--zone=` it)
  - ufw (Ubuntu, Mint...): `sudo ufw allow 26946:26959/udp`
- When a battle hosted on your network doesn't show in the list, join it by the address the host's screen shows. Some networks
  (guest Wi-Fi, public hotspots) keep devices apart altogether: there, use a phone's hotspot or a VPN.

## How it works

- **The link**: Unity Transport over UDP (`LanLink`), with the game's own search: a broadcast to the network every second, each
  host answering with its name, version and port (`LanDiscovery`), to the search's own port (26957), which a firewall can let
  in: an answer to a random port looks like a stranger's packet to a firewall that tracks what was asked (the question went to
  the whole network, the answer comes from one address). The battle (`LanSession`): the host runs the match (`Match`) and
  sends each event as the guest may see it; the guest sends its moves, each answered (taken or refused).
- **Coming back**: both sides say they are there every second; a side silent for 6 seconds is taken as away. The guest joins again
  every few seconds, says how much of the battle it has, and the host sends the rest (a guest silent for 3 seconds already gives
  way to a newcomer: most likely itself, its old link dead, and the newcomer still has to name the battle). Meanwhile it searches
  for its host with the battle's key (a hash of the battle's number and seed, which only the two devices know): a host that moved
  is joined at its new address, and no one else's list shows a battle under way. The battle is kept on disk while it is on
  (`LanSave`).
- **Fair play**: the shuffle comes from both devices, and once the battle is over the guest checks the host's record of it. What
  that guarantees, what it can't, and the larger design that would: [Fair play in LAN battles](lan-fair-play.md).
- **Testing it**: [LAN tests and bots](testing.md#lan-tests-and-bots).
