# Changelog

Every push to `main` is a release: it replaces the one GitHub release, `latest`, and the newest section below becomes its
notes. A push to `main` is only released when this file has a new section since the last release: add one, headed by the
date, before pushing.

## 2026-10-29

Story: The Witcher 3's Gwent as Geralt played it, on The Witcher 3's own maps.

### Story

- **Play > Story** begins it: The Witcher 3's Northern Realms starter deck and a leader of four factions, then its lands
  in its order: White Orchard, the royal palace in Vizima, Velen and Novigrad (Oxenfurt, and Hearts of Stone's players once
  Gwent: Skellige Style is done), Skellige, Toussaint.
- Each region is The Witcher 3's own map: drag it, zoom it (the wheel, a pinch, + and -), its Gwent players where they stand
  in the game. A marker shows gold while there is something to win, buy or find there, the quest's mark while a quest leads
  there, a tick once it's all yours.
- Every card comes as it does in The Witcher 3: a renowned player's own card when you first beat them, a random card from
  the players of no particular renown, innkeepers' and merchants' cards for crowns, the Gwent quests' leaders (Velen Players,
  Big City Players, Old Pals, Playing Innkeeps, Skellige Style…), A Dangerous Game, the masquerade's and High Stakes'
  tournaments, Count Monnier's Skellige deck and the Beauclair tournament. Nothing can be missed, and rematches are always
  there.
- Wins pay crowns; rematches pay less. Opponents get stronger along the story: the first ones play like beginners, then
  their decks grow; The Witcher 3's masters of the game (Thaler, Dijkstra, Crach an Craite, Olgierd…) and the tournaments
  play the hardest.
- A player's sheet shows their card and what they sell as cards, a price tag under each; the button says what a win pays.
- At the top of the map: the journal (J: the Gwent quests, as far as they have led), the collection (C: The Witcher 3's A
  Miraculous Guide to Gwent, a bar for every region and faction), your story decks (D: only the cards you own), travel (M).
- The story has its own save and its own decks: **Play**, **Multiplayer** and the deck builder of the main menu keep every
  card. **Start over** sets a story aside, never deletes it.

### Main menu

- **Play** asks what to play: **Story**, **Single match** (against the AI, as Play was) or **Multiplayer**. The menu is down
  to six entries.

### End screen

- The way on is one gold button with its key (Enter): **Play again**, **Rematch**, **Watch again**; beside it, round buttons
  with their names under them and their keys: **Save replay** (S; a tick once saved), **Main menu** (Esc).
