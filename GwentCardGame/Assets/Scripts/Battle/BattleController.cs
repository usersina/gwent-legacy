using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using Gwent.Menus;
using Gwent.Net;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    public enum BattleState { Start, PlayerTurn, EnemyTurn, RoundOver, Won, Lost, Draw }

    // A multi-step player action in progress. While one is pending the turn cannot end (pass / leader are blocked).
    public enum PendingAction { None, PanelChoice, PlacePickedCard, DiscardCards }

    // The battle's screen: it shows a match (Gwent.Rules.Match) and takes this side's moves. The match plays the battle
    // (the deal, turns, rounds, every rule); this shows its events one after the other (the board's changes as The
    // Witcher 3 plays them, the notices, the sounds) at the screen's pace, and turns the player's clicks into commands for
    // it. The screen is the Player side's, wherever its battle comes from (BattleSource): its own match against the AI
    // (IOpponent: asked when the match waits for it and everything before has been shown), its match as a LAN battle's
    // host (the guest's moves come over the link), or the host's events as its guest (LanSession). While the other device
    // of a LAN battle is away, the board waits under a notice (LanNotice). (Leaders: BattleController.Leaders.cs;
    // pickers: BattleController.Pickers.cs.)
    public partial class BattleController : MonoBehaviour
    {
        private const string DefaultDeckResource = "Decks/player_default";
        private const bool Me = true; // this screen's side of the match

        public static BattleController Instance { get; private set; }

        public BattleState battleState;
        public PendingAction pendingAction;

        public GameObject PostGame;
        public GameObject CardPrefab;
        public GameObject WeatherField;

        public TextAsset CardsDatabase;
        public TextAsset EnemyDeck;

        public GameObject PlayerField;
        public GameObject EnemyField;

        // The battle as this screen shows it: the event shown last left it so (MatchState.For: the opponent's hand hidden)
        public BattleSide PlayerInfo;
        public BattleSide EnemyInfo;

        // In-play related
        public GameObject TurnPicker;
        public GameObject Panel;
        public GameObject PassBtn;
        // Medic related
        public GameObject hiderObject;
        public GameObject selectedCard;
        public List<int> weatherList;
        public bool doubleSpies;
        public bool randomMedic;

        public CardDatabase Db { get; private set; }
        public GameObject TempObject { get; private set; }

        private Match match;              // this device's (null: a LAN guest's battle is the host's)
        private BattleSource source;
        private LanSession lan;           // a LAN battle's (null: against the AI)
        private int lanBattle;            // which of the session's battles this screen shows (a rematch's is another screen)
        private int shown;                // the match's events shown so far
        private MatchState latest;        // the battle as the event shown last left it, as this side sees it
        private MatchState logged;        // the whole battle as the event shown last left it (the log tells all of it)
        private MatchEvent showing;       // the event being shown, whole where this device has it (the log tells all of it)
        private LanNotice away;           // the other device away, said over the board
        private IOpponent opponent;
        private bool opponentActing;
        private BoardView board;
        private BoardPresenter presenter;
        private int presenting;           // plays being shown (the rules resolved them already)
        private readonly List<BoardChange> presented = new List<BoardChange>();
        private GameObject mulliganPrompt;
        private CancellationTokenSource flow; // the battle's flow: its opening and its events shown (giving up cancels it)
        private bool roundOpening;      // the next turn opens a round (after "Round Start")
        private bool? lastTurnWasPlayer; // whose turn was announced last in this round
        private string whiteFlameNotice;  // shown once the redraw is over
        private string deckProblem;       // why the selected deck gave way to the default one, said as the battle opens

        [Serializable]
        public class BattleSide : PlayerState
        {
            public string Name;
            public Sprite DeckBSprite;
        }

        private void Awake()
        {
            Instance = this;
            Db = CardLibrary.Get(CardsDatabase);
            TempObject = GameObject.Find("Temp");
            board = new BoardView(this);
            presenter = new BoardPresenter(board, Db);
            flow = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            ScreenAwake.Hold(this);
            LayOutForScreen();
        }

        // The board on any screen: the 1920 x 1080 layout whole in the middle (the canvas fits it), the table's wood
        // covering the rest; on a touch screen the menu button takes a finger over a larger square, and on a phone it is
        // a size larger (UIKit.PhoneScale), its corner where it was, as are the buttons beside and under it; the pinch zoom
        private void LayOutForScreen()
        {
            GameObject table = GameObject.Find("Main Canvas/Background");
            if (table != null)
                ((RectTransform)table.transform).Cover(16f / 9f);
            GameObject menu = GameObject.Find("Main Canvas/PauseButton");
            float phone = UIKit.PhoneScale;
            if (menu != null)
            {
                RectTransform menu_rect = (RectTransform)menu.transform;
                UIKit.GrowTouchArea(menu_rect, new Vector2(120, 120));
                menu_rect.anchoredPosition += menu_rect.rect.size / 2 * new Vector2(1, -1) * (phone - 1);
                menu_rect.localScale = new Vector3(phone, phone, 1);
            }
            // Two fingers zoom the board in; a button (or Escape) brings it back whole
            GameObject canvas = GameObject.Find("Main Canvas");
            if (canvas != null)
                BoardZoom.Attach(canvas.GetComponent<Canvas>(), menu != null ? (RectTransform)menu.transform : null);

            // The "?" beside the menu button: How to play, over the battle
            if (canvas != null && menu != null)
            {
                RectTransform menu_rect = (RectTransform)menu.transform;
                Button help = UIKit.RoundButton(canvas.transform, "HelpButton", "?", null, OpenRules, 46, true);
                RectTransform rect = (RectTransform)help.transform;
                rect.Place(new Vector2(0.5f, 0.5f), menu_rect.anchoredPosition + new Vector2(70 * phone, 0), new Vector2(46, 46));
                rect.localScale = new Vector3(phone, phone, 1);
                rect.SetSiblingIndex(menu_rect.GetSiblingIndex() + 1); // under the pickers and the end screen
                UIKit.GrowTouchArea(rect, new Vector2(70, 110)); // not over the menu button's
                help.gameObject.AddComponent<UITooltip>().text = () => "How to play";
                // The round button's text sits a little high for a "×"; a "?" is centred as it is
                help.GetComponentInChildren<TMPro.TextMeshProUGUI>().rectTransform.Stretch(0, 2, 2.4f, 0);
            }
        }

        // The "?": the rules page of the main menu (How to play) over the battle. The battle waits while it is open, as under
        // the pause menu; a LAN battle goes on, on the other device too
        private void OpenRules()
        {
            if (UIOverlay.HasModal || IsBattleOver)
                return;
            bool stop = lan == null;
            if (stop)
                Time.timeScale = 0f;
            Gwent.Menus.RulesPage.Open(() =>
            {
                if (stop && this != null && !Gwent.Menus.PauseMenu.IsOpen)
                    Time.timeScale = GameSettings.GameTimeScale;
            });
        }

        private void OnDestroy()
        {
            ScreenAwake.Release(this);
            if (lan != null)
                lan.Changed -= LanChanged;
            if (flow != null)
                flow.Dispose();
            if (Instance == this)
            {
                if (!IsBattleOver)
                    BattleLog.Left();
                Instance = null;
                Time.timeScale = 1f; // the game speed is the battle's
            }
        }

        public LeaderSlot PlayerLeader { get { return View(true).Leader; } }

        // The match this screen shows (the whole battle, the AI's hand too: for the AI and the tools); null on a LAN guest
        public Match Match { get { return match; } }

        // The battle as this side sees it now (the event shown last), and a move of its own, as the clicks make them: for
        // the tools that play a side (LanBot)
        internal MatchState Latest { get { return latest; } }
        internal bool Command(MatchCommand command) { return Submit(command); }

        private void Start()
        {
            LanSession meant = LanSession.ForBattle;
            bool lan_battle = meant is object; // a session closed meanwhile is a destroyed object: "!= null" would say none
            lan = lan_battle && meant.State == LanSession.Stage.Battle ? meant : null;
            if (lan_battle && lan == null)
            {
                // The LAN battle this was loaded for closed meanwhile (the other player left, a battle no longer there):
                // why, and the way back, over the empty board (no battle against the AI instead)
                LanSession.ForgetBattle();
                HandArtLoaded = true;
                battleState = BattleState.Draw; // nothing to play (the pause menu offers the main menu)
                UIOverlay.SetEscapeAction(OnEscape);
                away = LanNotice.Show(meant, true, () => LoadingScreen.Load(SceneNames.Menu));
                return;
            }
            if (!GameInput.IsTouch)
                gameObject.AddComponent<BattleKeys>();
            View(true).Deck.gameObject.AddComponent<DeckGiveUp>(); // a hand laid on your deck gives up
            GameAudio.PlayGameMusic();
            Time.timeScale = GameSettings.GameTimeScale;
            PlayerInfo.Name = lan != null ? lan.MyName : "Player";
            EnemyInfo.Name = lan != null ? lan.TheirName : "Opponent";

            UIOverlay.SetEscapeAction(OnEscape);
            battleState = BattleState.Start;
            SetupBattle();
            CardEffects.Prepare(); // no effect waits for its art the first time it plays
            BattleOpening(flow.Token).Forget();
        }

        // The hands dealt (on a LAN guest, once the host's first event is in: the loading screen waits for the hand's art
        // meanwhile), then The Witcher 3's opening once the loading screen has gone: the leaders' notice (The White Flame),
        // then the match's events: who goes first (a coin toss, or the Scoia'tael perk), the redraw, the rounds
        private async UniTaskVoid BattleOpening(CancellationToken ct)
        {
            if (source.Count == 0)
                await UniTask.WaitUntil(() => source.Count > 0, PlayerLoopTiming.Update, ct);
            ShowStarted(source.Seen(0), source.All(0).State);
            shown = 1;
            PreloadArt(MyDeck, TheirDeck).Forget();
            BattleLog.Begin(this, MyDeck, TheirDeck, opponentName);

            await While(() => LoadingScreen.Showing, ct); // the field fades up first
            GameAudio.PlaySfx("game_start"); // as it shows, the hands dealt (the loading screen may take a while on a phone)
            GameAudio.PlaySfx("card_draw");
            if (deckProblem != null)
                UIOverlay.Toast("Your selected deck can't be used (" + deckProblem + "). Playing with the default deck.", 4f);

            if (whiteFlameNotice != null)
            {
                BattleBanner.Show(BattleBanner.FactionEmblem(Faction.Nilfgaard), whiteFlameNotice, BattleBanner.Long);
                whiteFlameNotice = null;
            }
            await ShowEvents(ct);
        }

        public Rect DeckScreenRect(bool is_player) { return board.DeckRect(is_player); }

        // The player's hand row on the screen (the LAN talk window opens over it)
        public Rect HandScreenRect { get { return CardMotion.ScreenRect((RectTransform)View(true).Hand.transform); } }

        // The line between the two halves of the board (the close combat rows'), on the screen: a LAN turn clock's fuse
        internal Rect MiddleScreenRect
        {
            get
            {
                // Each side's close combat row: its horn slot on the left, its units' place on the right
                Rect Row(bool is_player)
                {
                    RectTransform horn_rect = View(is_player).Rows[Gwent.Rules.Row.Close].Special.transform as RectTransform;
                    Rect units = CardMotion.ScreenRect((RectTransform)View(is_player).Rows[Gwent.Rules.Row.Close].Unit.transform);
                    Rect horn = horn_rect != null ? CardMotion.ScreenRect(horn_rect) : units;
                    return Rect.MinMaxRect(Mathf.Min(horn.xMin, units.xMin), Mathf.Min(horn.yMin, units.yMin), Mathf.Max(horn.xMax, units.xMax), Mathf.Max(horn.yMax, units.yMax));
                }
                Rect mine = Row(true), theirs = Row(false);
                float y = (Mathf.Min(mine.yMax, theirs.yMax) + Mathf.Max(mine.yMin, theirs.yMin)) / 2; // between the two
                return Rect.MinMaxRect(Mathf.Min(mine.xMin, theirs.xMin), y - 1, Mathf.Max(mine.xMax, theirs.xMax), y + 1);
            }
        }

        // The redraw's band while the redraw is open (its clock's fuse burns along its foot), else null
        internal RectTransform RedrawBand { get { return mulliganPrompt != null ? (RectTransform)mulliganPrompt.transform.Find("Band") : null; } }

        private void Update()
        {
            if (battleState == BattleState.Start && PlayerInfo.CanExchange && mulliganPrompt != null && ShouldSkipCardExchange())
            {
                GameLog.Event(LogCategory.Battle, "redraw_skipped");
                FinishCardExchange();
            }

            // The pass button only exists during the player's turn, and not in the middle of a multi-step action
            bool can_pass = battleState == BattleState.PlayerTurn && pendingAction == PendingAction.None && !IsPresenting;
            if (PassBtn.activeSelf != can_pass)
                PassBtn.SetActive(can_pass);

            board.UpdateMarkers();

            // A LAN battle's clock runs once this screen has shown all there is (its animations are not the player's time)
            if (lan != null && presenting == 0 && source != null && shown >= source.Count)
                lan.ScreenShown(shown, lanBattle);
        }

        // Right click (or a second finger) keeps the hand; Escape opens the pause menu instead
        private bool ShouldSkipCardExchange()
        {
            return !UIOverlay.BlocksCancel && (GameInput.SecondaryPressed() || GameInput.SecondFingerTapped());
        }

        //----------------------------------------------Info helpers---------------------------------------------------//
        private BattleSide Info(bool is_player) { return is_player ? PlayerInfo : EnemyInfo; }
        private BattleSide Opponent(bool is_player) { return is_player ? EnemyInfo : PlayerInfo; }
        private BoardView.FieldView View(bool is_player) { return board.View(is_player); }

        // A side's leader slot and side panel (the player sheet reads them)
        public LeaderSlot LeaderOf(bool is_player) { return View(is_player).Leader; }
        public SidePanel PanelOf(bool is_player) { return View(is_player).Stats; }

        public bool IsPlayerActionAllowed
        {
            get { return battleState == BattleState.PlayerTurn && pendingAction == PendingAction.None && !IsPresenting; }
        }

        // The player may give up (a hand on their deck): in their turn, nothing pending or being shown; in a LAN battle at
        // any time, the other player there (holding its turn forever is no way to win)
        public bool CanGiveUp { get { return !IsBattleOver && (IsPlayerActionAllowed || lan != null && lan.Linked && shown > 0); } }

        // A play is being shown, the match has moved on and the screen not yet, a move is on its way to a LAN host, or the
        // other device of a LAN battle is away: nothing else can be done until then
        public bool IsPresenting
        {
            get { return presenting > 0 || source != null && (shown < source.Count || source.Waiting) || lan != null && !lan.Linked; }
        }

        // The other device of a LAN battle away (its link lost, or not back yet): its side shows greyed out
        public bool OpponentAway { get { return lan != null && lan.State == LanSession.Stage.Battle && !lan.Linked; } }

        // The battle as the log tells it: all of it (the opponent's hand too), else what this screen shows
        internal PlayerState Logged(bool is_player) { return logged != null ? (is_player ? logged.Player : logged.Enemy) : Info(is_player); }
        internal List<int> LoggedWeather { get { return logged != null ? logged.Weather : weatherList; } }

        //----------------------------------------------BATTLE SETUP---------------------------------------------------//
        private Deck MyDeck, TheirDeck;
        private string opponentName;

        // Against the AI: the match from both decks (the opponent the player chose before the battle, or a random one),
        // its seed from the game's random source (a seeded autoplay replays the same battles). A LAN battle's is the
        // session's: the host's match, or the host's events.
        private void SetupBattle()
        {
            if (lan != null)
            {
                match = lan.Match;
                source = lan.Source;
                opponent = lan.Hosting ? lan.Opponent : null;
                MyDeck = lan.MyDeck;
                TheirDeck = lan.TheirDeck;
                opponentName = lan.TheirName;
                // Until the first event is in (a battle taken up again, the other device not back yet): both sides as their
                // decks say
                foreach (bool is_player in new[] { true, false })
                {
                    Deck deck = is_player ? MyDeck : TheirDeck;
                    Info(is_player).Faction = deck.Faction;
                    Info(is_player).Leader = deck.Leader;
                    View(is_player).Stats.UISetGeneral(deck.Faction, is_player);
                    View(is_player).Stats.UISetPlayer(Info(is_player).Name, deck.Leader);
                }
                lanBattle = lan.Battle;
                lan.Changed += LanChanged;
                ChatView.Attach(this, lan, View(true).Stats, View(false).Stats);
                TurnFuse.Attach(this, lan);
                LanChanged(); // a battle taken up again starts with the other device away
            }
            else
            {
                MyDeck = PlayerDeck(Db, out deckProblem);
                TextAsset enemy_deck = OpponentDecks.ForBattle() ?? EnemyDeck;
                TheirDeck = DeckRules.Sanitize(CardLibrary.ParseDeck(enemy_deck.text, enemy_deck.name), Db);
                opponentName = enemy_deck.name;
                match = new Match(new MatchSetup { Player = MyDeck, Enemy = TheirDeck, Seed = Rng.Shared.Next() }, Db);
                match.Start();
                source = new LocalSource(match);
                opponent = GetComponent<AiPlayer>();
            }
            source.Refused += ShowRefused;
        }

        // The deck the player battles with: the selected one if it exists and is valid, else the built-in default deck
        // (problem: why the selected one could not be used, or null). The loading screen shows its crest.
        public static Deck PlayerDeck(CardDatabase db, out string problem)
        {
            problem = null;
            string path = DeckStorage.BattleDeckPath;
            if (DeckStorage.Exists(path)) // a deck file, or a preset (an AI deck)
            {
                Deck deck = DeckRules.Sanitize(DeckStorage.Load(path), db); // copies beyond the W3 limits are dropped
                List<string> errors = DeckRules.Validate(deck, db);
                if (errors.Count == 0)
                {
                    return deck;
                }
                problem = errors[0];
            }

            TextAsset deckAsset = Resources.Load<TextAsset>(DefaultDeckResource);
            return DeckRules.Sanitize(CardLibrary.ParseDeck(deckAsset.text, DefaultDeckResource), db);
        }

        // True once the hand's art is there (the loading screen waits for it, so the field comes up with its cards)
        public bool HandArtLoaded { get; private set; }

        // The cards' art before it is seen: the player's hand first (the redraw shows it at once), then both decks
        private async UniTaskVoid PreloadArt(Deck mine, Deck theirs)
        {
            await CardArt.Preload(PlayerInfo.HandList, "hand", destroyCancellationToken);
            HandArtLoaded = true;
            await CardArt.Preload(mine.Cards.Concat(theirs.Cards).Append(mine.Leader).Append(theirs.Leader), "battle", destroyCancellationToken);
        }

        // The field as the battle starts: each side's faction, leader, deck and hand. Emhyr, The White Flame cancels the
        // other leader's ability (the match decided it): its leader shows it, said once the field is up.
        private void ShowStarted(MatchEvent started, MatchState all)
        {
            ApplyView(started.State, all);
            foreach (bool is_player in new[] { true, false })
            {
                BattleSide info = Info(is_player);
                info.DeckBSprite = Resources.Load<Sprite>("Cards/Back/" + info.Faction.Code());
                View(is_player).Stats.UISetGeneral(info.Faction, is_player);
                View(is_player).Stats.UISetPlayer(info.Name, info.Leader);
                SetLeaderCard(info.Leader, is_player);
                SetDeckObject(info, is_player);
                SetHandObject(info.HandList, is_player);
                if (info.leaderDisabled)
                {
                    View(is_player).Leader.MarkCancelled();
                    whiteFlameNotice = is_player ? "Your opponent's leader cancels your leader ability" : "Your leader cancels your opponent's leader ability";
                }
            }
        }

        // Sets the leader in the UI
        public void SetLeaderCard(int leader_id, bool is_player_deck)
        {
            BoardView.FieldView view = View(is_player_deck);
            view.Leader.type = Db.Get(leader_id).row;
            CreateCardObjects(view.LeaderHolder, new List<int> { leader_id }, false, is_player_deck);
            view.Leader.UISetPassive();
        }

        // The battle as an event left it, seen from this side (all: the whole of it, for the log)
        private void ApplyView(MatchState state, MatchState all)
        {
            PlayerInfo.CopyFrom(state.Player);
            EnemyInfo.CopyFrom(state.Enemy);
            weatherList.Clear();
            weatherList.AddRange(state.Weather);
            doubleSpies = state.DoubleSpies;
            randomMedic = state.RandomMedic;
            latest = state;
            logged = all;
        }

        //----------------------------------------------The player's commands---------------------------------------------------//
        // A move of the player's for the match; false (and the "not allowed" sound) when it is refused
        private bool Submit(MatchCommand command)
        {
            if (source.Submit(command))
                return true;
            GameAudio.PlaySfx("invalid");
            GameLog.Event(LogCategory.Battle, "command_refused", "command", command.ToString());
            return false;
        }

        // The Scoia'tael dialog's answer
        public void ChooseFirstPlayer(bool player_first)
        {
            Submit(MatchCommand.ChooseFirst(player_first));
        }

        public void FinishCardExchange()
        {
            if (PlayerInfo.CanExchange)
                Submit(MatchCommand.EndRedraw());
        }

        // One redraw of the player's (double-click in the hand, or the redraw picker); the second one ends the phase.
        // hand_index: the card's place in the hand, when known (with two alike, the one chosen)
        public void RedrawPlayerCard(int card_id, int hand_index = -1)
        {
            if (!PlayerInfo.CanExchange || PlayerInfo.CardsExchanged >= 2 || !PlayerInfo.HandList.Contains(card_id) || IsPresenting)
                return;
            Submit(MatchCommand.Redraw(card_id, hand_index));
        }

        // The menu button at the top left
        public void OpenPauseMenu()
        {
            PauseMenu.Open();
        }

        // Reference: PassButton event (hold to pass button)
        public void SkipTurn(bool is_player_turn)
        {
            if (!is_player_turn || !IsPlayerActionAllowed)
            {
                GameAudio.PlaySfx("invalid");
                return;
            }
            Submit(MatchCommand.Pass());
        }

        //----------------------------------------------The match's events---------------------------------------------------//
        // The events one after the other, each shown in full before the next; when the screen has caught up and the match
        // waits for the opponent, the opponent acts (its move becomes the next events). A LAN battle taken up again
        // (LanSession.Rejoined) shows the events up to where it stands at once, the last of them as usual.
        private async UniTask ShowEvents(CancellationToken ct)
        {
            bool catching = lan != null && lan.Rejoined;
            while (true)
            {
                if (shown >= source.Count)
                {
                    AskOpponent(ct);
                    await UniTask.WaitUntil(() => shown < source.Count || catching && lan.CaughtUp, PlayerLoopTiming.Update, ct);
                    if (shown >= source.Count)
                    {
                        catching = false; // all there was is shown
                        CaughtUp();
                        continue;
                    }
                }
                if (catching && (shown + 1 < source.Count || !lan.CaughtUp))
                {
                    ShowAtOnce(source.Seen(shown), source.All(shown).State);
                    shown++;
                    continue;
                }
                if (catching)
                {
                    catching = false;
                    CaughtUp();
                }
                MatchEvent e = source.Seen(shown);
                showing = source.All(shown);
                shown++;
                presenting++;
                try
                {
                    await Show(e, showing.State, ct);
                }
                finally
                {
                    presenting = Math.Max(0, presenting - 1);
                }
                if (e.Kind == MatchEventKind.Ended)
                    return;
            }
        }

        // An event of a battle taken up again, before where it stands: the battle as it left it, and what it marks for good
        // (a leader spent, a life lost); no animation, notice, sound or wait
        private void ShowAtOnce(MatchEvent e, MatchState all)
        {
            ApplyView(e.State, all);
            if (e.Kind == MatchEventKind.LeaderUsed)
                View(e.Seat).Leader.MarkUsed();
        }

        // The board as the battle stands once caught up: every card in its place, the gems, whose turn; the redraw if it is
        // still on
        private void CaughtUp()
        {
            RefreshBoard();
            View(true).Stats.UIUpdateGemLives(PlayerInfo.Lives);
            View(false).Stats.UIUpdateGemLives(EnemyInfo.Lives);
            roundOpening = false;
            lastTurnWasPlayer = null;
            if (latest != null && latest.Phase == MatchPhase.Redraw && PlayerInfo.CanExchange && mulliganPrompt == null)
                mulliganPrompt = RedrawPicker.ShowPhase(this);
        }

        // The opponent's move, answer or redraw, when the match waits for it (once at a time)
        private void AskOpponent(CancellationToken ct)
        {
            if (opponentActing || match == null || opponent == null || match.IsOver || !match.Awaits(!Me))
                return;
            Acting(ct).Forget();
        }

        private async UniTaskVoid Acting(CancellationToken ct)
        {
            opponentActing = true;
            try
            {
                await opponent.Act(match, !Me, ct);
            }
            catch (OperationCanceledException)
            {
                // The battle went on without it (given up)
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!match.IsOver && match.Awaits(!Me) && !match.Apply(!Me, MatchCommand.Pass()) && !match.Apply(!Me, MatchCommand.Skip()))
                    match.Apply(!Me, MatchCommand.EndRedraw()); // never left waiting on it
            }
            finally
            {
                opponentActing = false;
            }
        }

        private async UniTask Show(MatchEvent e, MatchState all, CancellationToken ct)
        {
            switch (e.Kind)
            {
                case MatchEventKind.ChoiceAsked: await ShowChoice(e, all, ct); break;
                case MatchEventKind.FirstPlayer: await ShowFirstPlayer(e, all, ct); break;
                case MatchEventKind.Redrawn: ShowRedraw(e, all); break;
                case MatchEventKind.RedrawDone: await ShowRedrawDone(e, all, ct); break;
                case MatchEventKind.RoundStarted: await ShowRoundStart(e, all, ct); break;
                case MatchEventKind.TurnStarted: await ShowTurn(e, all, ct); break;
                case MatchEventKind.Passed: ShowPass(e, all); break;
                case MatchEventKind.Played: await ShowPlay(e, all, ct); break;
                case MatchEventKind.Transformed: await ShowTransform(e, all, ct); break;
                case MatchEventKind.LeaderUsed: await ShowLeader(e, all, ct); break;
                case MatchEventKind.Picked: await ShowPicked(e, all, ct); break;
                case MatchEventKind.Discarded: await ShowDiscard(e, all, ct); break;
                case MatchEventKind.Skipped: ShowSkip(e, all); break;
                case MatchEventKind.RoundEnded: await ShowRoundEnd(e, all, ct); break;
                case MatchEventKind.BoardCleared: await ShowClear(e, all, ct); break;
                case MatchEventKind.Reinforced: await ShowReinforcements(e, all, ct); break;
                case MatchEventKind.Ended: ShowEnd(e, all); break;
            }
        }

        // Who goes first: the player's Scoia'tael choice, the opponent's, or a coin toss; then the redraw opens
        private async UniTask ShowFirstPlayer(MatchEvent e, MatchState all, CancellationToken ct)
        {
            ApplyView(e.State, all);
            bool player_first = e.Seat;
            bool opponents_perk = e.Flag && EnemyInfo.Faction == Faction.Scoiatael;
            if (opponents_perk)
                BattleBanner.Show(BattleBanner.FactionEmblem(Faction.Scoiatael), player_first
                    ? "Opponent used the Scoia'tael faction ability to let you go first." : "Opponent used the Scoia'tael faction ability to go first.");
            else
                BattleBanner.Show(player_first ? BattleBanner.YouFirst : BattleBanner.OpponentFirst, player_first ? "You will go first" : "Your opponent will go first");
            await While(() => BattleBanner.Busy, ct);

            battleState = BattleState.Start; // the redraw (an opponent choosing first had it look like its turn)
            if (PlayerInfo.CanExchange)
                mulliganPrompt = RedrawPicker.ShowPhase(this);
        }

        // A side done with its redraw. The player's, while the opponent still redraws (a LAN battle's other player takes
        // its time; the AI never does): the redraw closes for the board once its last card has landed, and the screen
        // waits as on the opponent's turn
        private async UniTask ShowRedrawDone(MatchEvent e, MatchState all, CancellationToken ct)
        {
            ApplyView(e.State, all);
            if (e.Seat && e.Why == PassReason.TimedOut)
                UIOverlay.Toast("Out of time: you keep your hand", 3f);
            if (!e.Seat || !EnemyInfo.CanExchange)
                return;
            await While(() => RedrawPicker.IsAnimating, ct);
            if (mulliganPrompt != null)
                Destroy(mulliganPrompt);
            mulliganPrompt = null;
            OpponentsMove("Your opponent is still redrawing");
        }

        // The screen waits for the opponent outside its turns (its redraw, its choice of who goes first): as on its turn,
        // the "Opponent's turn" notice says what it is doing and its side's markers light up
        private void OpponentsMove(string doing)
        {
            battleState = BattleState.EnemyTurn;
            BattleBanner.Show(BattleBanner.OpponentsTurn, doing);
        }

        // Redraw: the deck's top card took the card's place in the hand, the card went to the bottom of the deck
        private void ShowRedraw(MatchEvent e, MatchState all)
        {
            BattleLog.Redraw(this, e.Seat, showing.Card, showing.Other);
            ApplyView(e.State, all);
            GameAudio.PlaySfx("card_exchange");
            SetDeckObject(Info(e.Seat), e.Seat);
            SetHandObject(Info(e.Seat).HandList, e.Seat);
        }

        // A round opens (the first once both sides redrew and the last redrawn card landed, a later one once the last
        // round's notices ended): "Round Start"
        private async UniTask ShowRoundStart(MatchEvent e, MatchState all, CancellationToken ct)
        {
            await While(() => RedrawPicker.IsAnimating || BattleBanner.Busy, ct);
            if (mulliganPrompt != null)
                Destroy(mulliganPrompt);
            ApplyView(e.State, all);

            BattleBanner.Show(BattleBanner.RoundStart, "Round Start");
            await While(() => BattleBanner.Busy, ct);
            roundOpening = true;
            lastTurnWasPlayer = null;
            BattleLog.RoundStart(e.Seat);
        }

        private async UniTask ShowTurn(MatchEvent e, MatchState all, CancellationToken ct)
        {
            TurnOver();
            ApplyView(e.State, all);
            if (!e.Seat)
            {
                battleState = BattleState.EnemyTurn;
                bool told = AnnounceTurn(false);
                BattleLog.TurnStart(this, false);
                if (!told)
                    GameAudio.PlaySfx("turn_enemy");
                return; // the opponent's move comes next (AskOpponent)
            }
            bool announced = AnnounceTurn(true);
            await Wait(0.3f, ct);
            await While(() => BattleBanner.Busy, ct);
            BattleLog.TurnStart(this, true);
            if (!announced)
                GameAudio.PlaySfx("turn_player");
            battleState = BattleState.PlayerTurn;
        }

        // What the turn before left behind on screen: the showcase, a pending step, the hider
        private void TurnOver()
        {
            CardShowcase.Clear();
            pendingAction = PendingAction.None;
            SetHider(false);
        }

        // The Witcher 3's turn notices, the turn's chime with them: each change of side. Not while the other side has
        // passed: its pass was announced already and the turns no longer alternate. Whether one is shown (else the chime
        // is the caller's).
        private bool AnnounceTurn(bool is_player)
        {
            roundOpening = false;
            bool shown = lastTurnWasPlayer != is_player && !Opponent(is_player).hasPassed;
            if (shown)
                BattleBanner.Show(is_player ? BattleBanner.YourTurn : BattleBanner.OpponentsTurn, is_player ? "Your turn!" : "Opponent's turn",
                    BattleBanner.Turn, is_player ? "turn_player" : "turn_enemy");
            lastTurnWasPlayer = is_player;
            return shown;
        }

        private static string Why(PassReason why)
        {
            return why == PassReason.EmptyHand ? "empty_hand" : why == PassReason.NothingToPlay ? "nothing_to_play" : "chose";
        }

        private void ShowPass(MatchEvent e, MatchState all)
        {
            bool is_player = e.Seat;
            BattleLog.Passed(this, is_player, Why(e.Why));
            ApplyView(e.State, all);
            // A pass that ends the round is followed by the round's result instead; at the opening of a round the
            // other side's "Round N" notice says it all. Its sound goes with its notice (which may wait behind another).
            if (!Opponent(is_player).hasPassed && !roundOpening)
                BattleBanner.Show(BattleBanner.Passed, is_player ? "Turn passed" : "Your opponent has passed", BattleBanner.Turn, "pass");
            else
                GameAudio.PlaySfx("pass");
            if (is_player)
            {
                DeselectCard();
                if (e.Why == PassReason.Chose)
                    TurnOver();
            }
        }

        // A card played (from the hand; a medic's unit from the discard pile; a weather leader's card from the deck): the
        // opponent's comes in from above the board to the showcase and stays a moment there; then it lands, and what it
        // caused follows (BoardPresenter.Play). The opponent's revived unit flies from its discard pile instead.
        private async UniTask ShowPlay(MatchEvent e, MatchState all, CancellationToken ct)
        {
            bool is_player = e.Seat;
            bool decoy = e.Other >= 0;
            Card card = Db.Get(e.Card);
            if (!is_player && e.From == PlaySource.Hand)
                await ShowcaseOpponent(card, ct);

            BattleLog.BeforeChange(this);
            if (is_player)
                selectedCard = null;
            pendingAction = PendingAction.None;
            CardShowcase.Clear();
            PlayCardAudio(card);
            board.BeginChange();
            ApplyView(e.State, all);
            RefreshBoard();
            BattleLog.Played(this, decoy ? "decoy_played" : "card_played", is_player, e.Card, e.Row);
            SetHider(false);
            BoardChange change = board.EndChange();
            int landing = !is_player && e.From == PlaySource.Discard ? -1 : e.Card;
            await ShowChange(change, show => presenter.Play(change, landing, is_player, e.Flag, show), ct);
        }

        // The card the opponent plays (or its leader) comes in from above the board to the showcase and stays a moment
        private async UniTask ShowcaseOpponent(Card card, CancellationToken ct)
        {
            try
            {
                await CardShowcase.Opponent(card, board.EntryRect(CardShowcase.ScreenSize()), ct);
            }
            catch (OperationCanceledException)
            {
                CardShowcase.Clear();
                throw;
            }
        }

        // Berserkers that met a Mardroeme (the special card, or Ermion) turn into their bears once the play that brought
        // them together has shown: the bears in their places, to the Mardroeme's growl, the Berserkers to the discard pile
        private async UniTask ShowTransform(MatchEvent e, MatchState all, CancellationToken ct)
        {
            board.BeginChange();
            ApplyView(e.State, all);
            GameLog.Event(LogCategory.Battle, "berserkers_transformed");
            RefreshBoard();
            BoardChange change = board.EndChange();
            GameAudio.PlaySfx("ability_mardroeme");
            await ShowChange(change, show => presenter.Transform(change, show), ct);
        }

        // A card taken into a hand (a leader's pick): it flies there from its pile
        private async UniTask ShowPicked(MatchEvent e, MatchState all, CancellationToken ct)
        {
            board.BeginChange();
            ApplyView(e.State, all);
            if (e.Card != CardIds.Hidden)
                GameLog.Event(LogCategory.Battle, "picked", "side", e.Seat ? "player" : "enemy", "card", Db.Get(e.Card).name, "from", e.From);
            RefreshBoard();
            BoardChange change = board.EndChange();
            await ShowChange(change, show => presenter.Play(change, -1, e.Seat, false, show), ct);
        }

        // Eredin, Destroyer of Worlds: a card of the hand to the discard pile
        private async UniTask ShowDiscard(MatchEvent e, MatchState all, CancellationToken ct)
        {
            GameAudio.PlaySfx("card_discard");
            GameLog.Event(LogCategory.Battle, "card_discarded", "side", e.Seat ? "player" : "enemy", "card", Db.Get(e.Card).name);
            board.BeginChange();
            ApplyView(e.State, all);
            RefreshBoard(); // a Cow discarded: its Bovine Defense Force comes next round, said now
            BoardChange change = board.EndChange();
            await ShowChange(change, show => presenter.Play(change, -1, e.Seat, false, show), ct);
        }

        // A choice given up, or a look ended: the picker goes, the turn goes on
        private void ShowSkip(MatchEvent e, MatchState all)
        {
            ApplyView(e.State, all);
            if (!e.Seat)
                return;
            Panel.SetActive(false);
            DeselectCard();
            RefreshBoard();
        }

        //----------------------------------------------ROUNDS---------------------------------------------------//
        // The result over the board as it ended (a tie Nilfgaard wins is announced first), and as it shows, to its sound,
        // the gem of each side that lost a life breaks (The Witcher 3)
        private async UniTask ShowRoundEnd(MatchEvent e, MatchState all, CancellationToken ct)
        {
            battleState = BattleState.RoundOver;
            TurnOver();
            int player_lives = PlayerInfo.Lives, enemy_lives = EnemyInfo.Lives;
            ApplyView(e.State, all);
            RecalculateStrengths();
            BattleLog.RoundEnd(this, e.Winner, e.PlayerTotal, e.EnemyTotal);
            bool[] lost = { PlayerInfo.Lives < player_lives, EnemyInfo.Lives < enemy_lives }; // the player's, the opponent's

            bool timed_out = e.Why == PassReason.TimedOut; // a LAN turn's clock: the side out of time lost the round
            if (timed_out && e.Seat)
                DropInput(); // what the player was doing goes with the round
            if (e.Flag)
                BattleBanner.Show(BattleBanner.FactionEmblem(Faction.Nilfgaard), "Nilfgaardian faction ability triggered: Nilfgaard wins the tie.");
            string sound = RoundOutcomeSound(e.Winner);
            int result;
            if (timed_out)
                result = BattleBanner.Show(e.Seat ? BattleBanner.RoundLost : BattleBanner.RoundWon,
                    e.Seat ? "Out of time: your opponent wins the round" : "Your opponent ran out of time: you win the round", BattleBanner.Long, sound);
            else if (e.Winner == RoundWinner.Player)
                result = BattleBanner.Show(BattleBanner.RoundWon, "You won the round!", BattleBanner.Long, sound);
            else if (e.Winner == RoundWinner.Enemy)
                result = BattleBanner.Show(BattleBanner.RoundLost, "Your opponent won the round", BattleBanner.Long, sound);
            else
                result = BattleBanner.Show(BattleBanner.RoundDrawn, "The round ended in a draw", BattleBanner.Long, sound);

            await While(() => !BattleBanner.Begun(result), ct);
            List<UniTask> shatters = new List<UniTask>();
            foreach (bool is_player in new[] { true, false })
                if (lost[is_player ? 0 : 1])
                    shatters.Add(CardEffects.Shatter(View(is_player).Stats.LostGem(Info(is_player).Lives), View(is_player).Stats.greyRuby, ct));
            await UniTask.WhenAll(shatters);
            View(true).Stats.UIUpdateGemLives(PlayerInfo.Lives);
            View(false).Stats.UIUpdateGemLives(EnemyInfo.Lives);
            await While(() => BattleBanner.Busy, ct);
        }

        private static string RoundOutcomeSound(RoundWinner winner)
        {
            switch (winner)
            {
                case RoundWinner.Player: return "round_win";
                case RoundWinner.Enemy: return "round_lose";
                default: return "round_end";
            }
        }

        // The board clears as in The Witcher 3: every card flies to its discard pile. Monsters keep a random unit; leader
        // horns vanish; weather goes back to whoever played it; the Northern Realms' winner draws a card.
        private async UniTask ShowClear(MatchEvent e, MatchState all, CancellationToken ct)
        {
            DeselectCard();
            pendingAction = PendingAction.None;

            board.BeginChange();
            ApplyView(e.State, all);
            foreach (bool is_player in new[] { true, false })
            {
                int kept = is_player ? e.PlayerKept : e.EnemyKept;
                if (kept == -1)
                    continue;
                GameLog.Event(LogCategory.Battle, "monsters_kept", "side", is_player ? "player" : "enemy", "card", Db.Get(kept).name);
                BattleBanner.Show(BattleBanner.FactionEmblem(Faction.Monsters), "Monsters faction ability triggered: one randomly chosen Monster Unit Card stays on the battlefield.");
            }
            foreach (bool is_player in new[] { true, false })
            {
                if (!(is_player ? e.PlayerDrew : e.EnemyDrew))
                    continue;
                GameLog.Event(LogCategory.Battle, "northern_realms_draw", "side", is_player ? "player" : "enemy");
                BattleBanner.Show(BattleBanner.FactionEmblem(Faction.NorthernRealms), "Northern Realms faction ability triggered: North draws an additional card.");
            }
            RefreshBoard(round_clear: true);
            BoardChange change = board.EndChange();
            await ShowChange(change, show => presenter.Clear(change, show), ct);
        }

        // Skellige's perk, as the third round opens on a cleared board: its notice, then two units of its discard pile
        // fly back onto the battlefield
        private async UniTask ShowReinforcements(MatchEvent e, MatchState all, CancellationToken ct)
        {
            board.BeginChange();
            ApplyView(e.State, all);
            foreach (bool is_player in new[] { true, false })
            {
                List<int> placed = (is_player ? e.PlayerCards : e.EnemyCards) ?? new List<int>();
                if (placed.Count == 0)
                    continue;
                GameLog.Event(LogCategory.Battle, "skellige_reinforcements", "side", is_player ? "player" : "enemy", "cards", string.Join(", ", placed.Select(id => Db.Get(id).name)));
                BattleBanner.Show(BattleBanner.FactionEmblem(Faction.Skellige), "Skellige faction ability triggered: " + (placed.Count == 1 ? "a unit" : "two units")
                    + " from " + (is_player ? "your" : "your opponent's") + " discard pile " + (placed.Count == 1 ? "returns" : "return") + " to the battlefield.");
            }
            RefreshBoard();
            BoardChange change = board.EndChange();
            await ShowChange(change, async show =>
            {
                await While(() => BattleBanner.Busy, show);
                await presenter.Clear(change, show);
            }, ct);
        }

        private void ShowEnd(MatchEvent e, MatchState all)
        {
            ApplyView(e.State, all);
            // Given up by the other side; or for this one, by the host while this guest was away (its own giving up does
            // not come here)
            ConcludeBattle(e.Winner == RoundWinner.Player ? BattleState.Won : e.Winner == RoundWinner.Enemy ? BattleState.Lost : BattleState.Draw,
                false, e.Flag && !e.Seat, e.Flag && e.Seat);
        }

        // A move the LAN host refused after all: the battle shows as it was (a card played back in the hand)
        private void ShowRefused()
        {
            if (IsBattleOver)
                return; // given up meanwhile: the end stays
            if (lan == null || lan.Linked)
                GameAudio.PlaySfx("invalid"); // not for a move lost with the link (made again once it is back)
            if (latest == null)
                return;
            ApplyView(latest, logged);
            RefreshBoard();
        }

        //----------------------------------------------A LAN battle's link---------------------------------------------------//
        // The other device away: the board waits under a notice (LanNotice) until it is back; after a minute the host may
        // end the battle (won: its guest left it) and the guest leave it. A battle that cannot go on (the host lost it)
        // says so, with the way back to the main menu.
        private void LanChanged()
        {
            if (this == null || lan == null || IsBattleOver)
                return;
            if (lan.State == LanSession.Stage.Closed)
            {
                CloseAway();
                if (!lan.Ended) // else its end shows (the other gave up, then left)
                    away = LanNotice.Show(lan, true, () => LoadingScreen.Load(SceneNames.Menu));
                return;
            }
            if (lan.Linked)
                CloseAway();
            else if (away == null)
                away = LanNotice.Show(lan, false, LeaveAway);
        }

        private void LeaveAway()
        {
            if (lan.Hosting)
                lan.Match.Apply(false, MatchCommand.GiveUp()); // the guest's seat: it left the battle
            else
            {
                // Out of the session too: no joining again behind the end screen (the host, its minute up, ends it)
                lan.Changed -= LanChanged;
                lan.Leave();
                GiveUp();
            }
            CloseAway();
        }

        private void CloseAway()
        {
            if (away != null)
                Destroy(away.gameObject);
            away = null;
        }

        public bool IsBattleOver
        {
            get
            {
                return battleState == BattleState.Won || battleState == BattleState.Lost || battleState == BattleState.Draw
                    || PlayerInfo.Lives == 0 || EnemyInfo.Lives == 0; // decided: the last round's result is showing
            }
        }

        // Giving up (a hand laid on the player's deck, DeckGiveUp; the pause menu's Leave battle): everything in progress
        // stops and the battle is lost. leaving: the main menu loads next, so no end screen and no defeat sound
        public void GiveUp(bool leaving = false)
        {
            if (IsBattleOver)
                return;
            flow.Cancel();
            flow.Dispose();
            flow = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            EndPresentations();
            BattleBanner.Clear();
            if (mulliganPrompt != null)
                Destroy(mulliganPrompt);
            DropInput();
            source.Submit(MatchCommand.GiveUp()); // a LAN guest away: the host finds out on its own
            shown = source.Count;
            if (leaving)
            {
                BattleLog.End(BattleState.Lost, true);
                battleState = BattleState.Lost;
                return;
            }
            ConcludeBattle(BattleState.Lost, true);
        }

        // What the player was in the middle of goes (a pile looked through, a card taken or picked, the leader lifted, a
        // choice open): the battle moved on without it (given up, out of time)
        private void DropInput()
        {
            DiscardViewer.Close();
            TurnPicker.SetActive(false);
            Panel.SetActive(false);
            pendingAction = PendingAction.None;
            DeselectCard();
            SetHider(false);
        }

        // gave_up: the player gave up; opponent_left: the opponent did (a LAN battle's other player); ended_away: the LAN
        // host ended it while this guest was away
        private void ConcludeBattle(BattleState outcome, bool gave_up = false, bool opponent_left = false, bool ended_away = false)
        {
            BattleLog.End(outcome, gave_up);
            battleState = outcome;
            BoardZoom.ZoomOut(); // the end screen over the whole board
            CloseAway();
            BattleBanner.Clear(); // a notice under way (the other's turn, as it gave up) does not show over the end
            CardShowcase.Clear();
            CardMotion.ClearLayer();
            PostGame.SetActive(true);
            PostGame.GetComponent<EndScreen>().Show(outcome, PlayerInfo, EnemyInfo, gave_up, opponent_left, !ReferenceEquals(lan, null), ended_away);
            if (outcome == BattleState.Won)
                GameAudio.PlaySfx("victory");
            else if (outcome == BattleState.Lost)
                GameAudio.PlaySfx("defeat");
        }

        //----------------------------------------------PLAYING CARDS---------------------------------------------------//
        // The board row of a row's object, by its name (Close, Range or Siege); Row.Special for an object of no row
        private static Row RowNamed(string name)
        {
            Row row;
            return Enum.TryParse(name, out row) && row.IsUnitRow() ? row : Row.Special;
        }

        // Places a card on a field: from the hand, or the card picked from a pile (a medic's unit, a weather leader's
        // card). Its object leaves the hand once the match takes the play (the showcase shows it meanwhile): the refresh
        // reuses hand objects, and with two copies in hand it could otherwise keep this very one for the copy that stays.
        public void DirectlyPlaceCard(GameObject card_go, GameObject field_go, bool is_player_turn)
        {
            if (!is_player_turn)
                return;
            Card card_stats = card_go.GetComponent<CardFace>().Card;
            Row field_row = field_go == WeatherField ? Row.Weather : RowNamed(field_go.transform.parent.name);
            int hand_index = HandIndexOf(card_go);
            MatchCommand command = pendingAction == PendingAction.PlacePickedCard
                ? MatchCommand.Choose(card_stats._id, field_row) : MatchCommand.Play(card_stats._id, field_row, hand_index);
            if (!Submit(command))
                return;
            card_go.GetComponent<CardHover>().DestroyEffect();
            card_go.transform.SetParent(null, false);
            Destroy(card_go);
        }

        // A card object's place in its side's hand (which shows the hand list in order), or -1 when it is not in a hand:
        // with two copies of a card in hand, the one that was clicked
        public int HandIndexOf(GameObject card_go)
        {
            Transform hand = card_go.transform.parent;
            if (hand == null || (hand != View(true).Hand.transform && hand != View(false).Hand.transform))
                return -1;
            int index = 0;
            foreach (Transform child in hand)
            {
                if (child == card_go.transform)
                    return index;
                if (child.GetComponent<CardFace>() != null)
                    index++;
            }
            return -1;
        }

        // Shows a change of the board (BoardPresenter); meanwhile the player cannot act. Never left waiting: a
        // presentation that fails or runs past the limit (game time: the pause stops it) is cut short and the board shows
        // the result at once. Giving up cancels it with the battle's flow.
        private async UniTask ShowChange(BoardChange change, Func<CancellationToken, UniTask> show, CancellationToken ct)
        {
            presenting++;
            presented.Add(change);
            bool finished = false;
            try
            {
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    try
                    {
                        finished = await UniTask.WhenAny(show(limit.Token), Wait(PresentationLimit, limit.Token)) == 0;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException) || !ct.IsCancellationRequested)
                    {
                        if (ct.IsCancellationRequested)
                            throw new OperationCanceledException(ct); // the battle went on without it: an effect's part may have gone first
                        Debug.LogException(e);
                    }
                    finally
                    {
                        limit.Cancel(); // the presentation, if the limit came first (or the limit, if it ended)
                    }
                }
                if (!finished)
                    Debug.LogError("A play's presentation did not end, the board shows the result at once");
                change.Flush();
                presented.Remove(change);
            }
            finally
            {
                presenting = Math.Max(0, presenting - 1);
            }
        }

        private const float PresentationLimit = 12f;

        // Waits while the condition holds; not at all when it does not (as the coroutines' "while (...) yield return null")
        private static UniTask While(Func<bool> condition, CancellationToken ct)
        {
            return condition() ? UniTask.WaitWhile(condition, PlayerLoopTiming.Update, ct) : UniTask.CompletedTask;
        }

        // A wait in game time (the pause stops it, the game speed and the autoplay's speed apply)
        internal static UniTask Wait(float seconds, CancellationToken ct)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);
        }

        // Plays cut short (giving up): every card shows in its place at once
        private void EndPresentations()
        {
            foreach (BoardChange change in presented)
                change.Flush();
            presented.Clear();
            presenting = 0;
            CardShowcase.Clear();
            CardMotion.ClearLayer();
        }

        // A card chosen from a pile (medic, weather from the deck) comes from that pile to the showcase
        public void ShowcasePicked(int card_id, PickSource field, bool is_player)
        {
            CardShowcase.Pick(Db.Get(card_id), field == PickSource.Deck ? board.DeckRect(is_player) : board.PileRect(is_player));
        }

        // The board as this screen shows it, for the rules' checks (a leader that would do nothing, the places a card
        // can go)
        private Board CurrentBoard()
        {
            return new Board
            {
                Player = PlayerInfo,
                Enemy = EnemyInfo,
                Weather = weatherList,
                DoubleSpies = doubleSpies,
                RandomMedic = randomMedic,
                Db = Db,
            };
        }

        public bool TryPlaySelectedCardOnBoard()
        {
            if (!IsSelectedCardPlayable())
                return false;

            Card selectedStats = SelectedStats();
            if (selectedStats.faction != Faction.Special)
                return false;

            if (selectedStats.row != Row.Weather && selectedStats.ability != Ability.Scorch)
                return false;

            DirectlyPlaceCard(selectedCard, WeatherField, true);
            return true;
        }

        public bool TryPlaySelectedDecoyOnCard(GameObject targetCard)
        {
            if (!IsSelectedCardPlayable())
                return false;

            Card selectedStats = SelectedStats();
            if (selectedStats.ability != Ability.Decoy)
                return false;

            Card targetStats = targetCard.GetComponent<CardFace>().Card;
            Row row = RowNamed(targetCard.transform.parent.parent.name);
            bool valid_target = targetCard.CompareTag("Player") && PlayRules.IsDecoyTarget(targetStats) && Rows.IsUnitRow(row);
            if (!valid_target)
            {
                GameAudio.PlaySfx("invalid");
                UIOverlay.Toast(CardIds.IsSummoned(targetStats._id) && targetCard.CompareTag("Player")
                    ? targetStats.DisplayName + " only exists on the board: a decoy can't take it back"
                    : "Decoy can only swap a non-hero unit on your side");
                return true;
            }

            // Out of the hand once the match takes it, as in DirectlyPlaceCard (a second decoy in hand keeps its own object)
            int hand_index = HandIndexOf(selectedCard);
            if (!Submit(MatchCommand.Decoy(selectedStats._id, targetStats._id, row, hand_index)))
                return true;
            selectedCard.GetComponent<CardHover>().DestroyEffect();
            selectedCard.transform.SetParent(null, false);
            Destroy(selectedCard);
            selectedCard = null;
            return true;
        }

        // Escape (the back button on Android) puts a selected card (or the chosen leader) back first, as in The Witcher 3;
        // otherwise it pauses
        // Escape (Android's Back) steps back one level: a card or leader put back; a picker's choice given up, as its × does
        // (asked first where something is lost), or a look closed; the whole board again; the end screen left for the main
        // menu; otherwise the pause menu (the redraw, who goes first and a LAN notice have nothing to step back to)
        private void OnEscape()
        {
            BattlePickers pickers = Panel != null && Panel.activeInHierarchy ? Panel.GetComponent<BattlePickers>() : null;
            if ((selectedCard != null || LeaderSelected) && IsPlayerActionAllowed)
                PutBackSelectedCard();
            else if (pickers != null && pickers.Picker != null && pendingAction == PendingAction.PanelChoice)
                pickers.Picker.BandButton.onClick.Invoke();
            else if (BoardZoom.Zoomed)
                BoardZoom.ZoomOut(); // the whole board first, then the menu
            else if (IsBattleOver && PostGame != null && PostGame.activeInHierarchy)
                PostGame.GetComponent<EndScreen>().ToMainMenu();
            else
                PauseMenu.Open();
        }

        public void PutBackSelectedCard(bool sound = true)
        {
            if (sound)
                GameAudio.PlaySfx("card_select");
            DeselectCard();
        }

        // A card is selected and can be placed now (from the hand, or picked from a pile by a medic or a leader)
        public bool IsSelectedCardPlayable()
        {
            return battleState == BattleState.PlayerTurn && selectedCard != null && !IsPresenting
                && (pendingAction == PendingAction.None || pendingAction == PendingAction.PlacePickedCard);
        }

        private Card SelectedStats()
        {
            return selectedCard.GetComponent<CardFace>().Card;
        }

        // The selected card (or the chosen leader) goes back to its place, gliding there from the showcase (or leaving it
        // at once: its play takes over)
        private void DeselectCard(bool glide = true)
        {
            ReleaseLeader();
            if (selectedCard != null)
            {
                CardHover hover = selectedCard.GetComponent<CardHover>();
                hover.isHoverable = true;
                hover.TranslateDown();
                hover.DestroyEffect();
            }
            if (glide)
                CardShowcase.Deselect();
            else
                CardShowcase.Clear();
            selectedCard = null;
            LightOffField(true);
            LightOffField(false);
        }

        private void PlayCardAudio(Card card)
        {
            if (card == null)
                return;

            if (card.IsSpecial)
            {
                if (card.row == Row.Weather)
                {
                    switch (card._idstr)
                    {
                        case "biting_frost": GameAudio.PlaySfx("ability_weather_frost"); break;
                        case "impenetrable_fog": GameAudio.PlaySfx("ability_weather_fog"); break;
                        case "torrential_rain":
                        case "skellige_storm": GameAudio.PlaySfx("ability_weather_rain"); break;
                        case "clear_weather": GameAudio.PlaySfx("ability_weather_clear"); break;
                        default: GameAudio.PlaySfx("ability_weather"); break;
                    }
                }
                else if (card.ability == Ability.Scorch)
                    GameAudio.PlaySfx("ability_scorch");
                else if (card.ability == Ability.Decoy)
                    GameAudio.PlaySfx("ability_decoy");
                else if (card.ability == Ability.CommanderHorn)
                    GameAudio.PlaySfx("ability_horn");
                else
                    GameAudio.PlaySfx("card_play_special");
                return;
            }

            switch (card.ability)
            {
                case Ability.Spy: GameAudio.PlaySfx("ability_spy"); return;
                case Ability.Medic: GameAudio.PlaySfx("ability_medic"); return;
                case Ability.Muster: GameAudio.PlaySfx("ability_muster"); return;
                case Ability.UnitHorn: GameAudio.PlaySfx("ability_horn"); return;
                case Ability.MoraleBoost: GameAudio.PlaySfx("ability_morale"); return;
                case Ability.TightBond: GameAudio.PlaySfx("ability_tight_bond"); return;
            }
            if (Abilities.IsRowScorch(card.ability) || card.ability == Ability.Scorch) // the Clan Dimun Pirate's too
                GameAudio.PlaySfx("ability_scorch");
            else
                GameAudio.PlaySfx(card.unique ? "card_play_hero" : "card_play_unit");
        }

        //----------------------------------------------Board (drawn by BoardView)---------------------------------------------------//
        private void RecalculateStrengths()
        {
            StrengthCalculator.Recalculate(PlayerInfo, weatherList, doubleSpies, Db);
            StrengthCalculator.Recalculate(EnemyInfo, weatherList, doubleSpies, Db);
        }

        // Recalculates the strengths, then shows both sides from the lists (round_clear: the board cleared for a new round)
        private void RefreshBoard(bool round_clear = false)
        {
            RecalculateStrengths();
            board.Refresh(round_clear);
            AnnounceCows(round_clear);
        }

        // An Avenger (a Cow, Kambi) gone to the discard pile during a round, said at once (AbilityRules.AvengerOf): killed
        // (scorched), its avenger takes the field now, or the player wonders where it came from; discarded (Destroyer of
        // Worlds), it is held until the board clears for the next round, never coming if the battle ends first, or the
        // player looks for it in vain. (At the end of a round it arrives in the same clearing: nothing to say.)
        private readonly Dictionary<int, int[]> avengersDiscarded = new Dictionary<int, int[]> { { CardIds.Cow, new int[2] }, { CardIds.Kambi, new int[2] } };
        private readonly Dictionary<int, int[]> summonsPending = new Dictionary<int, int[]> { { CardIds.Cow, new int[2] }, { CardIds.Kambi, new int[2] } };

        private void AnnounceCows(bool round_clear)
        {
            foreach (bool is_player in new[] { true, false })
            {
                BattleSide info = Info(is_player);
                int index = is_player ? 0 : 1;
                foreach (int caller in avengersDiscarded.Keys)
                {
                    int avenger = AbilityRules.AvengerOf(caller);
                    int discarded = info.DiscardList.Count(id => id == caller);
                    int pending = info.PendingSummons.Count(id => id == avenger);
                    if (!round_clear && discarded > avengersDiscarded[caller][index])
                    {
                        bool killed = pending <= summonsPending[caller][index];
                        GameLog.Event(LogCategory.Battle, killed ? "avenged" : "summon_pending", "side", is_player ? "player" : "enemy", "card", Db.Get(caller).name);
                        UIOverlay.Toast(AvengerNotice(caller == CardIds.Kambi, is_player, killed), 4f);
                    }
                    avengersDiscarded[caller][index] = discarded;
                    summonsPending[caller][index] = pending;
                }
            }
        }

        private static string AvengerNotice(bool kambi, bool is_player, bool killed)
        {
            if (kambi)
            {
                if (killed)
                    return is_player ? "Kambi was killed: Hemdall takes the field to avenge him" : "You killed the opponent's Kambi: their Hemdall takes the field to avenge him";
                return is_player ? "Kambi is discarded: Hemdall joins your close combat row next round, if the battle goes on"
                    : "The opponent's Kambi is discarded: their Hemdall comes next round, if the battle goes on";
            }
            if (killed)
                return is_player ? "Your Cow was killed: the Bovine Defense Force takes the field to avenge it"
                    : "You killed the opponent's Cow: their Bovine Defense Force takes the field to avenge it";
            return is_player ? "Your Cow is discarded: the Bovine Defense Force joins your close combat row next round, if the battle goes on"
                : "The opponent's Cow is discarded: their Bovine Defense Force comes next round, if the battle goes on";
        }

        private void SetDeckObject(BattleSide info, bool is_player_deck) { board.SetDeck(info, is_player_deck); }
        public void SetHandObject(List<int> hand_list, bool is_player_hand) { board.SetHand(hand_list, is_player_hand); }
        private void SetDiscardObject(List<int> discard_list, bool is_player) { board.SetDiscard(discard_list, is_player); }
        private void CreateCardObjects(GameObject my_object, List<int> card_list, bool isSelectable, bool is_player_card) { board.CreateCardObjects(my_object, card_list, isSelectable, is_player_card); }
        public GameObject CreateCardObject(Card card, bool is_player_card) { return board.CreateCardObject(card, is_player_card); }
        public void HighlightField(Card card_stats, bool is_player_turn) { board.Highlight(card_stats, is_player_turn); }
        public void LightOffField(bool is_player_turn) { board.Lightoff(is_player_turn); }
        public void SetHider(bool hidden) { board.SetHider(hidden); }

        // For the effects benchmark (EffectsBenchmark): the board; for it and the editor's previews (WeatherPreview), showing
        // the board after a change of the lists
        internal BoardView Board { get { return board; } }
        public void ShowBoard() { RefreshBoard(); }

        //----------------------------------------------Queries---------------------------------------------------//
        public Card GetCard(int card_id)
        {
            return Db.Get(card_id);
        }
    }
}
