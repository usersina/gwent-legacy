using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.Replays;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Battle
{
    // Replays: a battle over is saved from its end screen (Save replay: ReplayLibrary; against the AI and as a LAN host,
    // this device's match, as a LAN guest the one its fair play check played, once it passed), and a replay is shown by this screen as a battle (ReplayPlayer, ReplaySource): its
    // events from the side that recorded it, no input taken, no AI and no clock, at the replay's pace (paused, faster, a
    // move forward: it gates each event, ShowEvents). The player's own plays pop out of the hand to the showcase a moment,
    // as the opponent's come in; the choices they made don't open (what they chose follows); events that show nothing
    // that takes time get a beat, to be followed. Its controls: ReplayBar.
    public partial class BattleController
    {
        private ReplayPlayer replay;    // the replay watched (null: a battle played)
        private bool replaySaved;

        // The replay saved from this battle's end screen
        public bool ReplaySaved { get { return replaySaved; } }

        // Whether the battle over can be saved as a replay now: this device's match (against the AI, as a LAN host), or
        // the LAN guest's once its fair play check passed (it comes after the end)
        public bool CanSaveReplay
        {
            get { return !replaySaved && replay == null && (match != null ? match.IsOver : lan != null && lan.Checked != null); }
        }

        // Watching a replay (no input taken)
        public bool Replaying { get { return replay != null; } }

        // The time scale a battle runs at once nothing stops it: the game speed, or a replay's own (paused, faster)
        public static float PlayingTimeScale
        {
            get { return Instance != null && Instance.replay != null ? Instance.replay.TimeScale : GameSettings.GameTimeScale; }
        }

        // The hand and the deck of the side at the bottom (a replay's switch goes between them)
        internal RectTransform BottomHand { get { return (RectTransform)View(true).Hand.transform; } }
        internal Transform BottomDeck { get { return View(true).Deck.transform; } }

        // The replay's battle: both decks as they went in (the side seen at the bottom), its events, nobody to ask
        private void SetupReplay()
        {
            MyDeck = replay.MyDeck;
            TheirDeck = replay.TheirDeck;
            opponentName = TheirDeck.Name;
            match = null; // nobody to ask for moves: they are all in the source
            opponent = null;
            source = new ReplaySource(replay.Match, replay.Seat);
        }

        // The battle over saved as a replay (once): whether it was (a full disk: not)
        public bool SaveReplay()
        {
            if (!CanSaveReplay)
                return false;
            ReplayLibrary.Entry saved = match != null
                ? ReplayLibrary.Save(match, Me, lan != null ? RecordKind.LanHost : RecordKind.Ai, PlayerInfo.Name, EnemyInfo.Name)
                : ReplayLibrary.Save(lan.Checked, false, RecordKind.LanGuest, PlayerInfo.Name, EnemyInfo.Name);
            replaySaved = saved != null;
            return replaySaved;
        }

        // The player's own card in a replay: out of the hand (or the leader's slot) to the showcase, a moment there
        private async UniTask ShowcaseMine(int card_id, bool leader, CancellationToken ct)
        {
            GameObject holder = leader ? View(true).LeaderHolder : View(true).Hand;
            Transform card = null;
            string name = card_id.ToString();
            foreach (Transform child in holder.transform)
                if (child.name == name)
                {
                    card = child;
                    break;
                }
            if (card == null)
                return;
            CardShowcase.Select(card.gameObject, Db.Get(card_id), !leader);
            try
            {
                await Wait(CardShowcase.GlideSeconds + CardShowcase.HoldSeconds, ct);
            }
            catch (OperationCanceledException)
            {
                CardShowcase.Clear();
                throw;
            }
        }

        // A notice's words: as a battle says them to its player; in a replay, which is watched rather than played (and may
        // be seen from the other side), the sides by their names instead
        private string Said(string in_battle, string in_replay)
        {
            return replay != null ? in_replay : in_battle;
        }

        // A side's name as the board shows it (the bottom side's: is_player)
        private string NameOf(bool is_player)
        {
            return is_player ? PlayerInfo.Name : EnemyInfo.Name;
        }

        // In a replay, a beat after an event that shows nothing that takes time (a redraw, a choice the player made, a
        // pass), so that it can be followed
        private UniTask ReplayBeat(MatchEvent e, CancellationToken ct)
        {
            float beat;
            switch (e.Kind)
            {
                case MatchEventKind.Redrawn: beat = 0.6f; break;
                case MatchEventKind.RedrawDone: beat = 0.4f; break;
                case MatchEventKind.ChoiceAsked: beat = e.State.Choice != null && e.State.Choice.Seat ? 0.6f : 0f; break;
                case MatchEventKind.Skipped: beat = 0.5f; break;
                case MatchEventKind.Passed: beat = 0.3f; break;
                default: beat = 0f; break;
            }
            return beat > 0 ? Wait(beat, ct) : UniTask.CompletedTask;
        }
    }
}