- In the story, a win's cards come first, on their own and large: each turns over from its back, says whether it's new
  (or a quest's reward), the crowns count up beside them, with the quests done and the lands opened. **Collect** shows the
  scoreboard, with what you won in a line under it.

## 2026-10-27

Replays: save a battle from its end screen, and watch it again.

### Replays

- **Save replay** on the end screen keeps the battle: against the AI, and LAN battles on both devices (the guest's once its
  fair play check has passed).
- **Main menu > Replays** lists them, the newest first: who played whom, how it ended, when, both factions. They stay until
  you delete one with the cross beside it.
- Watching shows the battle from your side, at its own pace. A bar where the pass button is plays and pauses, goes a move or
  a round forward or back, and sets the speed (1×, 2×, 4×); on a computer, Space, the arrows and Shift with them do the same.
  Your own plays and leader show in the showcase on the right, as your opponent's do.
- The switch right of your hand (S on a computer) turns the board over: the same moment from your opponent's side, their
  hand shown.
- At the end: **Watch again** or back to **Replays**. Esc or Back > **Stop watching** leaves at any time.
- A replay is the battle played again from its moves: one recorded with an earlier version whose rules or cards play them
  differently says so instead of showing a battle that never was.
- How far the replay is shows along the line between the two halves of the board, a mark where each round starts.

### Battle

- The lines dividing the board (between its two halves, and along the left column) are now thin iron rods set in the
  table, as on The Witcher 3's board. A LAN battle's turn clock burns along the middle one, under the cards and their
  highlights.

## 2026-10-25 (2)

The game now tells you when a new version is out, and updates itself.

### Updates

- As the game starts, it checks for a newer release. When there is one, the main menu says so in its top right corner
  ("New version available · build 335"); it opens what's new, with the download's size, **Update** and **Later**.
- **Update** does what each platform can, and your settings and decks stay:
  - in a browser, the page reloads with the new version;
  - on Android, the game downloads the new version, checks it and hands it to Android's installer, which asks you to confirm
    (the first time, also to allow installing from the game);
  - on Windows, Linux and macOS, the game downloads the new version, checks it, then restarts as it. Only the game's own files
    are replaced.
- When it can't (no connection, a folder the game may not write to, a macOS copy not moved to Applications yet), it says why and
  offers the release's download page.
- **Settings > Game > Check for updates** turns it off.
- The main menu shows the build's number with its commit, "Build 335 (1a2b3c4d)": the commit opens its source on GitHub. The
  fan project's note moved under it, smaller.

Copies of the game from before this release don't have it: update them by hand once.

## 2026-10-25

Easier on a phone and in a browser: the web version fills the screen in landscape, the board zooms with two fingers, every
side's details are a tap away, the rules open mid-battle, and phones get larger menus and buttons. Esc and Android's Back
button now step back one thing at a time on every screen.

### In a browser

- The game fills the whole window, with no white around it. On a phone the first tap goes full screen and turns it
  sideways where the browser allows; held upright, the page asks to turn the phone. On an iPhone, a one-time note tells how
  to play full screen (Add to Home Screen).
- The page loads on the game's colours, with its crown and a gold bar, and can be added to a home screen as an app.

### Battles

- **Zoom:** on a touch screen, two fingers zoom the board in (up to 2.5 times) and move it. Zoomed in, one finger moves it
  too, and a double tap on the table zooms in there or back out. A magnifier under the menu button (or Back) brings the
  whole board back.
- **Lock:** zoomed in, a padlock under the magnifier locks the board: taps on it do nothing, to look around without playing
  a card by accident. Zooming out unlocks it.
- **A side at a glance:** a tap or click on a side's band (portrait, name, gems, score) opens its sheet: its faction's
  ability, its leader's ability and whether it can still be used, and its cards in hand, deck and discard pile.
- **The rules mid-battle:** a **?** beside the menu button opens How to play; the battle waits meanwhile (a LAN battle goes
  on).
- **Quick chat (LAN):** a speech-bubble mark on your portrait shows where the quick lines open; a finger takes the portrait,
  the mark and the chat badge over larger areas. The keys (E, T) are in their tooltips, and chat bubbles follow the portrait
  when the board is zoomed.
- Tooltips near the top of the screen open below their control instead of off the screen.

### Esc and Back, everywhere

- One press steps back one thing: a dialog or window closes, a chosen card goes back, a picker's choice is given up (asked
  first where something is lost) or a look closes, the board zooms out; then the pause menu. On the end screen it goes to
  the main menu.
- On the main menu, it asks to quit (not in a browser). It used to do nothing.
- In the deck builder it closes a card's details, the name being typed or the deck list one at a time, before going back to
  the main menu; a card's details sometimes didn't close on Esc at all.
- In the LAN lobby it closes the host's details and the join-by-address row before leaving the room or the lobby.

### On a phone

