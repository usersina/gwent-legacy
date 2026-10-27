using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Net
{
    // A LAN battle, from the lobby to its end: the link to the other device (LanLink), both players' names and decks, and
    // the battle. The host runs the match (Match): the guest's moves come over the link and go to it, and each of its
    // events goes to the guest as the guest sees it (MatchEvent.For). The guest shows those (Source) and sends its moves.
    // A link lost during the battle is waited for: the guest joins again (every few seconds, at once when the app comes
    // back from the background), says how much of the battle it has, and the host sends it the rest. Meanwhile it looks
    // for its host on the network too (LanDiscovery.Find, by the battle's key): a host found at another address (a phone
    // whose network came back with another one) is joined there. In a battle both sides say they are there every
    // second: a side not heard from for a few seconds is taken as gone, whatever the link says (a device frozen a
    // while, its old link seemingly kept alive by its attempts to come back). The battle is kept on disk while it is on
    // (LanSave): a player whose game closed takes it up again from the lobby (Rejoin), the host replaying it and
    // waiting on the same port, the guest joining its host again. Once a battle is over, either side may ask for a
    // rematch: when both have, the host begins the next battle on the same link (the same decks, a new coin toss) and
    // both screens load it. In a battle and after it, the players talk (Chat: quick lines and messages), the other's
    // heard unless muted; what was said is kept for the session (Talk). Each turn has a clock, the host's: a minute
    // (the redraw half that), counted once the side's screen has shown all there is and while both are linked; out of
    // time, the side loses the round (Match.TimeOut). Fair play (FairPlay, docs/lan-fair-play.md): each battle's
    // shuffle comes from both devices (the host promises its part, the guest gives its own, the host shows its part
    // with Begin), and once the battle is over the guest plays its record again (Record): the seed, both decks, every
    // move and event. Lives across scenes: the lobby is the menu's, the battle the game's.
    public class LanSession : MonoBehaviour
    {
        // Waiting: hosting, nobody there yet; Joining: on the way in (the host's answer not yet in); Ready: both there,
        // the host may start; Battle: under way; Closed: over (Problem says why, when something went wrong)
        public enum Stage { Waiting, Joining, Ready, Battle, Closed }

        private enum Message : byte { Join = 1, Resume, Welcome, Refused, Begin, Event, Command, CommandRefused, Leave, Resumed, Here, Rematch, Emote, Text, Shown, Clock, SeedPromise, SeedPart, Record, CommandTaken }

        private const float RejoinEvery = 2f;
        private const float JoinGiveUp = 8f;    // a join neither through nor failed this long: given up (the transport can hang on one)
        private const float HereEvery = 1f;     // in a battle, each side says it is there
        private const float SilentFor = 6f;     // not heard from that long: gone
        private const float PausedTooLong = 8f; // in the background longer: the other side has dropped the link
        private const int TalkKept = 50;
        private const float HeardEvery = 0.5f;  // the other's lines at most (a changed game could flood)
        public const float TurnTime = 60f;      // a turn's clock; the redraw's is half of it, a choice the turn brought has a quarter at least
        private const float ClockEvery = 0.5f;  // the host tells the guest what the clocks say
        private const float ShownGrace = 90f;   // a screen not caught up this long after its wait began: its clock runs anyway

        public static LanSession Current { get; private set; }

        // The session whose battle the Game scene is for: kept once it closes too (the other player left as the battle
        // loaded: the battle screen says why, instead of starting a battle against the AI); forgotten once the player leaves
        // it, or back in the main menu
        public static LanSession ForBattle { get; private set; }

        public static void ForgetBattle()
        {
            if (ForBattle is object && ForBattle.State != Stage.Battle) // closed: its object destroyed ("== null" would hide it)
                ForBattle = null;
        }

        public bool Hosting { get; private set; }
        public Stage State { get; private set; }
        public string Problem { get; private set; }     // why it closed, or why a guest was turned away
        public string MyName { get; private set; }
        public string TheirName { get; private set; }
        public Deck MyDeck { get; private set; }
        public Deck TheirDeck { get; private set; }      // the guest's whole deck (host); its faction and leader (guest)
        public Match Match { get; private set; }         // the host's
        public ushort Port { get { return port; } }

        // Whether the battle hosted shows in the others' lists (else, the search's port taken, it is joined by address)
        public bool Listed { get { return discovery != null; } }

        // Whether the other device is linked now (in a battle: false while it is away)
        public bool Linked { get { return link != null && link.State == LanLink.Status.Connected && (State != Stage.Battle || resumed); } }

        // How long the other device has been away (0 while it is there)
        public float AwayFor { get { return lostAt >= 0 ? Time.realtimeSinceStartup - lostAt : 0; } }

        // A battle taken up again from the disk (Rejoin): its screen catches up at once instead of playing every step
        public bool Rejoined { get; private set; }

        // Whether the screen has every event there is to show (the guest's, once its host said it sent the rest)
        public bool CaughtUp { get { return Hosting || resumed; } }

        // The battle under way (its number): a rematch begins another
        public int Battle { get { return battle; } }

        // The battle is over: a rematch may be asked
        public bool Over { get { return State == Stage.Battle && Ended; } }

        // The battle's end is in (its screen may not show it yet), whether the session goes on or closed since
        public bool Ended
        {
            get
            {
                return Hosting ? Match != null && Match.IsOver
                    : received.Count > 0 && received[received.Count - 1].Kind == MatchEventKind.Ended;
            }
        }

        // A rematch asked, by this side (AskRematch) and by the other
        public bool AskedRematch { get; private set; }
        public bool TheyAskedRematch { get; private set; }

        // A line said in the session: a quick one or a message, as shown, and by whom
        public sealed class Said
        {
            public bool Mine;
            public string Text;
        }

        // What the players said in the session (rematches too), the last 50
        public IReadOnlyList<Said> Talk { get { return talk; } }

        // The other player said something (muted: not heard, nor kept)
        public event Action<Said> Heard;

        // Whether this player hears the other (for the session, rematches too)
        public bool Muted { get; set; }

        public event Action Changed; // the lobby's state: someone joined or left, the battle begins, the session closed

        // The guest who just left the host's room (null once another joins)
        public string LeftRoom { get; private set; }

        // The guest's fair play check of the battle just over (FairPlay.Check): done, and what was wrong (null: nothing)
        public bool FairChecked { get; private set; }
        public string FairProblem { get; private set; }

        // The guest: the battle just over as its fair play check played it, once it passed (what its replay is saved
        // from: the host saves its own match); null before, or when it failed
        public Gwent.Rules.Match Checked { get; private set; }

        private LanLink link;
        private LanDiscovery discovery;
        private LanDiscovery finding;    // guest: its host looked for on the network while away (its address may have changed)
        private string address;          // the host's (guest)
        private ushort port;
        private int battle;              // the battle's number, for a guest coming back to it
        private int previous;            // the battle before a rematch's (host: a guest that missed the rematch's start)
        private bool resumed;            // in a battle: the guest is back and caught up (or never left)
        private float lostAt = -1;
        private float nextJoin;
        private int rejoins;             // guest: tries to join again since the link was lost
        private float joinStarted;       // guest: when the link now joining began
        private float pausedAt = -1;
        private float lastHeard;         // the other device's last message
        private float nextHere;
        private int sent;                // host: the events the guest has
        private int kept = -1;           // host: the moves on disk
        private List<MatchEvent> received = new List<MatchEvent>(); // guest: the battle's (a rematch's starts a list of its own)
        private readonly List<Said> talk = new List<Said>();
        private float heardAt = float.MinValue;
        private GuestSource source;
        private float turnTime = TurnTime;           // (a test's: the launch option lanTurnTime)
        private readonly float[] clockLeft = { -1, -1 };   // host: what each side has left (the host's seat first; -1: not waited for)
        private readonly bool[] clockRunning = new bool[2];
        private readonly int[] clockFrom = { -1, -1 };     // host: the event its wait began with
        private readonly int[] clockChoice = { -1, -1 };   // host: the choice its turn brought, given its quarter
        private readonly float[] clockSince = new float[2]; // host: when its wait began
        private int hostShown, guestShown;                 // host: the events each screen has shown
        private int shownSent = -1;                        // guest: the last count told
        private float clockSentAt, clockHeardAt;
        private bool clockLinked = true;                   // host: both devices linked, as the clock last saw
        private byte[] seedPart;                           // this device's part of the next battle's seed
        private byte[] seedPromise;                        // guest: the host's promise of its part
        private bool seedAsked;                            // host: its promise sent, the guest's part awaited
        private int? fairSeed;                             // guest: the seed both made for the battle under way
        private bool recordSent;                           // host: the battle's record went to the guest
        private readonly List<FairPlay.Sent> sentMoves = new List<FairPlay.Sent>(); // guest: its moves of the battle
        private int unanswered;                            // guest: the last of them the host has not answered yet
        private bool movesKnown = true;                    // guest: all of them (not a battle taken up from the disk)

        //----------------------------------------Opening----------------------------------------//
        // Hosts a battle with that deck, under that name (closed at once, Problem saying why, when no port can be had)
        public static LanSession Host(string name, Deck deck)
        {
            LanSession session = Make(name, deck, true);
            session.link = LanLink.Host();
            if (session.link == null)
            {
                session.Close("No free port for a game here: other programs hold them");
                return session;
            }
            session.port = session.link.Port;
            session.Listen();
            session.discovery = LanDiscovery.Answer(name, deck.Leader, session.port);
            session.State = Stage.Waiting;
            return session;
        }

        // Joins the battle hosted at that address with that deck, under that name
        public static LanSession Join(string address, ushort port, string name, Deck deck)
        {
            LanSession session = Make(name, deck, false);
            session.address = address;
            session.port = port;
            session.State = Stage.Joining;
            session.Connect();
            return session;
        }

        // Takes up a battle kept on disk: the host replays it and waits for its guest on the same port, the guest joins its
        // host again (both then load the battle). Closed at once, Problem saying why, when it cannot be.
        public static LanSession Rejoin(LanSave save)
        {
            LanSession session = Make(save.MyName, save.MyDeck, save.Hosting);
            session.TheirName = save.TheirName;
            session.TheirDeck = save.TheirDeck;
            session.battle = save.Battle;
            session.Rejoined = true;
            session.lostAt = Time.realtimeSinceStartup;
            if (save.Hosting)
            {
                try
                {
                    session.Match = Match.Replay(save.Setup, CardLibrary.Get(), save.Commands);
                }
                catch (InvalidOperationException e)
                {
                    save.Delete();
                    session.Close("The battle kept could not be taken up again (" + e.Message + ")");
                    return session;
                }
                session.kept = session.Match.Commands.Count;
                session.link = LanLink.Host(save.Port);
                if (session.link == null)
                {
                    session.Close("No free port for a game here: other programs hold them");
                    return session;
                }
                session.port = session.link.Port;
                if (session.port != save.Port)
                {
                    // Its guest comes back to the port it knew: another program holds it now
                    session.Close("Port " + save.Port + " is taken by another program now. Close it, then rejoin.");
                    return session;
                }
                session.Listen();
                session.discovery = LanDiscovery.Answer(save.MyName, save.MyDeck.Leader, session.port); // its guest looking for it
                session.discovery?.InBattle(LanDiscovery.Key(save.Battle, session.Match.Setup.Seed));
                session.State = Stage.Battle; // waiting for the guest (resumed once it is back)
                ForBattle = session;
                Say("taken up again (" + session.Match.Events.Count + " events) on port " + session.port + ": waiting for " + save.TheirName);
            }
            else
            {
                session.address = save.Address;
                session.port = save.Port;
                session.State = Stage.Battle;
                session.fairSeed = save.Seed;
                session.movesKnown = false; // those before the game closed are not known
                ForBattle = session;
                session.Connect();
                Say("taken up again: joining " + save.TheirName + " at " + save.Address + ":" + save.Port);
            }
            return session;
        }

        private static LanSession Make(string name, Deck deck, bool hosting)
        {
            if (Current != null)
                Current.Leave();
            GameObject go = new GameObject("LanSession");
            DontDestroyOnLoad(go);
            LanSession session = go.AddComponent<LanSession>();
            session.Hosting = hosting;
            session.MyName = PlayerNames.Clean(name) ?? "Player";
            session.MyDeck = deck;
            if (float.TryParse(LanProbe.Option("lanTurnTime"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float turn_time) && turn_time > 0)
                session.turnTime = turn_time;
            Current = session;
            GameLog.Event(LogCategory.Game, hosting ? "lan_host" : "lan_join", "name", name, "deck", deck.Name);
            return session;
        }

        private void Connect()
        {
            if (link != null)
                link.Dispose();
            link = LanLink.Join(address, port);
            if (link == null)
            {
                Close("\"" + address + "\" is not an address.");
                return;
            }
            Listen();
            joinStarted = Time.realtimeSinceStartup;
            nextJoin = joinStarted + RejoinEvery;
        }

        private void Listen()
        {
            link.Connected += OnConnected;
            link.Received += OnReceived;
            link.Disconnected += OnDisconnected;
        }

        //----------------------------------------The battle----------------------------------------//
        // The host starts the battle: the match from both decks (its own the Player side's), its first events to the guest;
        // both load the battle (BattleController takes it from here)
        public void StartBattle()
        {
            if (!Hosting || State != Stage.Ready)
                return;
            if (seedAsked)
                return; // on its way
            AskSeed(); // the battle begins with the guest's part (SeedPart)
            GameLog.Event(LogCategory.Game, "lan_battle", "host", MyName, "guest", TheirName);
            Changed?.Invoke();
        }

        // The host's part of the seed promised to the guest (its hash), the guest's awaited
        private void AskSeed()
        {
            seedPart = FairPlay.NewPart();
            seedAsked = true;
            SendPromise();
        }

        private void SendPromise()
        {
            byte[] promise = FairPlay.Promise(seedPart);
            Send(Message.SeedPromise, w => w.Write(promise));
        }

        // Asks for a rematch, the battle over; the other side asked too, the next battle begins (the host's: its number
        // changes, Battle, and both load it)
        public void AskRematch()
        {
            if (!Over || AskedRematch)
                return;
            AskedRematch = true;
            Send(Message.Rematch, w => w.Write(battle));
            Say("rematch asked" + (TheyAskedRematch ? ", as the other side did" : ""));
            if (Hosting && TheyAskedRematch)
                Rematch();
            Changed?.Invoke();
        }

        private void Rematch()
        {
            if (seedAsked)
                return;
            AskSeed();
            GameLog.Event(LogCategory.Game, "lan_rematch", "host", MyName, "guest", TheirName);
        }

        // A battle from both decks (the host's own the Player side's) and the seed both devices made, its number, the host's
        // part of the seed and first events to the guest
        private void BeginBattle(int seed)
        {
            ulong before = Match != null ? LanDiscovery.Key(battle, Match.Setup.Seed) : 0; // the battle just over (a rematch)
            previous = battle;
            battle = UnityEngine.Random.Range(1, int.MaxValue);
            seedAsked = false;
            recordSent = false;
            Match = new Match(new MatchSetup { Player = MyDeck, Enemy = TheirDeck, Seed = seed }, CardLibrary.Get());
            Match.Start();
            discovery?.InBattle(LanDiscovery.Key(battle, seed), before); // no longer a game to join: its guest's search only
            State = Stage.Battle;
            ForBattle = this;
            resumed = true;
            Rejoined = false;
            AskedRematch = TheyAskedRematch = false;
            sent = 0;
            lastHeard = Time.realtimeSinceStartup; // nothing is said in the room: the guest is not silent for that
            nextHere = 0;
            hostShown = guestShown = 0;
            clockLinked = true;
            for (int i = 0; i < 2; i++)
            {
                clockFrom[i] = clockChoice[i] = -1;
                clockLeft[i] = -1;
            }
            byte[] part = seedPart;
            Send(Message.Begin, w => { w.Write(battle); w.Write(part); });
            Keep();
        }

        // The battle on disk, as it is now (the host's after each move; the guest's once, as it begins); gone once over
        private void Keep()
        {
            if (Ended)
            {
                LanSave.Delete(Hosting);
                return;
            }
            LanSave save = new LanSave
            {
                Hosting = Hosting,
                Battle = battle,
                Address = address,
                Port = port,
                MyName = MyName,
                TheirName = TheirName,
                MyDeck = MyDeck,
                TheirDeck = TheirDeck ?? new Deck(),
                Seed = Hosting ? (int?)null : fairSeed,
            };
            if (Hosting)
            {
                save.Setup = Match.Setup;
                save.Commands.AddRange(Match.Commands);
                kept = Match.Commands.Count;
            }
            save.Write();
        }

        // What the battle screen shows: the host's own match, or the events the host sent (the guest's)
        public BattleSource Source
        {
            get
            {
                if (Hosting)
                    return Match != null ? new LocalSource(Match) : null;
                return source ?? (source = new GuestSource(this));
            }
        }

        // The guest's side of the battle on the host's screen: its moves come over the link
        public IOpponent Opponent { get { return new LanOpponent(); } }

        // Leaves the session (the other device is told), from the lobby or once the battle is over (giving up first is the
        // battle's business)
        public void Leave()
        {
            if (link != null && link.State == LanLink.Status.Connected)
                Send(Message.Leave, w => { });
            if (State == Stage.Battle || Rejoined)
                LanSave.Delete(Hosting);
            if (ForBattle == this)
                ForBattle = null;
            Close(null);
        }

        private void Close(string problem)
        {
            if (problem != null)
                Problem = problem;
            Say("closed" + (problem != null ? ": " + problem : ""));
            State = Stage.Closed;
            if (link != null)
                link.Dispose();
            if (discovery != null)
                discovery.Dispose();
            finding?.Dispose();
            link = null;
            discovery = null;
            finding = null;
            if (Current == this)
                Current = null;
            Changed?.Invoke();
            if (this != null)
                Destroy(gameObject);
        }

        //----------------------------------------The link----------------------------------------//
        private void Update()
        {
            discovery?.Update();
            link?.Update();
            if (State == Stage.Closed)
                return;
            // A join that never got through (no game at that address, the host's link turning it away) closes the link
            // without a word: the lobby is told why
            if (!Hosting && State == Stage.Joining && link != null && link.State == LanLink.Status.Closed)
            {
                Close(link.Reason ?? "No game answered at that address.");
                return;
            }
            // A join that hangs (the transport neither through nor giving up: seen with a host back from a network cut):
            // given up after a while; in a battle, tried again
            bool hung = !Hosting && link != null && link.State == LanLink.Status.Joining && Time.realtimeSinceStartup - joinStarted > JoinGiveUp;
            if (hung && State == Stage.Joining)
            {
                Close("No game answered at that address.");
                return;
            }
            if (hung && State == Stage.Battle)
            {
                Say("joining again: the last try hung");
                Connect();
            }
            // The guest away from the battle: it joins again now and then (the link it had gave up)
            if (!Hosting && State == Stage.Battle && (link == null || link.State == LanLink.Status.Closed) && Time.realtimeSinceStartup >= nextJoin)
            {
                // Joining again, the battle's host away: why the last try failed, now and then (a log to look at)
                if (link != null && rejoins++ % 5 == 0)
                    Say("joining again (try " + rejoins + "): " + (link.Reason ?? "no answer"));
                Connect();
            }
            FindHost();
            if (Hosting && State == Stage.Battle && Match.Commands.Count != kept)
                Keep();
            if (Hosting && State == Stage.Battle && resumed)
            {
                SendEvents();
                if (Match.IsOver && !recordSent && Linked)
                    SendRecord();
            }
            if (Hosting && State == Stage.Battle && Match != null && !Match.IsOver)
                RunClock();
            if (State == Stage.Battle || State == Stage.Ready)
                KeepAlive();
        }

        // Says this side is there (in the room too); lets the other go once it has been silent too long (the host drops its
        // link and takes the next, the guest joins again at once, or out of a room, is told the host is gone)
        private void KeepAlive()
        {
            if (link == null || link.State != LanLink.Status.Connected)
                return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextHere)
            {
                nextHere = now + HereEvery;
                Send(Message.Here, w => { });
            }
            if (now - lastHeard <= SilentFor)
                return;
            const string reason = "The other player stopped answering.";
            if (Hosting)
                link.Drop(reason); // OnDisconnected follows
            else
                JoinAgain(reason);
        }

        // The guest away from the battle looks for its host on the network: heard only at another address or port (its
        // network came back with another address), it is joined there (and the battle on disk says so)
        private void FindHost()
        {
            if (Hosting || State != Stage.Battle || resumed || fairSeed == null)
            {
                finding?.Dispose();
                finding = null;
                return;
            }
            if (finding == null)
                finding = LanDiscovery.Find(LanDiscovery.Key(battle, fairSeed.Value));
            finding.Update();
            if (link != null && link.State == LanLink.Status.Connected)
                return; // through, catching up
            LanDiscovery.Game host = finding.Games.FirstOrDefault(game => game.Compatible);
            if (host == null || host.HeardAt(address) && host.Port == port)
                return;
            Say("the host answers at " + host.Address + ":" + host.Port + " (not " + address + ":" + port + "): joining it there");
            address = host.Address;
            port = host.Port;
            Keep();
            Connect();
        }

        // The guest lets its link go and joins again at once (the host sends what it lacks)
        private void JoinAgain(string reason)
        {
            link.Dispose();
            link = null;
            nextJoin = 0;
            OnDisconnected(reason);
        }

        // The events the guest does not have yet, as it sees them
        private void SendEvents()
        {
            while (sent < Match.Events.Count && link != null && link.State == LanLink.Status.Connected)
            {
                MatchEvent e = Match.Events[sent].For(false);
                int index = sent;
                if (!Send(Message.Event, w => { w.Write(index); MatchWire.Write(w, e); }))
                    return;
                sent++;
            }
        }

        private void OnConnected()
        {
            lastHeard = Time.realtimeSinceStartup;
            shownSent = -1; // told again
            if (Hosting)
                return; // the guest speaks first
            if (State == Stage.Battle)
                Send(Message.Resume, w => { w.Write(battle); w.Write(MyName); w.Write(received.Count); });
            else
                Send(Message.Join, w => { w.Write(MyName); MatchWire.Write(w, MyDeck); });
        }

        private void OnDisconnected(string reason)
        {
            if (State == Stage.Closed)
                return;
            if (State == Stage.Battle)
            {
                // Away: waited for (the guest joins again, the host takes it back)
                resumed = false;
                if (lostAt < 0)
                    lostAt = Time.realtimeSinceStartup;
                Unsure(); // the moves not answered yet: the host may have had them, or not (their answer is lost with the link)
                if (source != null && source.Waiting)
                    source.Refuse(); // a move on its way may be lost with the link: the card goes back, to be played again
                recordSent = false; // sent again once it is back, in case it was lost with the link
                Say("link lost (" + reason + "): waiting");
                Changed?.Invoke();
                return;
            }
            if (Hosting)
                GuestLeftRoom(); // waiting for another
            else
                Close(Problem ?? reason);
        }

        private void GuestLeftRoom()
        {
            seedAsked = false;
            LeftRoom = TheirName;
            TheirName = null;
            TheirDeck = null;
            State = Stage.Waiting;
            Changed?.Invoke();
        }

        private void OnReceived(byte[] bytes)
        {
            lastHeard = Time.realtimeSinceStartup;
            try
            {
                using (BinaryReader r = new BinaryReader(new MemoryStream(bytes)))
                    Handle((Message)r.ReadByte(), r);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[lan] A message could not be read: " + e.Message);
            }
        }

        private void Handle(Message kind, BinaryReader r)
        {
            switch (kind)
            {
                // Host
                case Message.Join when Hosting:
                    string name = PlayerNames.Clean(r.ReadString()) ?? "Player";
                    Deck deck = DeckRules.Sanitize(MatchWire.ReadDeck(r), CardLibrary.Get());
                    if (State != Stage.Waiting)
                    {
                        Send(Message.Refused, w => w.Write(State == Stage.Battle ? "A battle is already under way there." : "Someone else joined first."));
                        return;
                    }
                    List<string> problems = DeckRules.Validate(deck, CardLibrary.Get());
                    if (problems.Count > 0)
                    {
                        Send(Message.Refused, w => w.Write("Your deck can't be used: " + problems[0]));
                        return;
                    }
                    TheirName = name;
                    TheirDeck = deck;
                    LeftRoom = null;
                    State = Stage.Ready;
                    Send(Message.Welcome, w => { w.Write(MyName); w.Write((byte)MyDeck.Faction); w.Write((short)MyDeck.Leader); });
                    GameLog.Event(LogCategory.Game, "lan_guest", "name", name, "deck", deck.Name);
                    Changed?.Invoke();
                    break;
                case Message.Resume when Hosting:
                    int number = r.ReadInt32();
                    r.ReadString();
                    int has = r.ReadInt32();
                    if (State == Stage.Battle && number == previous && previous != 0 && number != battle)
                    {
                        // Gone as the rematch began: its start, then all of it
                        byte[] part = seedPart;
                        Send(Message.Begin, w => { w.Write(battle); w.Write(part); });
                        number = battle;
                        has = 0;
                    }
                    if (State != Stage.Battle || number != battle || has < 0 || has > Match.Events.Count)
                    {
                        Send(Message.Refused, w => w.Write("The host no longer has that battle."));
                        return;
                    }
                    sent = has;
                    resumed = true;
                    lostAt = -1;
                    SendEvents(); // all it missed, then word that it has it all
                    Send(Message.Resumed, w => { });
                    if (AskedRematch)
                        Send(Message.Rematch, w => w.Write(battle)); // asked while it was away
                    if (seedAsked)
                        SendPromise(); // the rematch's seed, its part not in yet (the same promise)
                    Say("the guest is back, " + (Match.Events.Count - has) + " events to send it");
                    Changed?.Invoke();
                    break;
                case Message.Command when Hosting && State == Stage.Battle:
                    // Every move answered, in order: taken (after the events it made), or refused
                    MatchCommand command = MatchCommand.Read(r);
                    if (command.Kind == CommandKind.TimeOut || !Match.Apply(false, command)) // the clock is the host's
                        Send(Message.CommandRefused, w => { });
                    else
                    {
                        SendEvents();
                        Send(Message.CommandTaken, w => { });
                    }
                    break;
                case Message.Shown when Hosting:
                    guestShown = Mathf.Max(0, r.ReadInt32());
                    break;
                case Message.SeedPart when Hosting && seedAsked:
                    byte[] their_part = r.ReadBytes(FairPlay.PartSize);
                    if (their_part.Length != FairPlay.PartSize)
                        break; // not one
                    BeginBattle(FairPlay.Seed(seedPart, their_part));
                    Changed?.Invoke();
                    break;

                // Guest
                case Message.Welcome when !Hosting:
                    TheirName = PlayerNames.Clean(r.ReadString()) ?? "Player";
                    TheirDeck = new Deck { Faction = (Faction)r.ReadByte(), Leader = r.ReadInt16() };
                    State = Stage.Ready;
                    Changed?.Invoke();
                    break;
                case Message.Refused when !Hosting:
                    Problem = r.ReadString();
                    if (State == Stage.Battle)
                        LanSave.Delete(Hosting); // the host has it no more
                    Close(Problem);
                    break;
                case Message.SeedPromise when !Hosting && (State == Stage.Ready || Over):
                    // The host's promise of its part of the next battle's seed: this device's own part in return. The same
                    // promise again (lost with the link) has the same part; another one before the battle began is the host
                    // trying seeds until one suits it
                    byte[] promise = r.ReadBytes(32);
                    if (seedPromise != null && !promise.SequenceEqual(seedPromise))
                    {
                        Close("The host asked for a new shuffle before the battle began: its game may have been changed.");
                        break;
                    }
                    if (seedPromise == null)
                        seedPart = FairPlay.NewPart();
                    seedPromise = promise;
                    byte[] my_part = seedPart;
                    Send(Message.SeedPart, w => w.Write(my_part));
                    break;
                case Message.Begin when !Hosting && (State == Stage.Ready || Over):
                    // The first battle, or a rematch: the last one's events go (its screen is done with them). Its seed is
                    // both parts, the host's as promised
                    battle = r.ReadInt32();
                    byte[] host_part = r.ReadBytes(FairPlay.PartSize);
                    if (!FairPlay.Kept(seedPromise, host_part))
                    {
                        Close("The host's part of the shuffle is not the one it promised: its game may have been changed.");
                        break;
                    }
                    fairSeed = FairPlay.Seed(host_part, seedPart);
                    seedPromise = null;
                    sentMoves.Clear();
                    unanswered = 0;
                    movesKnown = true;
                    FairChecked = false;
                    FairProblem = null;
                    Checked = null;
                    State = Stage.Battle;
                    ForBattle = this;
                    resumed = true;
                    received = new List<MatchEvent>(); // the last battle's screen keeps its own, to finish showing its end
                    source = null;
                    Rejoined = false;
                    AskedRematch = TheyAskedRematch = false;
                    Keep();
                    Changed?.Invoke();
                    break;
                case Message.Resumed when !Hosting:
                    rejoins = 0;
                    resumed = true;
                    lostAt = -1;
                    source?.Answered();
                    if (AskedRematch)
                        Send(Message.Rematch, w => w.Write(battle)); // asked while away
                    Say("back in the battle, after " + received.Count + " events");
                    Changed?.Invoke();
                    break;
                case Message.Event when !Hosting:
                    int index = r.ReadInt32();
                    MatchEvent e = MatchWire.ReadEvent(r);
                    if (index > received.Count)
                    {
                        JoinAgain("an event missed"); // the host sends the rest again
                        return;
                    }
                    if (index == received.Count)
                        received.Add(e);
                    if (e.Kind == MatchEventKind.Ended)
                        LanSave.Delete(Hosting);
                    break; // (not the answer to a move of this side's: one from an earlier move may still be on its way)
                case Message.CommandTaken when !Hosting:
                    if (unanswered > 0)
                        unanswered--;
                    if (unanswered == 0)
                        source?.Answered();
                    break;
                case Message.CommandRefused when !Hosting:
                    if (unanswered > 0)
                    {
                        sentMoves.RemoveAt(sentMoves.Count - unanswered); // the earliest not answered: the host never took it
                        unanswered--;
                    }
                    source?.Refuse();
                    break;
                case Message.Record when !Hosting && State == Stage.Battle:
                    if (r.ReadInt32() != battle || FairChecked || !Ended)
                        break; // another battle's, checked already, or before its end (never from a host as it should be)
                    MatchSetup setup = MatchWire.ReadSetup(r);
                    int count = r.ReadInt32();
                    if (count < 0 || count > 10000)
                        break;
                    List<(bool seat, MatchCommand command)> commands = new List<(bool, MatchCommand)>(count);
                    for (int i = 0; i < count; i++)
                        commands.Add((r.ReadBoolean(), MatchCommand.Read(r)));
                    CheckRecord(setup, commands);
                    break;
                case Message.Clock when !Hosting:
                    for (int i = 0; i < 2; i++)
                    {
                        clockLeft[i] = r.ReadInt16() / 10f;
                        clockRunning[i] = r.ReadBoolean();
                    }
                    clockHeardAt = Time.realtimeSinceStartup;
                    break;

                case Message.Rematch when State == Stage.Battle:
                    if (r.ReadInt32() != battle || !Over)
                        break; // asked for another battle than this one (sent before it began)
                    TheyAskedRematch = true;
                    Say("the other side asks for a rematch");
                    if (Hosting && AskedRematch && Over)
                        Rematch();
                    Changed?.Invoke();
                    break;

                case Message.Emote when State == Stage.Battle:
                    int line = r.ReadByte();
                    if (line < Chat.Lines.Length)
                        Hear(Chat.Lines[line]);
                    break;
                case Message.Text when State == Stage.Battle:
                    string text = Chat.Clean(r.ReadString());
                    if (text != null)
                        Hear(text);
                    break;

                case Message.Leave:
                    // The other side left on purpose: in the lobby, its seat is free again (host) or the game is gone
                    // (guest); in a battle it gave up first, and its events said so
                    if (Hosting && State != Stage.Battle)
                        GuestLeftRoom();
                    else if (State == Stage.Battle && !Over)
                    {
                        // Left a battle not over (given up on its own screen while away): it gave the battle up
                        if (Hosting)
                            Match.Apply(false, MatchCommand.GiveUp());
                        Close(Hosting ? null : TheirName + " left the battle.");
                    }
                    else
                        Close(State == Stage.Battle ? null : "The host closed the battle.");
                    break;
            }
        }

        // The session's turns, in the player's log too (a LAN battle's trouble is looked for there)
        private static void Say(string line)
        {
            Debug.Log("[lan] session: " + line + " @" + Time.realtimeSinceStartup.ToString("0.0") + " s");
        }

        private bool Send(Message kind, Action<BinaryWriter> write)
        {
            return link != null && link.Send(MatchWire.Bytes(w => { w.Write((byte)kind); write(w); }));
        }

        // A quick line of this player's (Chat.Lines' number) to the other, in a battle or after it: what was said, or null
        // (the other device away)
        public Said SendEmote(int line)
        {
            if (line < 0 || line >= Chat.Lines.Length || !Talking || !Send(Message.Emote, w => w.Write((byte)line)))
                return null;
            return Remember(true, Chat.Lines[line]);
        }

        // A message of this player's (kept to Chat's rule) to the other: what was said, or null (nothing of it left, the
        // other device away)
        public Said SendChat(string message)
        {
            string text = Chat.Clean(message);
            if (text == null || !Talking || !Send(Message.Text, w => w.Write(text)))
                return null;
            return Remember(true, text);
        }

        private bool Talking { get { return State == Stage.Battle && Linked; } }

        private Said Remember(bool mine, string text)
        {
            Said said = new Said { Mine = mine, Text = text };
            talk.Add(said);
            if (talk.Count > TalkKept)
                talk.RemoveAt(0);
            return said;
        }

        // The other player's line: heard, unless muted or too soon after the last
        private void Hear(string text)
        {
            float now = Time.realtimeSinceStartup;
            if (Muted || now - heardAt < HeardEvery)
                return;
            heardAt = now;
            Heard?.Invoke(Remember(false, text));
        }

        // A guest's move for the host's match (kept, for the fair play check)
        private bool SendCommand(MatchCommand command)
        {
            if (!(State == Stage.Battle && Linked && Send(Message.Command, command.Write)))
                return false;
            sentMoves.Add(new FairPlay.Sent { Command = command });
            unanswered++;
            return true;
        }

        // The host: the battle's record to the guest, once it is over (both decks, the seed, every move)
        private void SendRecord()
        {
            recordSent = true;
            MatchSetup setup = Match.Setup;
            List<(bool seat, MatchCommand command)> commands = Match.Commands;
            int number = battle;
            Send(Message.Record, w =>
            {
                w.Write(number);
                MatchWire.Write(w, setup);
                w.Write(commands.Count);
                foreach ((bool seat, MatchCommand command) in commands)
                {
                    w.Write(seat);
                    command.Write(w);
                }
            });
        }

        // The guest: the record played again on its own (FairPlay.Check); a move still unanswered may not have reached the
        // host
        private void CheckRecord(MatchSetup setup, List<(bool seat, MatchCommand command)> commands)
        {
            Unsure(); // a move with no answer yet (one after the end: it will be refused)
            FairProblem = FairPlay.Check(setup, commands, received, MyDeck, TheirDeck, fairSeed, movesKnown ? sentMoves : null, CardLibrary.Get());
            FairChecked = true;
            Say("fair play " + (FairProblem == null ? "checked" + (fairSeed.HasValue && movesKnown ? "" : " (in part: taken up again)") : "FAILED: " + FairProblem));
            if (FairProblem != null) // what did not add up, for whoever looks into it
                Say("the guest's moves in the record: " + string.Join(", ", commands.Where(c => !c.seat).Select(c => c.command.ToString()))
                    + " | as it made them: " + string.Join(", ", sentMoves.Select(m => m.Command + (m.Unsure ? " (unsure)" : ""))));
            GameLog.Event(LogCategory.Game, "lan_fair_play", "ok", FairProblem == null, "problem", FairProblem ?? "");
            if (FairProblem == null)
            {
                try
                {
                    Checked = Gwent.Rules.Match.Replay(setup, CardLibrary.Get(), commands);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[lan] The battle could not be played again for its replay: " + e.Message);
                }
            }
            Changed?.Invoke();
        }

        // The guest's moves the host has not answered: perhaps taken, perhaps not
        private void Unsure()
        {
            for (int i = Mathf.Max(0, sentMoves.Count - unanswered); i < sentMoves.Count; i++)
            {
                FairPlay.Sent move = sentMoves[i];
                move.Unsure = true;
                sentMoves[i] = move;
            }
            unanswered = 0;
        }

        //----------------------------------------The clock----------------------------------------//
        // The time a side has left for its turn (mine: this device's player's), and whether it runs now (it waits while a
        // screen catches up, or the other device is away); false when the side is not waited for
        public bool Clock(bool mine, out float left, out bool running)
        {
            int i = Hosting == mine ? 0 : 1;
            left = clockLeft[i];
            running = clockRunning[i] && State == Stage.Battle && Linked;
            if (!Hosting && running)
                left -= Time.realtimeSinceStartup - clockHeardAt;
            left = Mathf.Max(0, left);
            return clockLeft[i] >= 0 && State == Stage.Battle;
        }

        // This device's battle screen has shown that many events of the battle and waits for nothing more (its animations
        // are not the player's time): the host's own count, told to the host by the guest. Only the battle under way's
        // screen tells it (the last battle's, still showing as a rematch begins, would tell the new one's clock it is there)
        public void ScreenShown(int count, int of_battle)
        {
            if (of_battle != battle)
                return;
            if (Hosting)
                hostShown = count;
            else if (count != shownSent && State == Stage.Battle && Linked && Send(Message.Shown, w => w.Write(count)))
                shownSent = count;
        }

        // The host's clock: each side the match waits for has turnTime for its turn (half that for its redraw, a quarter
        // at least for a choice the turn brought), counted while its screen has shown all there is and both devices are
        // linked (it holds while one is away, and gives a quarter at least once it is back); out of time, its TimeOut. The
        // guest hears twice a second what each side has left.
        private void RunClock()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f); // a frozen frame (the app in the background) is not time
            bool linked = Linked;
            if (linked && !clockLinked)
                for (int i = 0; i < 2; i++)
                    if (clockLeft[i] >= 0)
                        clockLeft[i] = Mathf.Max(clockLeft[i], turnTime / 4);
            clockLinked = linked;
            for (int i = 0; i < 2; i++)
            {
                bool seat = i == 0;
                if (!Match.Awaits(seat))
                {
                    clockFrom[i] = -1;
                    clockLeft[i] = -1;
                    clockRunning[i] = false;
                    continue;
                }
                int from = WaitBegan(seat, out int choice);
                if (from != clockFrom[i])
                {
                    clockFrom[i] = from;
                    clockSince[i] = Time.realtimeSinceStartup;
                    clockChoice[i] = -1;
                    clockLeft[i] = Match.Phase == MatchPhase.Redraw ? turnTime / 2 : turnTime;
                }
                if (choice > from && choice != clockChoice[i])
                {
                    clockChoice[i] = choice;
                    clockLeft[i] = Mathf.Max(clockLeft[i], turnTime / 4);
                }
                // Once its screen has shown all there is; or, a screen held still (a tampered game never saying so), a while
                // after its wait began
                clockRunning[i] = Linked && ((seat ? hostShown : guestShown) >= Match.Events.Count || Time.realtimeSinceStartup - clockSince[i] > ShownGrace);
                if (clockRunning[i])
                    clockLeft[i] -= dt;
                if (clockLeft[i] > 0)
                    continue;
                Say((seat ? "the host" : "the guest") + " is out of time in " + Match.Phase);
                GameLog.Event(LogCategory.Game, "lan_timeout", "side", seat ? "host" : "guest", "phase", Match.Phase.ToString());
                clockFrom[i] = -1;
                Match.Apply(seat, MatchCommand.TimeOut());
                return; // the next frame times what the match waits for now
            }
            if (Time.realtimeSinceStartup - clockSentAt >= ClockEvery)
            {
                clockSentAt = Time.realtimeSinceStartup;
                Send(Message.Clock, w =>
                {
                    for (int i = 0; i < 2; i++)
                    {
                        w.Write((short)Mathf.CeilToInt(Mathf.Max(-1, clockLeft[i]) * 10));
                        w.Write(clockRunning[i]);
                    }
                });
            }
        }

        // The event a side's wait began with: its turn's start, the redraw's, or the battle's (the Scoia'tael choice of who
        // goes first); choice: the latest choice asked of it (-1)
        private int WaitBegan(bool seat, out int choice)
        {
            choice = -1;
            for (int k = Match.Events.Count - 1; k >= 0; k--)
            {
                MatchEvent e = Match.Events[k];
                if (e.Kind == MatchEventKind.ChoiceAsked && e.Seat == seat && choice < 0)
                    choice = k;
                if (e.Kind == MatchEventKind.TurnStarted && e.Seat == seat || e.Kind == MatchEventKind.FirstPlayer || e.Kind == MatchEventKind.Started)
                    return k;
            }
            return 0;
        }

        //----------------------------------------The app in the background----------------------------------------//
        // A phone back after a long while: the other side has dropped the link by then (or is about to), so the guest joins
        // again at once
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                pausedAt = Time.realtimeSinceStartup;
                return;
            }
            bool long_away = pausedAt >= 0 && Time.realtimeSinceStartup - pausedAt > PausedTooLong;
            pausedAt = -1;
            if (!long_away || State != Stage.Battle || Hosting)
                return; // the host's link finds out on its own, and the guest comes back to it
            if (lostAt < 0)
                lostAt = Time.realtimeSinceStartup;
            resumed = false;
            Say("back from the background after a long while: joining again");
            if (link != null)
                link.Dispose(); // the host hears it went, and takes the next link
            link = null;
            nextJoin = 0; // at once
            Changed?.Invoke();
        }

        // The game closed after a battle: the other player is told it left (not that it is away: no rematch is coming).
        // During one, it is away: the battle is kept, to be taken up again.
        private void OnApplicationQuit()
        {
            if (Over)
                Leave();
        }

        // The phone kept awake while the room or the battle is open: asleep, it would be away
        private void Awake()
        {
            ScreenAwake.Hold(this);
        }

        private void OnDestroy()
        {
            ScreenAwake.Release(this);
            if (link != null)
                link.Dispose();
            if (discovery != null)
                discovery.Dispose();
            finding?.Dispose();
            if (Current == this)
                Current = null;
        }

        //----------------------------------------The guest's battle----------------------------------------//
        // The events the host sent, and the guest's moves on their way to it
        private sealed class GuestSource : BattleSource
        {
            private readonly LanSession session;
            private bool waiting;

            private readonly List<MatchEvent> events; // its battle's (the next one's are another list)

            public GuestSource(LanSession session)
            {
                this.session = session;
                events = session.received;
            }

            public override int Count { get { return events.Count; } }
            public override MatchEvent Seen(int index) { return events[index]; }
            public override MatchEvent All(int index) { return events[index]; }
            public override bool Waiting { get { return waiting; } }

            public override bool Submit(MatchCommand command)
            {
                if (waiting && command.Kind != CommandKind.GiveUp || !session.SendCommand(command))
                    return false;
                waiting = true;
                return true;
            }

            public void Answered()
            {
                waiting = false;
            }

            public void Refuse()
            {
                waiting = false;
                OnRefused();
            }
        }
    }

    // The guest's side of a LAN battle on the host's screen: its moves come over the link (LanSession), nothing to do here
    public sealed class LanOpponent : IOpponent
    {
        public Cysharp.Threading.Tasks.UniTask Act(Match match, bool seat, System.Threading.CancellationToken ct)
        {
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }
    }
}
