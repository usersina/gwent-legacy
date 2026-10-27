using System.Collections.Generic;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;

namespace Gwent.Replays
{
    // The replay being watched: the battle played again from its record (MatchRecord.Play), shown by the battle screen
    // from its side (BattleController with a ReplaySource), no input taken. It plays, pauses, goes faster (1x, 2x, 4x the
    // game speed), and goes a move or a round forward or back. Forward a move: the events up to the next move go fast, the
    // move itself at its pace, then it pauses. Back, or to another round: the battle screen loads again and shows every
    // event before that point at once (as a LAN battle taken up again), then goes on from there (StartAt). It is seen
    // from the side that recorded it, or from the other one (SwitchSide: the battle played again knows both hands), the
    // side seen at the bottom. It lasts across those loads; the main menu forgets it (Forget), and opens the replays again
    // after Leave.
    public sealed class ReplayPlayer
    {
        public static readonly int[] Speeds = { 1, 2, 4 };
        private const float SkipSpeed = 6f; // the events before a move, going to it

        public static ReplayPlayer Current { get; private set; }

        // Left for the replays (Leave): the main menu opens them again
        public static bool ReturnToList { get; private set; }

        public readonly ReplayLibrary.Entry Entry;
        public readonly Match Match;

        public bool Seat { get; private set; }    // the side seen, at the bottom: the match's Player side (true) or Enemy side
        public int StartAt { get; private set; }  // the first event shown as it plays (those before at once)
        public bool Paused { get; private set; }
        public int SpeedIndex { get; private set; }
        public int At { get; private set; }       // the event being shown, or waiting to be
        public bool Begun { get; private set; }   // that event's showing has begun
        private int stepTo = -1;                  // going a move forward: the move's event (-1: not)

        private ReplayPlayer(ReplayLibrary.Entry entry, Match match)
        {
            Entry = entry;
            Match = match;
            Seat = entry.Record.Seat;
        }

        public MatchRecord Record { get { return Entry.Record; } }
        public List<MatchEvent> Events { get { return Match.Events; } }
        public int Speed { get { return Speeds[SpeedIndex]; } }
        public bool Stepping { get { return stepTo >= 0; } }

        // Seen from the other side than the one that recorded it
        public bool Switched { get { return Seat != Record.Seat; } }

        // The side seen and the other: their decks as they went into the battle, their names
        public Deck MyDeck { get { return Seat ? Record.Setup.Player : Record.Setup.Enemy; } }
        public Deck TheirDeck { get { return Seat ? Record.Setup.Enemy : Record.Setup.Player; } }
        public string MyName { get { return Switched ? Record.TheirName : Record.MyName; } }
        public string TheirName { get { return Switched ? Record.MyName : Record.TheirName; } }

        // Starts watching a replay (the battle screen loads): null, or why it can't be watched
        public static string Watch(ReplayLibrary.Entry entry)
        {
            string problem;
            Match match = entry.Record.Play(CardLibrary.Get(), out problem);
            if (match == null)
            {
                GameLog.Event(LogCategory.Game, "replay_refused", "build", entry.Record.Build, "this", BuildInfo.Number);
                return problem;
            }
            Current = new ReplayPlayer(entry, match);
            ReturnToList = false;
            GameLog.Event(LogCategory.Game, "replay_watch", "events", match.Events.Count, "kind", entry.Record.Kind);
            Current.Reload(0, false);
            return null;
        }

        // Out of the replay, to the main menu and its replays
        public static void Leave()
        {
            Current = null;
            ReturnToList = true;
            Time.timeScale = 1f;
            LoadingScreen.Load(SceneNames.Menu);
        }

        // The main menu forgets the replay (another battle is no replay); whether it is to open the replays
        public static bool Forget()
        {
            Current = null;
            bool list = ReturnToList;
            ReturnToList = false;
            return list;
        }

        //----------------------------------------Pace----------------------------------------//
        // The game's time scale while watching: the game speed times the replay's, faster on the way to a move; paused, it
        // stops an event caught in the middle of its showing, where it is (between two events it runs: the next one waits
        // anyway, and what the board settles, as it does after a reload, settles)
        public float TimeScale
        {
            get
            {
                if (Stepping)
                    return GameSettings.GameTimeScale * Speed * (At < stepTo ? SkipSpeed : 1f);
                return Paused && Begun ? 0f : GameSettings.GameTimeScale * Speed;
            }
        }

        // The time scale applied, unless something over the battle stopped time itself (the menu, a sheet, a pile): it
        // puts back what it found
        private void Apply()
        {
            if (!UIOverlay.HasModal)
                Time.timeScale = TimeScale;
        }

        // Whether the battle screen may go on to that event
        public bool MayShow(int index)
        {
            return !Paused || Stepping && index <= stepTo;
        }

        // The battle screen waits to show that event
        public void Reached(int index)
        {
            At = index;
            Begun = false;
            Apply();
        }

        // It shows it now
        public void Showing(int index)
        {
            At = index;
            Begun = true;
            Apply();
        }

        // It showed it: a move forward ends there, paused
        public void Shown(int index)
        {
            if (!Stepping || index < stepTo)
                return;
            stepTo = -1;
            Paused = true;
            Apply();
        }

        //----------------------------------------The controls----------------------------------------//
        public void TogglePause()
        {
            stepTo = -1;
            Paused = !Paused;
            Apply();
        }

        public void NextSpeed()
        {
            SpeedIndex = (SpeedIndex + 1) % Speeds.Length;
            Apply();
        }

        public void SetSpeed(int index)
        {
            SpeedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
            Apply();
        }

        // A move forward: to the next one (the one showing, if it is one), then paused
        public void NextMove()
        {
            int move = MatchRecord.NextMove(Events, At);
            stepTo = move >= 0 ? move : Events.Count - 1;
            Apply();
        }

        // A move back: to the start of the move showing (or of the one before, between moves), paused before it
        public void PreviousMove()
        {
            int move = Begun && At < Events.Count && MatchRecord.IsMove(Events[At].Kind) ? At : MatchRecord.MoveBefore(Events, At);
            Reload(move, true);
        }

        // The next round, from its start (nothing, in the last)
        public void NextRound()
        {
            int round = MatchRecord.NextRound(Events, At);
            if (round > 0)
                Reload(round, Paused);
        }

        // The round showing, from its start; or the one before, when it has only just started
        public void PreviousRound()
        {
            Reload(MatchRecord.RoundBefore(Events, At), Paused);
        }

        // The other side's view, from the event showing (paused or playing, as it was)
        public void SwitchSide()
        {
            Seat = !Seat;
            Reload(At, Paused);
        }

        // From the start, playing (the end screen's Watch again)
        public void Restart()
        {
            Reload(0, false);
        }

        // The battle screen again, showing the events before that one at once
        private void Reload(int start, bool paused)
        {
            StartAt = Mathf.Clamp(start, 0, Events.Count - 1);
            At = StartAt;
            Begun = false;
            Paused = paused;
            stepTo = -1;
            Time.timeScale = 1f; // the loading screen's pace; the battle screen sets the replay's
            LoadingScreen.Load(SceneNames.Game);
        }
    }
}