- Menus, dialogs, the chat window, the player sheet and the card pickers start a size larger, measured on the screen (a
  tablet's stay as they were), as large as the largest **Menu size**; a smaller **Menu size** still shrinks them. The
  dialogs' × is larger.
- The opponent list scrolls when it doesn't fit the screen (a phone, or the largest **Menu size** anywhere): its last row
  was cut off.
- The battle's menu button, **?**, magnifier and padlock are a size larger, and so are chat bubbles; the chat window's ×
  and mute take a finger over larger areas.
- A phone with a camera cutout shows the game to the screen's edge on that side too: it had a black strip there.
- The redraw's and the pickers' cards stay clear of their title band, which the larger cards covered.

### How to play

- Its Controls topic covers zooming, the lock, the player sheet, the **?** and stepping back with Esc or Back.

## 2026-10-20

Every screen fits a 16:10 screen, and the battle's menu is easier to reach: a bigger button at the top left, and a way to
leave a battle. Also: leaders' portraits framed on their faces, clearer questions, tooltips on the icons that had none, and a
few names no longer cut short.

### Battles

- The menu button is at the top left, and bigger. On a phone a finger takes it over a larger area, as it does the crowns
  and the pass button.
- **Leave battle** in the menu gives the battle up (it asks first: it counts as a loss) and goes back to the main menu; in a
  LAN battle the other player wins. Once the battle is over, **Main menu** takes its place.
- Hovering the empty weather slot says what it is for, and a discard pile says whose it is and how many cards it holds.
- On a phone's wide screen the table's wood covers it all: no black bands at the sides.

### Every screen

- On a screen narrower than 16:9 (16:10 laptops and MacBooks, tablets) the battle and the deck builder lost their sides:
  the menu button, the deck builder's back arrow, the side panels, the decks. The whole layout fits now, the background
  going on above and below it.
- A question's gold answer is the one Enter takes: the safe one ("Stay", "Keep playing") as it opens. The risky one used to
  look chosen.
- **Quit game** is in the pause menu on Android too, as in the main menu (where **Quit** is now **Quit game**). A browser
  has neither: a page can't close its own tab.
- The settings' arrows at their first or last option are a faint gold: once clicked there, they showed on a grey square.
- With Censored artwork on, the main menu's art also covers the woman standing behind the title, on the left.

### Deck builder

- Esc, and Android's Back button, go back to the main menu. Esc first closes the name being typed, the deck list or a card's
  details.
- The seven filter tabs fit above their list (they ran past the screen's edge), and each tells its name when hovered; so do
  the back arrow, the pencil and the trash.
- The deck list is wider: longer names show whole.

### Leaders' portraits

- Every round portrait of a leader (the opponent picker, the LAN lobby, How to play, the battle's side panels and end screen)
  is framed on the leader's face: one framing for every card had shown Eredin's portal, an arm or a burning hand. Francesca's
  covered versions have a framing of their own, and a portrait switches with the Censored artwork setting.
- The faces load while the main menu is on, and fade in: no more faction card backs swapping to faces as the picker opens.
- The opponent picker gives the faction and the leader a line each: long leaders' names showed cut short.

### Documentation

- [How to play](docs/how-to-play.md): leaving a battle, the menu button's place, the deck builder's Esc, the new tooltips.

## 2026-10-19

The game plays in your browser now, at <https://usersina.github.io/gwent-legacy/>. Also: a Multiplayer menu, a gold-edged icon,
drag and drop and a leader carousel in the deck builder, sharper cards, a Censored artwork setting (on by default), LAN fixes from a test with a real
phone, and the battle's sounds in step with what they go with.

### Play in your browser

- Every release is published on GitHub Pages: [play it in your browser](https://usersina.github.io/gwent-legacy/), nothing to
  install. Everything but multiplayer is there; decks are kept in the browser.
- The browser tab shows the crown, and a phone adding the page to its home screen gets the gold icon.

### Multiplayer

- The main menu's **Play on LAN** is now **Multiplayer**, on every platform: it offers **Local network** (the LAN lobby, as before)
  and **Online**, through a server, coming later. What can't be chosen says why: Online is coming soon, and a browser can't play on
  a local network.
- A LAN battle still on shows as **Rejoin** on Local network. Leaving the lobby comes back to Multiplayer.

### Deck builder

- Dragging a card between the collection and the deck works: drop it anywhere in the other side's column, its title, its
  cards or the space under them. The game looked for the drop in an empty spot off the screen, so only a double click
  worked.
- A card all of whose copies are in your deck stays in the collection, greyed, at x0, instead of vanishing; double-click it
  and it says why it can't be added.
- The deck's name: the name field and its + are gone. The pencil, now beside the trash, opens the deck's name in place of the
  list: Enter keeps it, Escape leaves it. Your own deck is renamed; named, a preset gives its cards to a new deck of yours.
  "+ New empty deck", at the bottom of the deck list, names a new deck with no cards the same way.
- The leader: a click on it opens the faction's leaders in the carousel of the battle's redraw, your deck's in front and its
  ability at the bottom right. Click a leader to bring it forward, click it again (or Enter) to make it your deck's leader; the
  ×, Escape or a right click leave it as it was. It replaces the list a double click opened.
- The faction's perk is now called its ability, as the battle's notices say. Nilfgaard's and Skellige's, the longer ones, wrap
  to a second line instead of running past the middle column.

### The icon

- A gold contour all around, on every platform: on Android the crown's green disc sits in gold, whatever shape the phone
  gives its icons; on macOS (Windows and Linux too) a thicker gold frame, in the size and shape macOS expects, so it no
  longer shows on a dark plate.

### Sharper cards

- On Windows, macOS and Linux the cards are about twice as sharp, up to 4K: the full art (the showcase, the pickers, the
  deck builder, the leaders' portraits), the pictures on the board and in the hand, the card backs (Skellige's too) and the
  ability and row icons.
- Android and the web get the cards at the size their screens show them, so their downloads stay small.

### Censored artwork

- **Settings > Game > Censored artwork**, on by default: covered versions of the art of the cards that show women and of
  some female monsters, and of the main menu's art.
- Switch it any time, even in the middle of a battle: every card on screen changes at once. In a LAN battle each player
  sees their own choice.

### Play on LAN

- A phone's screen stays on during a battle and in a LAN room. Left untouched through the other player's turn, it used to
  fall asleep, its player away: after a minute, the other could claim the win.
- Behind a firewall that only lets the game's ports in (Linux's firewalld and ufw let nothing in until told), the list of
  battles stayed empty: the answers to a search came back to a random port. They now come to port 26957, and the game's
  ports are UDP 26946 to 26959. [Play on LAN](docs/lan.md#networks-and-ports) gives the commands to open them.
- A player coming back to a battle (a phone whose address changed, a game restarted) is let in at once, where the host,
  still holding their old, silent link, turned them away for a few seconds.

### Battles

- A lost round's gem breaks as the result shows, to its sound; it used to break only once the notice had gone.
- A notice's sound plays as the notice shows, even one waiting behind another: "Your turn!" no longer chimes as it fades,
  nor a pass or a round's result over the notice before it (Nilfgaard winning a tie).
- The draw's sound plays as the cards fly to the hand (a spy's two, the Northern Realms' card, a leader's pick), and the
  battle's opening sound as the board shows, no longer behind the loading screen.
- A scorch leader facing a row of only heroes says so (heroes are immune), instead of asking for a strength of 10 the row has.
- Every scorch tested against every board we could think of (26 cases: heroes on top, ties, weather, horns, bonds, morale,
  spies, medics and decoys bringing a scorcher back): heroes are passed over, the strongest units that are not heroes burn.
- A faction's ability shows when the pointer rests on a side panel's shield or faction name (or a finger is held on it), for
  both sides. It was written nowhere in the battle.
- A passive leader's crown (Daisy of the Valley, the White Flame, Invader of the North, The Treacherous, King Bran) is faded
  out: always in effect, never used. It looked almost like a crown ready to use.

### Documentation

- New screenshots in the README.
- [Rules](docs/rules.md#cards): how every scorch treats heroes, ties and a row's 10. [Play on LAN](docs/lan.md#networks-and-ports):
  the ports, and how to open them on Linux.
- The README links the browser version; [Builds and releases](docs/builds-and-releases.md#releases) says how it is published.
- [How to play](docs/how-to-play.md#the-deck-builder): choosing the leader, and where to read a faction's ability.

## 2026-10-18

LAN battles grow up: talk to the other player, a turn clock, rematches, a fairer shuffle, and a build number both players
compare. The menus get a cleaner look.

### Play on LAN

- **Rematch**: once a battle is over, either player asks on the end screen; when both have, the next battle begins with the
  same decks, a new coin toss and a new shuffle. Under the buttons, what the other player is at: wants a rematch, waited for,
  away, left.
- **Talk**, as in Rocket League: E (or a click on your portrait) pops up The Witcher 3's quick lines, 1 to 8 says one; T (or
  your portrait's "..." badge) opens the chat window over your hand, with what was said, a message to type (up to 100
  characters), a speaker to mute the other player and a × to hide it. What either says pops up in a bubble from their
  portrait, on the end screen too.
- **A turn clock**: 60 seconds a turn, 30 for the redraw. It counts only once your screen has shown everything, and holds
  while either device is away. A fuse burns along the board in the last 15 seconds; out of time in a turn, you lose the
  round; in the redraw, you keep your hand. Never against the AI.
- **A fairer shuffle**: each battle's shuffle comes from both devices, so neither can choose it, and once the battle is over
  the guest's game checks the host's record of it, move by move.
- **The same build**: both players need the very same build of the game, shown in the main menu's bottom right corner and in
  the lobby. A battle of another build shows in the list but can't be joined, and says why.
- **Details**, by the address a host gives, lists every network another device can join it on (Wi-Fi, Ethernet, a hotspot,
  a VPN such as Tailscale or ZeroTier) and the ports. Friends on the same VPN join from anywhere with its address. The
  address shown no longer offers a phone's mobile data.
- A host whose address changed (a phone whose Wi-Fi came back with another) is found again, and the battle goes on.
- You can give up at any time in a LAN battle. When the other player has been away a minute, the way out says what it does:
  **Claim the win** or **Give up**. The pause menu no longer stops the battle.
- Fixes: the host no longer drops its guest at the start after a while in the room; a battle taken up again shows its
  notice at once; a join that hangs is tried again; a card played as the connection dropped goes back to your hand; a guest
  whose connection dropped as a rematch began comes back to it.
- The lobby: Escape leaves a room before the lobby, Enter in the address field joins, the room says who left, the list of
  battles scrolls, and Abandon asks first.

### A cleaner look

- Dialogs close with a × in their top right corner (or Escape), as the chat window does: no more "Esc to close", Back or
  Close.
- A card picker's Skip or Close is a × (giving a choice up still asks first). Done and the pass button show their key, Space,
  in a small cap, and the battle's notices show keys the same way.
- How to play tells the controls shorter, with two more: choosing cards and the LAN chat.

### Documentation

- The README is a quick look at the game; every detail moved to the
  [docs](https://github.com/usersina/GwentUnityClone/blob/main/docs/README.md), each page with its table of contents.

## 2026-10-16

Play against a friend on your network, the weather comes alive, a Graphics tab, loading screens, and the game is called
Gwent Legacy everywhere.

### Play on LAN

- Two players on the same network (the same Wi-Fi, or a phone's hotspot) can battle each other, on any mix of Windows,
  macOS, Linux and Android: main menu > **Play on LAN** (not in the web version; over the internet, not yet).
- The lobby lists the battles hosted on your network as it finds them, each with its host's name, faction and leader. Host
  your own, or join one by the host's address when your network hides it. Hosting, you see both players face to face and
  start the battle once someone has joined.
- Choose the name the other player sees (up to 16 letters, numbers, spaces and - _ . '). You play your battle deck; the
  lobby takes you to the deck builder and back.
- Each screen shows only its player's hand: the other's cards stay face down.
- While the other player decides (their redraw, who goes first), it shows as their turn.
- A lost connection shows over the board: the other player greys out and the battle waits for them, reconnecting by
  itself. After a minute counting down (01:00, 00:59...), the host can end the battle and the guest can leave it.
- A game closed in the middle of a battle (Android closing it in the background, a crash) can go back to it: Play on LAN
  offers to rejoin it, for 6 hours.
- Both players need the same version of the game.

### The weather comes alive

- Biting Frost, Impenetrable Fog and Torrential Rain move as The Witcher 3 paints them: the frost's ice needles snap up
  from the snow and glint, the fog billows and drifts along its row, the rain falls in fine streaks and splashes on the
  row's edge. Each stays in its row.
- Skellige Storm blows its rain sideways and churns its fog; lightning strikes every few seconds, with thunder.
- Clear Weather (and Foltest's order) sweeps the weather away in a shaft of sunlight.

### Settings

- A Graphics tab: a preset (Low, Medium, High) over how many particles the effects use, and each effect on or off: the
  animated weather (off: The Witcher 3's still pictures of it), the storm's lightning and thunder, the sun, the battle's
  effects, shine and pulsing, the menus' animations. An effect that tells you something keeps a light stand-in when it is
  off. Phones start on Medium, the rest on High.
- The settings open on Game; the tabs are Game, Display, Graphics, Audio.

### Smoother

- A loading screen between the menu, the deck builder and battles (a battle shows both sides' crests). Battles load in
  the background, much faster on Android.
- The battle's effects play at the speed they were made for: they ran up to twice as fast, more so at 60 FPS.
- The first hero or scorch of a battle no longer stutters.

### In battle

- The opponent's medic, leader picks and Eredin's discards play out on the board: the revived unit flies up from the
  discard pile, a card a leader takes flies to the hand, Eredin's discards fly to the pile.
- Redrawing one of two identical cards replaces it in its place (the other copy used to move instead).
- A side that has passed no longer greys out: its score says so.
- Long player names fit next to the score.

### The deck builder

- Drag a card from one grid to the other to put it in the deck or take it out; double-click still works.

### The name and the icon

- The game is called Gwent Legacy everywhere: under its Android icon, the macOS app (Gwent Legacy.app) and the window
  title.
- Android shows the game's crown as its icon instead of Unity's logo.
- On Windows, macOS and Linux this version keeps its decks and settings in a new place: decks made with an earlier version
  don't carry over.

## 2026-10-15

Skellige joins the game, a faster start on the web, the menus rebuilt, closer to The Witcher 3 in battle, and most of the
game's insides renewed.

### Skellige (Blood and Wine)

- The fifth faction, with its cards as The Witcher 3 has them (35 units for its decks, Hemdall and the bears they bring, and
  Mardroeme), their art in the same print style as the others, its two leaders and its perk: as the third round opens, two
  random units of its discard pile return to the battlefield.
- Crach an Craite shuffles both discard piles back into the decks; under King Bran, bad weather only halves your units.
- Berserkers turn into bears on a row with a Mardroeme, the special card in the row's horn slot or Ermion; the Berserker
  goes to the discard pile as it turns, where a medic can find it and bring it back to turn again. Kambi calls Hemdall as
  the Cow calls the Bovine Defense Force; Cerys calls the Shield Maidens; the Clan Dimun Pirate scorches the battlefield as
  he lands.
- Summoned cards are tokens: the Bovine Defense Force, Hemdall and the bears only exist on the board, and a decoy can no
  longer take them back to your hand.
- Skellige Storm, weather on the ranged and siege rows, for every faction.
- Two Skellige opponent decks: the an Craite Warband (Crach an Craite) and the Berserkers of Ard Skellig (King Bran).
- How to play, the deck builder and the battle's notices know Skellige.

### The battle, closer to The Witcher 3

- Your hand stays in the order the cards were drawn, as the redraw shows it: a redrawn card takes the place of the one
  it replaces, new cards join at the right.
- The leader: click it and it leaves its place for the right, like a card about to be played, and its crown glows
  orange; the crown uses it (X, then X again or Enter, still works). Its empty place, right-click or Esc puts it back.
- A played card appears at the end of its row already as it looks on the board and glides into place; a card put back
  turns at once into its hand version; drawn cards fly in as hand cards.
- The effects, closer to the game's: a hero lands under billowing smoke while a swarm of sparks runs around its edge, flames
  rising off the top; an ability's card flashes white and burns bright orange around its large white icon; a landing card
  puffs white wisps from its sides; a medic's pile smokes with green flames; scorched cards burn, fire climbing them from
  the bottom and charring them. They are real particle effects now.
- The cards' descriptions (under the big card, in the deck builder, the redraw and the leader picker) are written live in
  The Witcher 3's style: its near-black panel, the ability's icon on a cream disc, the title and text in muted gold.
- The Ice Giant is 5 strength, and Eredin's four leader cards each show their own art, name and ability, as in The
  Witcher 3.

### In battle

- Give up Yu-Gi-Oh style: in your turn, lay your hand on your deck (the cursor turns into a hand), click it or hold a
  finger on it, then confirm. The pause menu no longer has it.
- Skipping a choice (a leader's or a medic's pick) asks first, so a stray click can't waste it.
- Kill the Cow, get the punishment: a Cow killed by a scorch calls the Bovine Defense Force at once (The Witcher 3 waits
  for the next round); discarded any other way, it still comes next round. A notice says which.
- The decks' card counts are back under the decks.
- The weather looks as in The Witcher 3: its own art of the frost, the fog and the rain lies over the row and its cards, fades in
  with the card and out once cleared; the fog billows up and drifts along the row.
- On Normal and Hard the opponent now plays its Cow while the battle goes on, for the Bovine Defense Force it brings next round.

### The deck builder

- A card you add or remove flies to its place in the other grid: one copy leaves while others are left, the last copy is
  the card itself; where it lands, the count goes up as it arrives.
- Its two grids show a scroll bar in the menus' gold while their cards overflow.
- The factions' banners at the top are written live: each faction's shield and its name in the menus' lettering (the old
  pictures misspelt Scoia'tael).

### Fixes

- Editing a preset deck with a long name (such as Riders of the Wild Hunt) a second time no longer freezes the game.

### The web version

- The game starts after downloading about 26 MB instead of about 67: the cards' full art and the music now come when they
  are first shown or played (a battle fetches its two decks' art as it begins), and the browser keeps them for next time.

### Menus

- The main menu, opponent picker, settings, pause menu, How to play, confirmations and notices are rebuilt on Unity's
  UI Toolkit, with the same look and smoother highlights.
- How to play's topics show a scroll bar while they overflow (the Monsters' part was out of reach without a mouse
  wheel).

### Platforms

- The macOS build is for Apple Silicon only (Unity is dropping Intel Macs).

### Under the hood

- The battle's turns and animations run on UniTask and LitMotion; the art and music load through Addressables; the
  effects' particles are drawn by ParticleEffectForUGUI.
- The editor and development builds write a game log (`Logs/game.jsonl`): every play, AI decision, round and menu, a JSON
  line each, for people and AI agents to read. Release builds write none.

## 2026-10-14

The whole game rebuilt on Unity 6.6, closer to The Witcher 3's Gwent in its rules and its look, with opponents for every
faction and builds for Linux, Windows, macOS, Android and the web.

### Rules, as in The Witcher 3

- A new rules engine, checked against The Witcher 3's own Gwent code: weather, tight bond, morale, Commander's Horn and
  Dandelion in the game's order, scorch, muster, medics (chaining), spies, agile units and decoys.
- Every leader does what its card says in The Witcher 3, and an ability that would do nothing can't be wasted.
- The faction perks: Northern Realms draw after a won round, Nilfgaard win ties, Monsters keep a unit, Scoia'tael choose
  who goes first.
- The Witcher 3's round flow: the winner starts the next round, a side with an empty hand sits a round out, and the Cow
  calls the Bovine Defense Force.
- Decks hold only the copies The Witcher 3 has of each card.

### Opponents

- Nine opponent decks across the four factions, picked before the battle or left to chance, each with a leader that suits
  its cards.
- Three difficulties in Settings > Game: Easy plays like a beginner; Normal saves its cards for the rounds that count;
  Hard plays each move out to the end of the battle, a few dozen times, and keeps the one that wins most often (it never
  sees your hand).

### The battle

- The board, cards and notices in The Witcher 3's style: the left column, score discs and laurel, row emblems, the turn
  glow, its emblems for each notice, the gem that breaks at the end of a round.
- Plays shown as in The Witcher 3: the card on display, the glide to its place, a hero landing in fire, ability effects.
- The redraw in The Witcher 3's carousel, and the same carousel for card pickers and discard piles.
- An end screen with The Witcher 3's emblem of the outcome, and Give up in the pause menu.
- Game speed: Slow, Normal or Fast.

### Menus and the deck builder

- A new main menu, settings (audio, display, game speed, difficulty), pause menu and an in-game How to play.
- The deck builder lists each card once with its count, has a Weather tab, and offers the opponents' decks as presets
  (your changes make your own copy).

### Controls

- Mouse, keyboard (play a card, use the leader, pass by holding Space, browse discard piles) and touch (tap, hold a finger
  on a card to read it).

### Platforms

- Linux, Windows, macOS (Intel and Apple Silicon), Android (64-bit) and the web.
- Downloads about half the size they would have been (every image compresses on every platform).

### Not yet as in The Witcher 3

- The Ice Giant is 6 strength (5 in The Witcher 3); a revived agile unit goes where you put it; a leader's horn leaves no
  card in the discard pile; no Skellige faction (Blood and Wine).
