using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Gwent.Rules
{
    // Who recorded a battle: the player against the AI, or a LAN battle's host or guest
    public enum RecordKind : byte { Ai, LanHost, LanGuest }

    // A battle kept to be watched again (a replay): its setup and every move of both sides (played again, they give the
    // same battle, event for event: Match.Replay), whose side it was seen from (Seat: the match's Player side, or its
    // Enemy side for a LAN guest), the names on the board, how it ended, and when and with which build it was played.
    // A fingerprint of its events comes with it: a later build whose rules or cards play the moves differently gives
    // another one, and the replay says so (Play) instead of showing a battle that never was.
    public sealed class MatchRecord
    {
        public const byte Format = 1;

        public DateTime Saved;          // UTC
        public int Build;               // the build number it was played with (BuildInfo.Number)
        public string BuildId = "";     // and its whole id ("1.0.0+1a2b3c4d")
        public RecordKind Kind;
        public bool Seat;               // the side it was seen from: the match's Player side (true) or Enemy side
        public string MyName = "", TheirName = "";
        public RoundWinner Winner;      // as the match ended it (its Player and Enemy sides)
        public bool GaveUp;             // a side gave up (or left)
        public bool GaveUpSeat;         // which one
        public int MyRounds, TheirRounds;
        public int Events;              // how many events the battle has
        public string Fingerprint = "";
        public MatchSetup Setup;
        public List<(bool seat, MatchCommand command)> Commands = new List<(bool, MatchCommand)>();

        // The side seen from, and the other, as their decks went into the battle
        public Deck MyDeck { get { return Seat ? Setup.Player : Setup.Enemy; } }
        public Deck TheirDeck { get { return Seat ? Setup.Enemy : Setup.Player; } }

        // The outcome for the side seen from: Player (won), Enemy (lost) or Draw
        public RoundWinner Outcome { get { return Seat ? Winner : MatchState.Flipped(Winner); } }

        // The record of a battle over, seen from that side (null: it is not over)
        public static MatchRecord Of(Match match, bool seat, RecordKind kind, string my_name, string their_name)
        {
            if (match == null || !match.IsOver)
                return null;
            MatchEvent end = match.Events[match.Events.Count - 1];
            PlayerState mine = seat ? end.State.Player : end.State.Enemy, theirs = seat ? end.State.Enemy : end.State.Player;
            return new MatchRecord
            {
                Saved = DateTime.UtcNow,
                Kind = kind,
                Seat = seat,
                MyName = my_name ?? "",
                TheirName = their_name ?? "",
                Winner = match.Result,
                GaveUp = end.Kind == MatchEventKind.Ended && end.Flag,
                GaveUpSeat = end.Seat,
                MyRounds = mine.RoundsWon.Count(won => won),
                TheirRounds = theirs.RoundsWon.Count(won => won),
                Events = match.Events.Count,
                Fingerprint = FingerprintOf(match.Events),
                Setup = match.Setup,
                Commands = new List<(bool, MatchCommand)>(match.Commands),
            };
        }

        // The battle played again from the record: null (and why) when it can't be, or when this build plays it otherwise
        // than the one that recorded it
        public Match Play(CardDatabase db, out string problem)
        {
            problem = null;
            Match match;
            try
            {
                match = Match.Replay(Setup, db, Commands);
            }
            catch (Exception)
            {
                problem = Older();
                return null;
            }
            if (!match.IsOver || match.Events.Count != Events || FingerprintOf(match.Events) != Fingerprint)
            {
                problem = Older();
                return null;
            }
            return match;
        }

        private string Older()
        {
            return "This replay was recorded with " + (Build > 0 ? "build " + Build : "another build")
                + ", whose rules or cards play it differently: it can't be shown with this one.";
        }

        // Every event of a battle, whole, as bytes, hashed: two builds that play the moves alike give the same
        public static string FingerprintOf(IList<MatchEvent> events)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(stream))
                {
                    foreach (MatchEvent e in events)
                        MatchWire.Write(w, e);
                }
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray()), 0, 16).Replace("-", "").ToLowerInvariant();
            }
        }

        //----------------------------------------On disk----------------------------------------//
        public void Write(BinaryWriter w)
        {
            w.Write(Format);
            w.Write(Saved.ToBinary());
            w.Write(Build);
            w.Write(BuildId ?? "");
            w.Write((byte)Kind);
            w.Write(Seat);
            w.Write(MyName ?? "");
            w.Write(TheirName ?? "");
            w.Write((byte)Winner);
            w.Write(GaveUp);
            w.Write(GaveUpSeat);
            w.Write((byte)MyRounds);
            w.Write((byte)TheirRounds);
            w.Write(Events);
            w.Write(Fingerprint ?? "");
            MatchWire.Write(w, Setup);
            w.Write(Commands.Count);
            foreach ((bool seat, MatchCommand command) in Commands)
            {
                w.Write(seat);
                command.Write(w);
            }
        }

        // A record read back; an InvalidDataException for another format (a later build's)
        public static MatchRecord Read(BinaryReader r)
        {
            byte format = r.ReadByte();
            if (format != Format)
                throw new InvalidDataException("A replay of format " + format + ", not " + Format);
            MatchRecord record = new MatchRecord
            {
                Saved = DateTime.FromBinary(r.ReadInt64()),
                Build = r.ReadInt32(),
                BuildId = r.ReadString(),
                Kind = (RecordKind)r.ReadByte(),
                Seat = r.ReadBoolean(),
                MyName = r.ReadString(),
                TheirName = r.ReadString(),
                Winner = (RoundWinner)r.ReadByte(),
                GaveUp = r.ReadBoolean(),
                GaveUpSeat = r.ReadBoolean(),
                MyRounds = r.ReadByte(),
                TheirRounds = r.ReadByte(),
                Events = r.ReadInt32(),
                Fingerprint = r.ReadString(),
                Setup = MatchWire.ReadSetup(r),
            };
            int commands = r.ReadInt32();
            for (int i = 0; i < commands; i++)
                record.Commands.Add((r.ReadBoolean(), MatchCommand.Read(r)));
            return record;
        }

        public byte[] ToBytes()
        {
            return MatchWire.Bytes(Write);
        }

        public static MatchRecord FromBytes(byte[] bytes)
        {
            using (BinaryReader r = new BinaryReader(new MemoryStream(bytes)))
                return Read(r);
        }

        //----------------------------------------Finding one's way in it----------------------------------------//
        // A move, where watching stops when going a move forward or back: a side's play, redraw, leader, pass, card taken
        // or discarded, a round's end and the battle's
        public static bool IsMove(MatchEventKind kind)
        {
            switch (kind)
            {
                case MatchEventKind.Redrawn:
                case MatchEventKind.Played:
                case MatchEventKind.LeaderUsed:
                case MatchEventKind.Passed:
                case MatchEventKind.Picked:
                case MatchEventKind.Discarded:
                case MatchEventKind.RoundEnded:
                case MatchEventKind.Ended:
                    return true;
                default:
                    return false;
            }
        }

        // The first move from that event on (that one included), or -1
        public static int NextMove(IList<MatchEvent> events, int from)
        {
            for (int i = Math.Max(0, from); i < events.Count; i++)
                if (IsMove(events[i].Kind))
                    return i;
            return -1;
        }

        // The last move before that event, or 0 (the battle's start) when there is none
        public static int MoveBefore(IList<MatchEvent> events, int before)
        {
            for (int i = Math.Min(before, events.Count) - 1; i >= 0; i--)
                if (IsMove(events[i].Kind))
                    return i;
            return 0;
        }

        // Where the round after the one at that event starts (its RoundStarted), or -1 when it is the last
        public static int NextRound(IList<MatchEvent> events, int at)
        {
            for (int i = Math.Max(0, at + 1); i < events.Count; i++)
                if (events[i].Kind == MatchEventKind.RoundStarted)
                    return i;
            return -1;
        }

        // Where the round at that event started (its RoundStarted, or 0 in the opening); when that is the event itself or
        // just before (watching it start), the round before's
        public static int RoundBefore(IList<MatchEvent> events, int at)
        {
            int start = 0;
            for (int i = Math.Min(at, events.Count - 1); i >= 0; i--)
                if (events[i].Kind == MatchEventKind.RoundStarted)
                {
                    start = i;
                    break;
                }
            if (start > 0 && at - start <= 1)
                return RoundBefore(events, start - 1);
            return start;
        }

        // The round an event is in (0: the opening, before the first)
        public static int RoundAt(IList<MatchEvent> events, int at)
        {
            if (events.Count == 0)
                return 0;
            return events[Math.Max(0, Math.Min(at, events.Count - 1))].State.Round;
        }
    }
}
