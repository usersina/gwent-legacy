using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Gwent.Rules.Tests
{
    // Replays (MatchRecord): a battle over, kept from either side, comes back from its bytes and plays again event for
    // event; one this build plays otherwise (a move it refuses, other events) says so; going a move or a round forward
    // and back finds the right events
    public class MatchRecordTests
    {
        private static CardDatabase Db { get { return TestBoard.Db; } }

        // A battle the AI plays for both sides, to the end
        private static Match Played(int game, int leader = LeaderIds.EredinBringerOfDeath)
        {
            Match match = new Match(new MatchSetup { Player = MatchTests.Monsters(), Enemy = MatchTests.Monsters(leader), Seed = 1000 + game }, Db);
            Random rnd = new Random(game);
            match.Start();
            for (int step = 0; !match.IsOver; step++)
            {
                Assert.Less(step, 2000, "the battle never ended");
                bool seat = match.Awaits(true) || !match.Awaits(false) && rnd.Next(2) == 0;
                if (!match.Awaits(seat))
                    seat = !seat;
                Assert.IsTrue(match.Apply(seat, MatchTests.AiCommand(match, seat, rnd.Next())), "a move of the AI's refused");
            }
            return match;
        }

        [Test]
        public void ABattleOverIsRecordedFromEitherSide([Values(true, false)] bool seat)
        {
            Match match = Played(1);
            MatchRecord record = MatchRecord.Of(match, seat, seat ? RecordKind.Ai : RecordKind.LanGuest, "Me", "Them");
            Assert.That(record, Is.Not.Null);
            Assert.That(record.Seat, Is.EqualTo(seat));
            Assert.That(record.MyDeck, Is.SameAs(seat ? match.Setup.Player : match.Setup.Enemy));
            Assert.That(record.Events, Is.EqualTo(match.Events.Count));
            Assert.That(record.Commands.Count, Is.EqualTo(match.Commands.Count));
            RoundWinner outcome = seat ? match.Result : MatchState.Flipped(match.Result);
            Assert.That(record.Outcome, Is.EqualTo(outcome), "the outcome as the recording side saw it");
            PlayerState mine = match.Board.Side(seat);
            Assert.That(record.MyRounds, Is.EqualTo(mine.RoundsWon.Count(won => won)));
        }

        [Test]
        public void ABattleNotOverIsNotRecorded()
        {
            Match match = new Match(new MatchSetup { Player = MatchTests.Monsters(), Enemy = MatchTests.Monsters(), Seed = 7 }, Db);
            match.Start();
            Assert.That(MatchRecord.Of(match, true, RecordKind.Ai, "", ""), Is.Null);
        }

        [Test]
        public void ARecordComesBackFromItsBytesAndPlaysTheSameBattle([Values(1, 2, 3)] int game)
        {
            Match match = Played(game);
            MatchRecord record = MatchRecord.Of(match, true, RecordKind.LanHost, "Host", "Guest");
            record.Build = 332;
            record.BuildId = "1.0.0+da4254ce";
            MatchRecord back = MatchRecord.FromBytes(record.ToBytes());

            Assert.That(back.Build, Is.EqualTo(332));
            Assert.That(back.BuildId, Is.EqualTo("1.0.0+da4254ce"));
            Assert.That(back.Kind, Is.EqualTo(RecordKind.LanHost));
            Assert.That(back.MyName, Is.EqualTo("Host"));
            Assert.That(back.TheirName, Is.EqualTo("Guest"));
            Assert.That(back.Saved, Is.EqualTo(record.Saved));
            Assert.That(back.Fingerprint, Is.EqualTo(record.Fingerprint));
            Assert.That(back.Setup.Seed, Is.EqualTo(match.Setup.Seed));
            Assert.That(back.Setup.Player.Cards, Is.EqualTo(match.Setup.Player.Cards));

            string problem;
            Match replayed = back.Play(Db, out problem);
            Assert.That(problem, Is.Null);
            Assert.That(replayed.Events.Count, Is.EqualTo(match.Events.Count));
            for (int i = 0; i < match.Events.Count; i++)
                Assert.That(MatchWire.Bytes(w => MatchWire.Write(w, replayed.Events[i])), Is.EqualTo(MatchWire.Bytes(w => MatchWire.Write(w, match.Events[i]))),
                    "event " + i + " plays the same");
        }

        [Test]
        public void ABuildThatPlaysItOtherwiseSaysSo()
        {
            Match match = Played(4);
            MatchRecord record = MatchRecord.Of(match, true, RecordKind.Ai, "", "");
            record.Build = 300;
            string problem;

            // Other events for the same moves (the rules or cards changed): another fingerprint
            MatchRecord changed = MatchRecord.FromBytes(record.ToBytes());
            changed.Fingerprint = "0123456789abcdef0123456789abcdef";
            Assert.That(changed.Play(Db, out problem), Is.Null);
            StringAssert.Contains("build 300", problem);

            // A move this build refuses
            MatchRecord refused = MatchRecord.FromBytes(record.ToBytes());
            int play = refused.Commands.FindIndex(c => c.command.Kind == CommandKind.Play);
            refused.Commands[play] = (refused.Commands[play].seat, MatchCommand.Play(-5, Row.Close));
            Assert.That(refused.Play(Db, out problem), Is.Null);
            Assert.That(problem, Is.Not.Null);

            // Moves missing: it stops before its end
            MatchRecord cut = MatchRecord.FromBytes(record.ToBytes());
            cut.Commands.RemoveRange(cut.Commands.Count / 2, cut.Commands.Count - cut.Commands.Count / 2);
            Assert.That(cut.Play(Db, out problem), Is.Null);
            Assert.That(problem, Is.Not.Null);
        }

        [Test]
        public void AnotherFormatIsRefused()
        {
            MatchRecord record = MatchRecord.Of(Played(5), true, RecordKind.Ai, "", "");
            byte[] bytes = record.ToBytes();
            bytes[0] = MatchRecord.Format + 1;
            Assert.Throws<InvalidDataException>(() => MatchRecord.FromBytes(bytes));
        }

        //----------------------------------------A move, a round forward and back----------------------------------------//
        [Test]
        public void MovesForwardAndBackLandOnMoves()
        {
            List<MatchEvent> events = Played(6).Events;
            for (int at = 0; at < events.Count; at++)
            {
                int next = MatchRecord.NextMove(events, at);
                if (next >= 0)
                {
                    Assert.That(next, Is.GreaterThanOrEqualTo(at));
                    Assert.That(MatchRecord.IsMove(events[next].Kind), "forward: " + events[next].Kind + " is a move");
                    for (int i = at; i < next; i++)
                        Assert.That(MatchRecord.IsMove(events[i].Kind), Is.False, "no move skipped");
                }
                int back = MatchRecord.MoveBefore(events, at);
                Assert.That(back, Is.LessThan(Math.Max(1, at)));
                if (back > 0)
                    Assert.That(MatchRecord.IsMove(events[back].Kind), "back: " + events[back].Kind + " is a move");
            }
            Assert.That(MatchRecord.NextMove(events, events.Count - 1), Is.EqualTo(events.Count - 1), "the end is one");
        }

        [Test]
        public void RoundsForwardAndBackLandOnRoundStarts()
        {
            List<MatchEvent> events = Played(7).Events;
            List<int> starts = Enumerable.Range(0, events.Count).Where(i => events[i].Kind == MatchEventKind.RoundStarted).ToList();
            Assert.That(starts.Count, Is.GreaterThanOrEqualTo(2), "a battle of two rounds at least");

            Assert.That(MatchRecord.NextRound(events, 0), Is.EqualTo(starts[0]), "from the opening, the first round");
            Assert.That(MatchRecord.NextRound(events, starts[0] + 3), Is.EqualTo(starts[1]), "from within the first, the second");
            Assert.That(MatchRecord.NextRound(events, starts[starts.Count - 1]), Is.EqualTo(-1), "none after the last");

            Assert.That(MatchRecord.RoundBefore(events, starts[1] + 3), Is.EqualTo(starts[1]), "within a round: its start");
            Assert.That(MatchRecord.RoundBefore(events, starts[1] + 1), Is.EqualTo(starts[0]), "just started: the round before");
            Assert.That(MatchRecord.RoundBefore(events, starts[0] + 1), Is.EqualTo(0), "the first just started: the opening");
            Assert.That(MatchRecord.RoundAt(events, starts[1] + 2), Is.EqualTo(2));
        }
    }
}
