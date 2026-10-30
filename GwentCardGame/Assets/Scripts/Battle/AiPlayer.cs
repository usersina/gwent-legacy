using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Battle
{
    // Who decides for the opponent's side of a battle: the AI, or (a LAN battle) the other player's device. The battle
    // asks it when the match waits for that side and everything before has been shown; it answers through the match
    // (Match.Apply): a move, a choice, its redraw.
    public interface IOpponent
    {
        UniTask Act(Match match, bool seat, CancellationToken ct);
    }

    // Plays the opponent's side. Decisions come from Gwent.Rules.AiBrain at the settings' difficulty (a story opponent's
    // own); this class only gives them to the match, and always ends the AI's turn (passes when its move is refused).
    public class AiPlayer : MonoBehaviour, IOpponent
    {
        // In a browser: Hard thinks 10 ms a frame, 0.7 s of thinking at most (a few battles fewer per move than elsewhere)
        private const int SliceMs = 10;
        private const int SlicedTimeLimitMs = 700;
        private long thinkMs; // how long the last decision took (for the log)

        private static bool NoThreads { get { return Application.platform == RuntimePlatform.WebGLPlayer; } }

        private Match match;
        private bool seat;

        public UniTask Act(Match match, bool seat, CancellationToken ct)
        {
            this.match = match;
            this.seat = seat;
            switch (match.Phase)
            {
                case MatchPhase.Redraw:
                    Redraw();
                    return UniTask.CompletedTask;
                case MatchPhase.Turn:
                    return TakeTurn(ct);
                default:
                    Answer();
                    return UniTask.CompletedTask;
            }
        }

        private Board Board { get { return match.Board; } }

        // The settings' difficulty, or a story opponent's own
        private static AiDifficulty Difficulty
        {
            get { return BattleController.Instance != null ? BattleController.Instance.AiDifficulty : GameSettings.AiDifficulty; }
        }
        private CardDatabase Db { get { return match.Board.Db; } }

        //----------------------------------------------AI Phases------------------------------------------------------------//
        // Initial redraw of up to 2 cards (AiBrain.Redraws: a redrawn card goes to the bottom of the deck), then the hand
        // is kept
        private void Redraw()
        {
            PlayerState ai = Board.Side(seat);
            foreach (int card_id in AiBrain.Redraws(ai, Db, Difficulty))
            {
                if (!ai.CanExchange || ai.CardsExchanged >= 2)
                    break;
                if (ai.HandList.Contains(card_id))
                    match.Apply(seat, MatchCommand.Redraw(card_id));
            }
            if (ai.CanExchange)
                match.Apply(seat, MatchCommand.EndRedraw());
            GameLog.Event(LogCategory.Ai, "redraw", "exchanged", ai.CardsExchanged);
        }

        // The AI's turn: Easy and Normal decide at once, Hard while the turn's notice shows; a moment after the notice,
        // its move (refused: it passes)
        private async UniTask TakeTurn(CancellationToken ct)
        {
            UniTask<AiAction> deciding = Decide(ct); // Hard thinks while the turn's notice shows
            await BattleController.Wait(0.3f, ct);
            AiAction action = await deciding;
            await UniTask.WaitWhile(() => BattleBanner.Busy, PlayerLoopTiming.Update, ct);
            await BattleController.Wait(0.15f, ct);

            GameLog.Event(LogCategory.Ai, "decision", "difficulty", Difficulty, "action", Describe(action),
                "think_ms", thinkMs, "considered", Considered());
            if (!match.Apply(seat, MatchCommand.From(action)))
            {
                Debug.LogError("AI move refused, passing: " + action);
                match.Apply(seat, MatchCommand.Pass());
            }
        }

        // A choice the match asks of the AI (AiBrain.Answer): a medic's unit, a leader's card, who goes first...
        private void Answer()
        {
            MatchCommand answer = AiBrain.Answer(Board, seat, match.Choice);
            if (!match.Apply(seat, answer) && !match.Apply(seat, MatchCommand.Skip()))
                Debug.LogError("AI answer refused: " + answer);
        }

        // The move for this turn. Easy and Normal decide at once; Hard plays battles out on its own copy of the board while
        // the turn's notice shows: on another thread, or in a browser (no other thread) a few milliseconds every frame.
        // A decision that fails is taken again as on Normal.
        private async UniTask<AiAction> Decide(CancellationToken ct)
        {
            AiContext ctx = BuildContext();
            considered = GameLog.Enabled ? new List<AiOption>() : null;
            ctx.Considered = considered;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (ctx.Difficulty != AiDifficulty.Hard)
                    return Decide(ctx);
                AiContext copy = Snapshot(ctx);
                if (!NoThreads)
                    return await UniTask.RunOnThreadPool(() => AiBrain.Decide(copy), cancellationToken: ct);
                AiSearch.Thinking thinking = AiSearch.Begin(copy, SlicedTimeLimitMs);
                while (!thinking.Step(SliceMs))
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                return thinking.Result;
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                Debug.LogError("AI decision failed, playing as on Normal: " + e);
                AiContext normal = BuildContext();
                normal.Difficulty = AiDifficulty.Normal;
                return Decide(normal);
            }
            finally
            {
                thinkMs = watch.ElapsedMilliseconds;
            }
        }

        private static AiAction Decide(AiContext ctx)
        {
            try
            {
                return AiBrain.Decide(ctx);
            }
            catch (Exception e)
            {
                Debug.LogError("AI decision failed, passing: " + e);
                return AiAction.Pass();
            }
        }

        // The moves the last decision weighed, for the log: the Normal AI's estimates, then Hard's play-outs
        private List<AiOption> considered;

        private List<Dictionary<string, object>> Considered()
        {
            List<Dictionary<string, object>> options = new List<Dictionary<string, object>>();
            if (considered == null)
                return options;
            foreach (AiOption option in considered)
                options.Add(option.WinRate < 0
                    ? new Dictionary<string, object> { { "move", Describe(option.Action) }, { "gain", option.Gain }, { "board", option.Board }, { "cost", option.Cost } }
                    : new Dictionary<string, object> { { "move", Describe(option.Action) }, { "win_rate", Math.Round(option.WinRate, 3) }, { "battles", option.Battles } });
            return options;
        }

        // A move in words: "pass", "leader (Biting Frost)", "Ballista on Siege", "Decoy on Close for Ballista"
        private string Describe(AiAction action)
        {
            switch (action.Type)
            {
                case AiActionType.Pass:
                    return "pass";
                case AiActionType.UseLeader:
                    return "leader" + (action.CardId >= 0 ? " (" + Db.Get(action.CardId).name + ")" : "");
                default:
                    return Db.Get(action.CardId).name + " on " + action.Row + (action.TargetId >= 0 ? " for " + Db.Get(action.TargetId).name : "");
            }
        }

        // The AI decides on the whole board (the match's): it does not cheat for all that, Hard re-deals the cards it
        // cannot see (AiSearch)
        private AiContext BuildContext()
        {
            AiContext ctx = Board.AiContextFor(seat);
            ctx.Difficulty = Difficulty;
            ctx.Seed = Rng.Shared.Next();
            return ctx;
        }

        // The board as it is now, for a decision made on another thread while the battle goes on
        private static AiContext Snapshot(AiContext ctx)
        {
            return new AiContext
            {
                Me = ctx.Me.Clone(),
                Opponent = ctx.Opponent.Clone(),
                Weather = new List<int>(ctx.Weather),
                DoubleSpies = ctx.DoubleSpies,
                RandomMedic = ctx.RandomMedic,
                Db = ctx.Db,
                Difficulty = ctx.Difficulty,
                Seed = ctx.Seed,
                Considered = ctx.Considered,
            };
        }
    }
}
