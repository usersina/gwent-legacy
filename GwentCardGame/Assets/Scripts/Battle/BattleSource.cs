using System;
using Gwent.Rules;

namespace Gwent.Battle
{
    // Where a battle screen's battle comes from: its own match (playing alone, or hosting a LAN battle: LocalSource), or
    // the host's device (a LAN guest: LanSession's). The screen shows the events one after the other and sends its side's
    // moves through it.
    public abstract class BattleSource
    {
        // The events so far
        public abstract int Count { get; }

        // One of them, as this screen's side sees it (itself at the bottom: MatchEvent.For)
        public abstract MatchEvent Seen(int index);

        // The same event whole, for the log: all of it where this device has it (the AI's redraws, its hand), else as seen
        public abstract MatchEvent All(int index);

        // This side's move: taken (its events follow), or false when it is refused at once
        public abstract bool Submit(MatchCommand command);

        // A move sent and not answered yet (a guest's, on its way to the host): nothing else is taken meanwhile
        public virtual bool Waiting { get { return false; } }

        // A move sent was refused after all (the host's answer): the screen shows the battle as it was again
        public event Action Refused;

        protected void OnRefused()
        {
            Refused?.Invoke();
        }
    }

    // The screen's own match, its side the Player's
    public sealed class LocalSource : BattleSource
    {
        private readonly Match match;

        public LocalSource(Match match)
        {
            this.match = match;
        }

        public override int Count { get { return match.Events.Count; } }
        public override MatchEvent Seen(int index) { return match.Events[index].For(true); }
        public override MatchEvent All(int index) { return match.Events[index]; }
        public override bool Submit(MatchCommand command) { return match.Apply(true, command); }
    }
}

namespace Gwent.Battle
{
    // A replay's battle, played again from its record (Gwent.Replays.ReplayPlayer): every event is there from the start,
    // seen from one side, at the bottom (the one that recorded it, or the other: a LAN guest's is the match's Enemy
    // side); it takes no move
    public sealed class ReplaySource : BattleSource
    {
        private readonly Match match;
        private readonly bool seat;

        public ReplaySource(Match match, bool seat)
        {
            this.match = match;
            this.seat = seat;
        }

        public override int Count { get { return match.Events.Count; } }
        public override MatchEvent Seen(int index) { return match.Events[index].For(seat); }

        // Whole from the match's Player side (as against the AI, as the host), else as seen (as the guest)
        public override MatchEvent All(int index) { return seat ? match.Events[index] : Seen(index); }

        public override bool Submit(MatchCommand command) { return false; }
    }
}
