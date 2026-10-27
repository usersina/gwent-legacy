using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gwent.Battle;
using Gwent.Core;
using Gwent.DeckBuilding;
using Gwent.Menus;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Gwent.EditorTools
{
    // End-to-end smoke test: plays whole battles in the real Game scene with a random "player" against the AI.
    // The first battles start with the redraw played through real input (a virtual mouse and keyboard at human speed),
    // and the player's first turns check the same way card selection, the pause menu, passing, the keyboard controls,
    // the touch controls (simulated), the game speed setting and the card picker's keys.
    // Every leader of every faction gets a generated deck. Fails on exceptions/errors in the log, on a game that
    // stops progressing (softlock), and on cards appearing or disappearing (card conservation).
    // Then the deck builder opens on a test Monsters deck: real clicks add and remove cards (and are refused past the
    // copy and special limits), change the leader, switch factions and pick the deck in the dropdown; then its API
    // (duplicate names, rename, battle deck, corrupt files, delete). The main menu follows (settings, How to play), then
    // Play, the opponent picker and the battle with the deck the builder chose, and the fallback for an invalid deck.
    // It only touches its own "Autoplay ..." decks, and restores the battle deck, the opponent and the settings it changes.
    //
    //  Editor:  menu Gwent > Run Autoplay Smoke Test (Ctrl+Shift+Alt+G)
    //  Batch:   Unity -batchmode -projectPath <path> -executeMethod Gwent.EditorTools.GwentAutoplay.RunBatch
    //                 [-autoplayMatches 21] [-autoplaySeed 1] [-autoplayReport <file>]
    public static class GwentAutoplay
    {
        // The run's failures: each one also goes into the game log, where it sits among the events that led to it
        private class LoggedFailures : List<string>
        {
            public new void Add(string failure)
            {
                base.Add(failure);
                GameLog.Event(LogCategory.Game, "autoplay_failure", "what", failure);
            }
        }

        private const string ActiveKey = "GwentAutoplay.Active";
        private const string BatchKey = "GwentAutoplay.Batch";
        private const string MatchesKey = "GwentAutoplay.Matches";
        private const string SeedKey = "GwentAutoplay.Seed";
        private const string ReportKey = "GwentAutoplay.Report";
        private const float SoftlockSeconds = 20f;
        private const float TimeScale = 20f;

        [MenuItem("Gwent/Run Autoplay Smoke Test %#&g")] // Ctrl+Shift+Alt+G
        public static void RunFromMenu()
        {
            Begin(LeaderIds.ByFaction.Values.Sum(leaders => leaders.Length) + 1, 1, false, Path.Combine("Logs", "autoplay-report.txt"));
        }

        public static void RunBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int matches = IntArg(args, "-autoplayMatches", LeaderIds.ByFaction.Values.Sum(leaders => leaders.Length) + 1);
            int seed = IntArg(args, "-autoplaySeed", 1);
            string report = StringArg(args, "-autoplayReport", Path.Combine("Logs", "autoplay-report.txt"));
            Begin(matches, seed, true, report);
        }

        private static void Begin(int matches, int seed, bool batch, string report)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Stop Play mode before running the autoplay smoke test.");
                return;
            }
            // Opening the Game scene would otherwise drop unsaved changes of the current scene
            if (!batch && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetInt(MatchesKey, matches);
            SessionState.SetInt(SeedKey, seed);
            SessionState.SetString(ReportKey, report);
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static Runner runner;

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Play mode stopped in the middle of a run: report what was done instead of staying hooked to the editor
            if (state == PlayModeStateChange.ExitingPlayMode && runner != null && runner.Running)
            {
                runner.Abort("play mode was stopped before the run ended");
                return;
            }
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(ActiveKey, false))
                return;
            SessionState.SetBool(ActiveKey, false);
            runner = new Runner(SessionState.GetInt(MatchesKey, 21), SessionState.GetInt(SeedKey, 1),
                SessionState.GetBool(BatchKey, false), SessionState.GetString(ReportKey, "Logs/autoplay-report.txt"));
            runner.Start();
        }

        // The notice showing at the top of the screen, if any
        private static string ToastText()
        {
            UnityEngine.UIElements.VisualElement toast = UIScreens.Find("Toast");
            UnityEngine.UIElements.Label label = toast != null ? UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(toast, "Message") : null;
            return label != null ? label.text : "";
        }

        private static int IntArg(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            int value;
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out value) ? value : fallback;
        }

        private static string StringArg(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        //----------------------------------------The runner (ticked by EditorApplication.update)----------------------------------------//
        private class Runner
        {
            private readonly int totalMatches;
            private readonly int seed;
            private readonly bool batch;
            private readonly string reportPath;
            private readonly System.Random rnd;
            private readonly LoggedFailures failures = new LoggedFailures();
            private readonly List<string> results = new List<string>();
            private readonly List<string> errors = new List<string>();
            private int ignoredEditorErrors;
            private readonly Dictionary<string, int> stats = new Dictionary<string, int>();
            private List<Deck> decks;

            private int match = -1;
            private bool deckPhase;
            private int deckPhaseFrames;
            private bool menuPhase;
            private int menuPhaseFrames;
            private BattleController controller;
            private int expectedCards = -1;
            private string lastSignature;
            private double lastProgress;
            private double nextActionTime;
            private string currentDeck;
            private InputCheck inputCheck;
            private int redrawChecks;
            private int redrawsLeft = -1; // this battle's random redraws still to make (-1: not chosen yet)
            private bool selectionChecked;
            private bool pauseChecked;
            private bool giveUpChecked;
            private bool passKeyChecked;
            private bool keyboardChecked;
            private bool touchChecked;
            private bool speedChecked;
            private bool pickerChecked;
            private bool leaderChecked;
            private bool pileChecked;
            private bool pinchChecked;
            private bool sheetChecked;
            private bool endScreenChecked;
            private int roundEndChecked;
            private readonly HashSet<int> turnsChecked = new HashSet<int>();
            private int boardMismatchMatch = -1;
            private int deckStage;
            private int menuStage;
            private bool roundTripPhase;
            private int roundTripStage;
            private double phaseStart;
            private BattleController previousBattle;

            // Restored at the end: what the run changes in the player's settings
            private string savedBattleDeck;
            private string savedOpponent;
            private int savedDifficulty;
            private bool savedTouch;
            private readonly List<string> testFiles = new List<string>();

            // The deck builder's test deck (a valid Monsters deck in the deck folder) and the opponent chosen in the menu
            private string testDeckPath;
            private string testDeckName;
            private TextAsset chosenOpponent;

            public Runner(int total_matches, int seed, bool batch, string report_path)
            {
                totalMatches = total_matches;
                this.seed = seed;
                this.batch = batch;
                reportPath = report_path;
                rnd = new System.Random(seed);
            }

            public bool Running { get; private set; }

            public void Abort(string why)
            {
                failures.Add("[" + Label() + "] ABORTED: " + why);
                Finish();
            }

            public void Start()
            {
                Running = true;
                savedBattleDeck = PlayerPrefs.GetString("playerDeckPath", "");
                savedOpponent = PlayerPrefs.HasKey("opponentDeck") ? OpponentDecks.Chosen : null; // null: none was chosen
                savedDifficulty = PlayerPrefs.HasKey("aiDifficulty") ? (int)GameSettings.AiDifficulty : -1;
                savedTouch = GameInput.SimulateTouch;
                GameInput.SimulateTouch = false;
                // The run's battles kept as replays apart from the player's
                Gwent.Replays.ReplayLibrary.FolderOverride = Path.Combine(Application.temporaryCachePath, "autoplay-replays");
                if (Directory.Exists(Gwent.Replays.ReplayLibrary.FolderOverride))
                    Directory.Delete(Gwent.Replays.ReplayLibrary.FolderOverride, true);
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
                Time.timeScale = TimeScale;
                decks = GenerateDecks(CardLibrary.Get());
                NextMatch(first: true);
            }

            private void OnLog(string message, string stack, LogType type)
            {
                if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
                    return;
                // Unity 6 editor bug: Quick Search indexing throws on startup when no index exists yet (not game code)
                if (stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup"))
                {
                    ignoredEditorErrors++;
                    return;
                }
                errors.Add("[" + Label() + "] " + type + ": " + message + "\n" + stack.Split('\n').Take(4).Aggregate("", (a, b) => a + "    " + b + "\n"));
            }

            private string Label()
            {
                return "match " + (match + 1) + " (" + currentDeck + " vs " + OpponentDecks.Override + ", " + GameSettings.AiDifficulty + ")";
            }

            private void Count(string key)
            {
                int value;
                stats.TryGetValue(key, out value);
                stats[key] = value + 1;
            }

            private void NextMatch(bool first = false)
            {
                EndInputCheck(); // a check never carries over into another battle
                match++;
                GameLog.Event(LogCategory.Game, "autoplay_match", "match", match + 1);
                turnsChecked.Clear();
                // Every opponent deck in turn, so the AI plays every faction
                List<TextAsset> opponents = OpponentDecks.All();
                OpponentDecks.Override = opponents.Count > 0 ? opponents[match % opponents.Count].name : null;
                if (match >= totalMatches)
                {
                    // Battles done: on to the deck builder, which opens on the battle deck (a test Monsters deck)
                    OpponentDecks.Override = null;
                    DeckStorage.BattleDeckPath = CreateTestDeck(CardLibrary.Get());
                    deckPhase = true;
                    deckPhaseFrames = 0;
                    lastProgress = EditorApplication.timeSinceStartup;
                    currentDeck = "deck builder";
                    SceneManager.LoadScene(SceneNames.Deck);
                    return;
                }

                Rng.Seed(seed * 1000 + match);
                // Every difficulty in turn, each opponent deck meeting all three over the run
                GameSettings.AiDifficulty = (AiDifficulty)((match + match / Math.Max(1, opponents.Count)) % 3);
                Count("AI " + GameSettings.AiDifficulty);
                // Match 0: the built-in default deck; then one generated deck per leader (cycling)
                if (match == 0 || decks.Count == 0)
                {
                    DeckStorage.BattleDeckPath = null;
                    currentDeck = "default NR deck";
                }
                else
                {
                    Deck deck = decks[(match - 1) % decks.Count];
                    string path = Path.Combine(Application.temporaryCachePath, "autoplay_" + deck.Faction.Code() + "_" + deck.Leader + ".json");
                    File.WriteAllText(path, JsonUtility.ToJson(DeckFile.Of(deck)));
                    DeckStorage.BattleDeckPath = path;
                    currentDeck = deck.Name;
                }

                controller = null;
                expectedCards = -1;
                lastSignature = null;
                lastProgress = EditorApplication.timeSinceStartup;
                if (!first)
                    SceneManager.LoadScene(SceneNames.Game);
            }

            private void Tick()
            {
                if (!EditorApplication.isPlaying)
                    return;
                // Real-time input runs at normal speed, like a person playing (the game speed check sets its own; the pause
                // menu, a discard pile, a player's sheet and the rules page stop time)
                if (!PauseMenu.IsOpen && !DiscardViewer.IsOpen && !Gwent.Battle.PlayerSheet.IsOpen && UIScreens.Find("Item The battle") == null
                    && (inputCheck == null || !inputCheck.OwnsTimeScale))
                    Time.timeScale = inputCheck != null ? 1f : TimeScale;

                try
                {
                    if (roundTripPhase)
                    {
                        RoundTripStep();
                        return;
                    }
                    if (menuPhase)
                    {
                        MenuStep();
                        return;
                    }
                    if (deckPhase)
                    {
                        DeckBuilderStep();
                        return;
                    }
                }
                catch (Exception e)
                {
                    Abort("autoplay exception in the " + currentDeck + ": " + e);
                    return;
                }

                try
                {
                    Step();
                }
                catch (Exception e)
                {
                    failures.Add("[" + Label() + "] autoplay exception: " + e);
                    EndInputCheck();
                    NextMatch();
                }
            }

            private void Step()
            {
                if (controller == null || controller != BattleController.Instance)
                {
                    controller = BattleController.Instance;
                    if (controller == null)
                    {
                        // The battle scene never started (it failed to load, or threw while starting)
                        if (EditorApplication.timeSinceStartup - lastProgress > SoftlockSeconds * 3)
                        {
                            failures.Add("[" + Label() + "] the battle did not start");
                            NextMatch();
                        }
                        return;
                    }
                }
                BattleController c = controller;

                if (expectedCards < 0 && c.PlayerInfo.HandList.Count > 0)
                    expectedCards = CountCards(c);

                // Progress / softlock detection
                string signature = Signature(c);
                double now = EditorApplication.timeSinceStartup;
                if (signature != lastSignature)
                {
                    lastSignature = signature;
                    lastProgress = now;
                    CheckConservation(c);
                }
                else if (inputCheck == null && now - lastProgress > SoftlockSeconds) // an input check waits on purpose
                {
                    failures.Add("[" + Label() + "] SOFTLOCK: no progress for " + SoftlockSeconds + "s, state " + signature);
                    NextMatch();
                    return;
                }

                if (c.battleState == BattleState.Won || c.battleState == BattleState.Lost || c.battleState == BattleState.Draw)
                {
                    if (!endScreenChecked)
                    {
                        endScreenChecked = true;
                        // The Witcher 3's emblem of the outcome (its plaque names it)
                        string expected = c.battleState == BattleState.Won ? "win" : c.battleState == BattleState.Lost ? "lose" : "draw";
                        Image emblem = c.PostGame.GetComponentsInChildren<Image>().FirstOrDefault(i => i.name == "Emblem");
                        Check(c.PostGame.activeInHierarchy && emblem != null && emblem.sprite != null && emblem.sprite.name == expected, "the end screen shows the \"" + expected + "\" emblem", "end screen");
                        GameObject save = GameObject.Find("Button Save replay");
                        Check(GameObject.Find("Button Play again") != null && save != null && GameObject.Find("Button Main menu") != null, "the end screen offers Play again, Save replay and Main menu", "end screen");
                        // Saved as a replay: the newest, as many events as the battle, playing again the same; once only
                        int before = Gwent.Replays.ReplayLibrary.All().Count;
                        save?.GetComponent<Button>().onClick.Invoke();
                        List<Gwent.Replays.ReplayLibrary.Entry> saved = Gwent.Replays.ReplayLibrary.All();
                        string problem = null;
                        Check(saved.Count == before + 1 && c.Match != null && saved[0].Record.Events == c.Match.Events.Count && saved[0].Record.Play(c.Db, out problem) != null,
                            "Save replay saves the battle, and it plays again" + (problem != null ? " (" + problem + ")" : ""), "replay");
                        Check(c.ReplaySaved && !c.CanSaveReplay && save != null && !save.GetComponent<Button>().interactable, "Save replay is done once", "replay");
                    }
                    results.Add(Label() + ": " + c.battleState + " " + string.Join("-", c.PlayerInfo.RoundsScore) + " vs " + string.Join("-", c.EnemyInfo.RoundsScore));
                    Count("result " + c.battleState);
                    NextMatch();
                    return;
                }

                // The round's result shows over the board before it clears
                if (c.battleState == BattleState.RoundOver && c.PlayerInfo.RoundsScore.Count != roundEndChecked)
                {
                    roundEndChecked = c.PlayerInfo.RoundsScore.Count;
                    Check(BattleBanner.Busy, "the round's result is announced", "round end");
                }

                if (inputCheck != null)
                {
                    inputCheck.Tick();
                    if (inputCheck.Done)
                        EndInputCheck();
                    return;
                }

                if (now < nextActionTime)
                    return;
                nextActionTime = now + 0.02;
                PlayerStep(c);
            }

            private void EndInputCheck()
            {
                if (inputCheck == null)
                    return;
                inputCheck.Teardown();
                inputCheck = null;
                lastProgress = EditorApplication.timeSinceStartup; // the time it took was no lack of progress
            }

            //----------------------------------------The random player----------------------------------------//
            private void PlayerStep(BattleController c)
            {
                // The Scoia'tael choice comes first, before the redraw (as in The Witcher 3)
                if (c.TurnPicker.activeInHierarchy)
                {
                    Check(GameObject.Find("Button Go first") != null && GameObject.Find("Button Go second") != null, "the Scoia'tael turn dialog shows its two choices", "turn dialog");
                    FirstTurnPicker picker = c.TurnPicker.GetComponentInChildren<FirstTurnPicker>();
                    if (rnd.Next(2) == 0) picker.GoFirst(); else picker.GoSecond();
                    Count("scoiatael turn pick");
                    return;
                }

                if (c.battleState == BattleState.Start && c.PlayerInfo.CanExchange)
                {
                    // The redraw opens once the coin toss has been announced
                    if (!RedrawPicker.IsOpen)
                        return;
                    InputScenario[] redraws = { InputScenario.RedrawSlowClicksThenDone, InputScenario.RedrawTwice, InputScenario.RedrawCarousel, InputScenario.RedrawSpace };
                    if (redrawChecks < redraws.Length)
                    {
                        InputScenario scenario = redraws[redrawChecks++];
                        inputCheck = new InputCheck(c, scenario, (ok, what) => Check(ok, what, "redraw input"));
                        return;
                    }

                    // Up to two random redraws, one at a time (each shown before the next), then the hand is kept
                    List<int> hand = c.PlayerInfo.HandList;
                    if (c.IsPresenting)
                        return;
                    if (redrawsLeft < 0)
                        redrawsLeft = rnd.Next(3);
                    if (redrawsLeft > 0 && c.PlayerInfo.CardsExchanged < 2 && hand.Count > 0)
                    {
                        c.RedrawPlayerCard(hand[rnd.Next(hand.Count)]);
                        redrawsLeft--;
                        Count("exchange");
                        return;
                    }
                    c.FinishCardExchange();
                    redrawsLeft = -1;
                    return;
                }

                // A play is being shown: wait for it to end
                if (c.IsPresenting)
                    return;

                if (c.Panel.activeInHierarchy)
                {
                    BattlePickers panel = c.Panel.GetComponent<BattlePickers>();
                    IList<int> offered = panel.Cards;
                    // The first picker with a choice to make: through its keys
                    if (!pickerChecked && offered.Count >= 2 && c.pendingAction == PendingAction.PanelChoice)
                    {
                        pickerChecked = true;
                        inputCheck = new InputCheck(c, InputScenario.PickerKeys, (ok, what) => Check(ok, what, "picker keys"));
                        return;
                    }
                    if (offered.Count == 0 || rnd.Next(10) == 0)
                    {
                        c.CancelPanel();
                        Count("panel skip");
                    }
                    else
                    {
                        panel.Choose(offered[rnd.Next(offered.Count)]);
                        Count("panel choose");
                    }
                    return;
                }

                if (c.battleState != BattleState.PlayerTurn)
                    return;
                if (c.pendingAction == PendingAction.None)
                {
                    CheckBoardShowsModel(c);
                    // The Witcher 3: a side with nothing left to play passes automatically, it never gets a turn
                    if (c.PlayerInfo.HandList.Count == 0 && turnsChecked.Add(c.PlayerInfo.RoundsScore.Count * 100 + c.EnemyInfo.HandList.Count))
                        Check(c.CanUseLeader(true), "a turn with an empty hand still has the leader to use", "auto pass");
                }

                switch (c.pendingAction)
                {
                    case PendingAction.PlacePickedCard:
                        PlaceSelected(c);
                        return;
                    case PendingAction.DiscardCards:
                        // After the second, the match asks for a card of the deck (a picker)
                        List<int> hand = c.PlayerInfo.HandList;
                        c.DiscardCard(hand[rnd.Next(hand.Count)], true);
                        Count("leader discard");
                        return;
                    case PendingAction.PanelChoice:
                        return;
                }

                if (!selectionChecked && c.PlayerField.transform.Find("Hand").childCount > 0)
                {
                    selectionChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.SelectCard, (ok, what) => Check(ok, what, "selection input"));
                    return;
                }
                if (!pauseChecked)
                {
                    pauseChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.PauseMenu, (ok, what) => Check(ok, what, "pause menu input"));
                    return;
                }
                if (!giveUpChecked && c.CanGiveUp)
                {
                    giveUpChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.GiveUpAsked, (ok, what) => Check(ok, what, "give up input"));
                    return;
                }
                if (!passKeyChecked)
                {
                    passKeyChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.PassWithSpace, (ok, what) => Check(ok, what, "pass input"));
                    return;
                }
                if (!keyboardChecked && c.PlayerInfo.HandList.Count > 0)
                {
                    keyboardChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.KeyboardPlay, (ok, what) => Check(ok, what, "keyboard play"));
                    return;
                }
                if (!touchChecked && c.PlayerInfo.HandList.Count > 0)
                {
                    touchChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.TouchControls, (ok, what) => Check(ok, what, "touch controls"));
                    return;
                }
                if (!speedChecked)
                {
                    speedChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.GameSpeed, (ok, what) => Check(ok, what, "game speed"));
                    return;
                }
                if (!sheetChecked)
                {
                    sheetChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.PlayerSheet, (ok, what) => Check(ok, what, "player sheet"));
                    return;
                }
                if (!pinchChecked && c.PlayerInfo.HandList.Count > 0)
                {
                    pinchChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.PinchZoom, (ok, what) => Check(ok, what, "pinch zoom"));
                    return;
                }
                if (!pileChecked && (c.PlayerInfo.DiscardList.Count >= 2 || c.EnemyInfo.DiscardList.Count >= 2))
                {
                    pileChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.DiscardPile, (ok, what) => Check(ok, what, "discard pile"));
                    return;
                }
                // The first turn with a leader ability that would act
                if (!leaderChecked && c.CanUseLeader(true) && c.LeaderProblem() == null && c.PlayerInfo.HandList.Count > 0)
                {
                    leaderChecked = true;
                    inputCheck = new InputCheck(c, InputScenario.LeaderLift, (ok, what) => Check(ok, what, "leader input"));
                    return;
                }

                // A normal turn: maybe the leader, sometimes pass, otherwise play a random card through the real UI scripts
                if (c.CanUseLeader(true) && rnd.Next(6) == 0)
                {
                    c.ActivateLeader(c.PlayerInfo.Leader, true);
                    Count("leader " + c.PlayerInfo.Leader + (c.Match.Board.Player.canLeader ? " (unavailable)" : ""));
                    return;
                }

                Transform hand_t = c.PlayerField.transform.Find("Hand");
                bool should_pass = hand_t.childCount == 0 || (c.EnemyInfo.hasPassed && c.PlayerInfo.TotalScore() > c.EnemyInfo.TotalScore()) || rnd.Next(14) == 0;
                if (should_pass)
                {
                    c.SkipTurn(true);
                    Count("pass");
                    return;
                }

                GameObject card = hand_t.GetChild(rnd.Next(hand_t.childCount)).gameObject;
                card.GetComponent<CardSelect>().OnCardSelect();
                if (c.selectedCard != card)
                    return;
                PlaceSelected(c);
            }

            private void PlaceSelected(BattleController c)
            {
                if (c.selectedCard == null)
                    return;
                Card stats = c.selectedCard.GetComponent<CardFace>().Card;
                Count("play " + (stats.faction == Faction.Special ? stats._idstr : stats.ability == Ability.None ? "unit" : stats.ability.Code()));

                // Only where the card can go lights up: no rows for a decoy (its targets get a frame) or a weather card
                List<GameObject> lit = c.PlayerField.GetComponent<RowPicker>().highLightedRows.Concat(c.EnemyField.GetComponent<RowPicker>().highLightedRows).ToList();
                WeatherSlot weather_board = c.WeatherField.GetComponent<WeatherSlot>();
                bool weather_lit = c.WeatherField.GetComponent<Image>().sprite == weather_board.weatherSelected;
                if (stats.ability == Ability.Decoy)
                {
                    int frames = c.PlayerField.GetComponentsInChildren<Transform>().Count(t => t.name == "DecoyTarget");
                    int swappable = Rows.Units.Sum(row => c.PlayerInfo.UnitRow(row).Count(id => PlayRules.IsDecoyTarget(c.Db.Get(id))));
                    Check(lit.Count == 0 && !weather_lit && frames == swappable, "a decoy frames its " + swappable + " targets and lights no row (" + frames + " frames, " + lit.Count + " rows)", "highlight");
                }
                else if (stats.row == Row.Weather || stats.IsSpecial && stats.ability == Ability.Scorch)
                    Check(lit.Count == 0 && weather_lit, "a weather card lights only the weather board", "highlight");

                if (stats.ability == Ability.Decoy)
                {
                    List<GameObject> targets = new List<GameObject>();
                    foreach (Row row in Rows.Units)
                        foreach (Transform t in c.PlayerField.transform.Find(row.ToString()).Find("Unit"))
                            if (t.GetComponent<CardSelect>() != null)
                                targets.Add(t.gameObject);
                    if (targets.Count == 0)
                    {
                        // Nothing to swap: click the same card again to deselect it
                        c.selectedCard.GetComponent<CardSelect>().OnCardSelect();
                        c.SkipTurn(true);
                        return;
                    }
                    targets[rnd.Next(targets.Count)].GetComponent<CardSelect>().OnCardSelect();
                    return;
                }

                if (stats.row == Row.Weather || stats.IsSpecial && stats.ability == Ability.Scorch)
                {
                    c.WeatherField.GetComponent<WeatherSlot>().OnWeatherClick();
                    return;
                }

                List<GameObject> rows = new List<GameObject>();
                rows.AddRange(c.PlayerField.GetComponent<RowPicker>().highLightedRows);
                rows.AddRange(c.EnemyField.GetComponent<RowPicker>().highLightedRows);
                if (rows.Count == 0)
                {
                    failures.Add("[" + Label() + "] nothing highlighted for selected card " + stats._idstr);
                    c.SkipTurn(true);
                    return;
                }
                GameObject target = rows[rnd.Next(rows.Count)];
                RowClick click = target.GetComponent<RowClick>();
                if (click != null)
                    click.OnRowClick();
                else
                    c.DirectlyPlaceCard(c.selectedCard, target, true);
            }

            //----------------------------------------Deck builder----------------------------------------//
            // Opens on the test deck: checks the screen, real clicks (InputScenario.DeckBuilder), then the API checks. The
            // test deck ends up the battle deck, picked in the builder, for the round trip through the main menu.
            private void DeckBuilderStep()
            {
                DeckBuilder dc = DeckBuilder.Instance;
                if (deckStage == 0)
                {
                    if (dc == null || !dc.IsReady || ++deckPhaseFrames < 30)
                    {
                        // let every Start() of the scene run first, but not forever (the scene failed to load)
                        if (EditorApplication.timeSinceStartup - lastProgress > SoftlockSeconds * 3)
                            Abort("the deck builder did not start");
                        return;
                    }
                    CheckBuilderOpensOnDeck(dc);
                    deckStage = 1;
                    inputCheck = new InputCheck(null, InputScenario.DeckBuilder, (ok, what) => Check(ok, what, "deck builder input"), testDeckPath);
                    return;
                }
                if (TickInputCheck())
                    return;

                List<string> created = new List<string>();
                try
                {
                    RunDeckBuilderChecks(dc, created);
                }
                catch (Exception e)
                {
                    failures.Add("[deck builder] exception: " + e);
                }
                finally
                {
                    foreach (string path in created)
                        if (File.Exists(path))
                            File.Delete(path);
                }

                dc.deckSummary.SelectDeckPath(testDeckPath);
                Check(PlayerPrefs.GetString("playerDeckPath", "") == testDeckPath, "the deck picked in the builder is saved as the battle deck");
                DeckStorage.BattleDeckPath = null; // the main menu reads it back from the saved settings

                // Then the main menu: its scene, settings and rules, and Play
                deckPhase = false;
                menuPhase = true;
                currentDeck = "main menu";
                SceneManager.LoadScene(SceneNames.Menu);
            }

            // The builder opened on the battle deck's faction: its header panel and perk, both card lists, the dropdown
            // and the status line
            private void CheckBuilderOpensOnDeck(DeckBuilder dc)
            {
                CardGrid browse = UnityEngine.Object.FindObjectsByType<CardGrid>().FirstOrDefault(m => !m.CompareTag("Player"));
                Check(dc.currentDeckPath == testDeckPath && dc.my_deck.Name == testDeckName, "the deck builder opens on the battle deck");
                Check(dc.deckCollection.faction == Faction.Monsters && browse != null && browse.faction == Faction.Monsters
                    && dc.deckCollection.header.CurrentPanel == Array.IndexOf(CardGrid.HeaderFactions, Faction.Monsters)
                    && dc.deckCollection.factionPerk.text == Faction.Monsters.Ability(),
                    "... on its faction: the header, its ability, the deck's cards and the collection (" + dc.deckCollection.faction + ", panel " + dc.deckCollection.header.CurrentPanel + ")");
                TMP_Dropdown picker = dc.deckSummary.deckPicker;
                Check(picker.options.Count > picker.value && picker.options[picker.value].text == testDeckName, "the deck dropdown shows the deck");
                Check(dc.BattleDeckStatus == "Battle deck: " + testDeckName, "the status line names the battle deck (\"" + dc.BattleDeckStatus + "\")");
            }

            // Ticks the input check running in a phase; false when there is none
            private bool TickInputCheck()
            {
                if (inputCheck == null)
                    return false;
                inputCheck.Tick();
                if (inputCheck.Done)
                    EndInputCheck();
                return true;
            }

            // A valid Monsters deck in the deck folder, the battle deck while the builder is checked: every hero it can
            // hold (the collection then offers none), the strongest other units (the weakest ones stay in the collection,
            // to be added), and 10 specials, in the order of their ids, as many copies of each as exist (an 11th is
            // refused for the 10-special limit)
            private string CreateTestDeck(CardDatabase db)
            {
                Faction faction = Faction.Monsters;
                List<Card> units = db.All.Where(card => card.IsUnit && !CardIds.IsSummoned(card._id) && (card.faction == faction || card.faction == Faction.Neutral)).ToList();
                List<int> cards = units.Where(card => card.unique).Select(card => card._id).ToList();
                cards.AddRange(units.Where(card => !card.unique).OrderByDescending(card => card.strength).ThenBy(card => card._id).Take(DeckRules.MinUnits).Select(card => card._id));
                int special_count = 0;
                foreach (Card special in db.All.Where(card => card.IsSpecial && DeckRules.Fits(card, faction)).OrderBy(card => card._id))
                    for (int i = 0; i < DeckRules.MaxCopies(special) && special_count < DeckRules.MaxSpecials; i++, special_count++)
                        cards.Add(special._id);

                testDeckName = "Autoplay " + seed + " Monsters";
                testDeckPath = DeckStorage.PathFor(faction, DeckRules.ToFileName(testDeckName));
                testFiles.Add(testDeckPath);
                Deck deck = new Deck { Faction = faction, Name = testDeckName, Leader = LeaderIds.ByFaction[faction][0], Cards = cards };
                DeckStorage.EnsureDirectories();
                Check(DeckRules.IsValid(deck, db) && DeckStorage.Save(deck, testDeckPath), "the test deck is valid and saved (" + string.Join("; ", DeckRules.Validate(deck, db)) + ")");
                return testDeckPath;
            }

            //----------------------------------------Main menu, then a battle from it----------------------------------------//
            private void MenuStep()
            {
                if (TickInputCheck())
                    return;
                if (menuStage == 0)
                {
                    if (++menuPhaseFrames < 60)
                        return;
                    MainMenu menu = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
                    Check(menu != null && menu.Items.Count == 7 && menu.Items[1].name == "Item Multiplayer" && menu.Items[3].name == "Item Replays", "the main menu loads with its 7 entries, Multiplayer second, Replays fourth", "main menu");
                    Check(DeckStorage.BattleDeckPath == testDeckPath, "the main menu picks up the battle deck saved by the deck builder", "main menu");
                    inputCheck = new InputCheck(null, InputScenario.MainMenuSettings, (ok, what) => Check(ok, what, "main menu input"));
                    menuStage = 1;
                    return;
                }
                if (menuStage == 1)
                {
                    // Play: the opponent picker, then the battle against the one chosen (not the first, to tell them apart)
                    List<TextAsset> opponents = OpponentDecks.All();
                    chosenOpponent = opponents.Count > 1 ? opponents[1] : opponents.FirstOrDefault();
                    inputCheck = new InputCheck(null, InputScenario.MenuPlay, (ok, what) => Check(ok, what, "main menu input"), null, chosenOpponent);
                    menuStage = 2;
                    return;
                }
                menuPhase = false;
                roundTripPhase = true;
                phaseStart = EditorApplication.timeSinceStartup;
                currentDeck = "battle started from the menu";
            }

            // The battle started from the menu uses the builder's deck against the chosen opponent; then a battle deck that
            // is no longer valid gives way to the default deck, with a notice
            private void RoundTripStep()
            {
                BattleController battle = BattleController.Instance;
                if (battle == null || battle == previousBattle || battle.PlayerInfo.HandList.Count == 0 || LoadingScreen.Showing)
                {
                    if (EditorApplication.timeSinceStartup - phaseStart > SoftlockSeconds * 3)
                        Abort("the " + currentDeck + " did not start");
                    return;
                }
                if (roundTripStage == 0)
                {
                    Deck mine = DeckStorage.Load(testDeckPath);
                    Deck theirs = chosenOpponent != null ? CardLibrary.ParseDeck(chosenOpponent.text, chosenOpponent.name) : null;
                    Check(mine != null && battle.PlayerInfo.Faction == mine.Faction && battle.PlayerInfo.Leader == mine.Leader,
                        "the battle uses the deck chosen in the deck builder, with the leader picked there", "battle from the menu");
                    Check(theirs != null && battle.EnemyInfo.Faction == theirs.Faction && battle.EnemyInfo.Leader == theirs.Leader,
                        "... against the opponent chosen in the menu", "battle from the menu");

                    string invalid = Path.Combine(Application.temporaryCachePath, "autoplay_invalid.json");
                    File.WriteAllText(invalid, JsonUtility.ToJson(DeckFile.Of(new Deck { Faction = Faction.Monsters, Name = "Autoplay invalid", Leader = LeaderIds.ByFaction[Faction.Monsters][0], Cards = new List<int>(mine != null ? mine.Cards.Take(5) : new int[0]) })));
                    DeckStorage.BattleDeckPath = invalid;
                    previousBattle = battle;
                    roundTripStage = 1;
                    phaseStart = EditorApplication.timeSinceStartup;
                    currentDeck = "battle with an invalid deck";
                    SceneManager.LoadScene(SceneNames.Game);
                    return;
                }
                if (roundTripStage == 1)
                {
                    TextAsset fallback = Resources.Load<TextAsset>("Decks/player_default");
                    Deck standard = fallback != null ? CardLibrary.ParseDeck(fallback.text, fallback.name) : null;
                    Check(standard != null && battle.PlayerInfo.Faction == standard.Faction && battle.PlayerInfo.Leader == standard.Leader,
                        "an invalid battle deck gives way to the default deck", "battle from the menu");
                    Check(ToastText().Contains("can't be used"), "... and the battle says so (\"" + ToastText() + "\")", "battle from the menu");

                    // A preset (an AI deck) as the battle deck
                    List<TextAsset> presets = OpponentDecks.All();
                    DeckStorage.BattleDeckPath = presets.Count > 0 ? OpponentDecks.PresetPath(presets[presets.Count - 1]) : null;
                    previousBattle = battle;
                    roundTripStage = 2;
                    phaseStart = EditorApplication.timeSinceStartup;
                    currentDeck = "battle with a preset";
                    SceneManager.LoadScene(SceneNames.Game);
                    return;
                }
                Deck preset = DeckStorage.Load(DeckStorage.BattleDeckPath);
                Check(preset != null && battle.PlayerInfo.Faction == preset.Faction && battle.PlayerInfo.Leader == preset.Leader,
                    "a preset picked as the battle deck is the one played", "battle from the menu");
                Finish();
            }

            private void Check(bool condition, string what, string area = "deck builder")
            {
                Count(area + (condition ? " check passed" : " check FAILED"));
                if (!condition)
                    failures.Add("[" + area + "] " + what);
            }

            private void RunDeckBuilderChecks(DeckBuilder dc, List<string> created)
            {
                TMP_InputField input = UnityEngine.Object.FindAnyObjectByType<TMP_InputField>(FindObjectsInactive.Include); // hidden until the pencil
                Check(input != null, "deck name input field found");
                Faction faction = dc.my_deck.Faction;
                string tag = "Autoplay " + seed;
                Func<int> deck_count = () => DeckStorage.DeckPaths(faction).Count;
                int before = deck_count();

                // Presets: the faction's AI decks, picked as they are (as the battle deck too), never renamed or deleted;
                // an edit is saved as the player's own copy and the preset stays as it was
                List<string> presets = OpponentDecks.PresetPaths(faction);
                Check(presets.Count > 0 && presets.All(p => dc.deckSummary.myPaths.Contains(p)), "the faction's AI decks are offered as presets");
                string first_preset = presets.FirstOrDefault();
                // A new deck of the player's: a preset named (its cards, under that name)
                Func<string, bool> from_preset = name =>
                {
                    dc.deckSummary.SelectDeckPath(first_preset);
                    return dc.NameDeck(name);
                };
                if (presets.Count > 0)
                {
                    string preset = presets[0];
                    Deck original = DeckStorage.Load(preset);
                    dc.deckSummary.SelectDeckPath(preset);
                    Check(dc.currentDeckPath == preset && PlayerPrefs.GetString("playerDeckPath", "") == preset && dc.BattleDeckStatus.Contains(original.Name + " (preset)"),
                        "a preset picked in the builder is the battle deck (\"" + dc.BattleDeckStatus + "\")");
                    // The pencil: the list gives way to the name field, offering a copy's name; the cross leaves it all as it was
                    dc.RenameDeck(input);
                    Check(dc.Naming && input.gameObject.activeInHierarchy && !dc.deckSummary.deckPicker.gameObject.activeInHierarchy
                        && input.text.StartsWith(original.Name + " (copy"), "the pencil opens the name field over the list, offering a copy's name (\"" + input.text + "\")");
                    dc.DeleteDeck();
                    Check(!dc.Naming && dc.deckSummary.deckPicker.gameObject.activeInHierarchy && dc.currentDeckPath == preset && deck_count() == before
                        && GameObject.Find("ConfirmDialog") == null, "the cross closes the name field, nothing named, nothing deleted");
                    Check(dc.NameDeck(tag + " Preset") && !OpponentDecks.IsPreset(dc.currentDeckPath) && dc.my_deck.Name == tag + " Preset"
                        && dc.my_deck.Cards.Count == original.Cards.Count && deck_count() == before + 1 && DeckStorage.Load(preset).Name == original.Name,
                        "naming a preset makes the player's own copy under that name; the preset stays as it was");
                    created.Add(dc.currentDeckPath);
                    dc.ConfirmDelete(dc.currentDeckPath);
                    dc.deckSummary.SelectDeckPath(preset);
                    dc.DeleteDeck();
                    Check(GameObject.Find("ConfirmDialog") == null && dc.currentDeckPath == preset, "a preset cannot be deleted");
                    dc.my_deck.Cards.RemoveAt(0);
                    dc.deckCollection.OnDeckEdit();
                    string copy = dc.currentDeckPath;
                    created.Add(copy);
                    Deck saved_copy = DeckStorage.Load(copy);
                    Check(!OpponentDecks.IsPreset(copy) && File.Exists(copy) && saved_copy != null && saved_copy.Name.StartsWith(original.Name + " (copy")
                        && saved_copy.Cards.Count == original.Cards.Count - 1 && DeckStorage.Load(preset).Cards.Count == original.Cards.Count,
                        "editing a preset saves the change in the player's own copy (" + (saved_copy != null ? saved_copy.Name : "none") + "); the preset stays as it was");
                    dc.ConfirmDelete(copy);
                    before = deck_count();
                }

                // The list's last entry: a new empty deck, named first (the cross leaves it unmade)
                dc.deckSummary.OnOptionChanged(dc.deckSummary.myDecks.Count);
                Check(dc.Naming && input.gameObject.activeInHierarchy && input.text.StartsWith("New deck"),
                    "the list's \"" + DeckSummary.NewDeckOption + "\" opens the name field, offering \"" + input.text + "\"");
                dc.DeleteDeck();
                Check(!dc.Naming && deck_count() == before && GameObject.Find("ConfirmDialog") == null, "the cross leaves no new deck");
                dc.deckSummary.OnOptionChanged(dc.deckSummary.myDecks.Count);
                input.text = tag + " Empty";
                dc.RenameDeck(input); // the check
                string empty = DeckStorage.PathFor(faction, DeckRules.ToFileName(tag + " Empty"));
                created.Add(empty);
                Deck made = DeckStorage.Load(empty);
                Check(!dc.Naming && made != null && made.Cards.Count == 0 && made.Name == tag + " Empty" && dc.currentDeckPath == empty && deck_count() == before + 1,
                    "naming the new deck makes it, with no cards, and shows it");
                dc.ConfirmDelete(empty);

                // A new deck: a preset named, its spaces tidied
                from_preset("  " + tag + "  Deck ");
                string path = DeckStorage.PathFor(faction, DeckRules.ToFileName(tag + " Deck"));
                created.Add(path);
                Check(File.Exists(path) && deck_count() == before + 1, "naming a preset creates the deck file");
                Check(dc.currentDeckPath == path && dc.my_deck.Name == tag + " Deck", "the new deck is selected");

                // Same name with other letter case / spacing: rejected, nothing overwritten (B3)
                dc.my_deck.Cards.Add(LeaderIds.ByFaction[faction][0]); // marker that must survive
                File.WriteAllText(path, JsonUtility.ToJson(DeckFile.Of(new Deck { Faction = faction, Name = tag + " Deck", Leader = dc.my_deck.Leader, Cards = new List<int> { 1 } })));
                from_preset(tag.ToUpperInvariant() + " DECK");
                Deck after_duplicate = DeckStorage.Load(path);
                Check(deck_count() == before + 1 && after_duplicate != null && after_duplicate.Cards.Count == 1, "a duplicate name does not overwrite the existing deck");

                // Fill it up to a valid deck: it becomes the battle deck (B9)
                dc.deckSummary.SelectDeckPath(path);
                CardDatabase db = dc.Db;
                dc.my_deck.Cards = db.All.Where(c => c.IsUnit && (c.faction == faction || c.faction == Faction.Neutral) && !c.unique && !CardIds.IsSummoned(c._id))
                    .SelectMany(c => Enumerable.Repeat(c._id, Math.Min(2, c.copies))).Take(24).ToList();
                dc.deckCollection.OnDeckEdit();
                Check(DeckRules.IsValid(DeckStorage.Load(path), db), "edited deck saved and valid");
                Check(DeckStorage.BattleDeckPath == path && PlayerPrefs.GetString("playerDeckPath", "") == path, "a valid edited deck becomes the battle deck");

                // Remove cards below the minimum: no longer the battle deck
                dc.my_deck.Cards.RemoveRange(0, 10);
                dc.deckCollection.OnDeckEdit();
                Check(DeckStorage.BattleDeckPath == null && !PlayerPrefs.HasKey("playerDeckPath"), "an invalid deck stops being the battle deck");

                // Rename (B4)
                dc.NameDeck(tag + " Renamed");
                string renamed = DeckStorage.PathFor(faction, DeckRules.ToFileName(tag + " Renamed"));
                created.Add(renamed);
                Check(File.Exists(renamed) && !File.Exists(path), "rename moves the deck file");
                Check(dc.my_deck.Name == tag + " Renamed" && dc.my_deck.Cards.Count == 14, "rename keeps the cards");

                // Rename another deck onto that name: rejected, both decks intact
                from_preset(tag + " Other");
                string other = DeckStorage.PathFor(faction, DeckRules.ToFileName(tag + " Other"));
                created.Add(other);
                dc.NameDeck((tag + " renamed").ToLowerInvariant());
                Check(File.Exists(other) && File.Exists(renamed) && DeckStorage.Load(renamed).Cards.Count == 14, "rename onto an existing name is rejected");

                // Unsafe names never leave the deck folder (B20)
                from_preset("../../" + tag + " escape");
                string escaped = DeckStorage.PathFor(faction, DeckRules.ToFileName("../../" + tag + " escape"));
                created.Add(escaped);
                Check(File.Exists(escaped) && Path.GetDirectoryName(escaped) == DeckStorage.FactionFolder(faction), "unsafe characters are stripped from the file name");

                // A corrupt deck file is skipped instead of breaking the builder (B18)
                string corrupt = Path.Combine(DeckStorage.FactionFolder(faction), "autoplay_corrupt_" + seed + ".json");
                created.Add(corrupt);
                File.WriteAllText(corrupt, "{ this is not json");
                dc.PopulateDeckAssets();
                dc.deckSummary.LoadDecks();
                Check(dc.deckSummary.myPaths.Count(p => !OpponentDecks.IsPreset(p)) == deck_count() - 1, "a corrupt deck file is skipped");

                // Delete (the confirmation dialog's "Delete" button calls ConfirmDelete)
                dc.deckSummary.SelectDeckPath(other);
                dc.DeleteDeck();
                Check(File.Exists(other), "DeleteDeck only asks for confirmation");
                foreach (string path_to_delete in new[] { other, renamed, escaped })
                    dc.ConfirmDelete(path_to_delete);
                Check(!File.Exists(other) && !File.Exists(renamed) && !File.Exists(escaped), "confirmed deletes remove the files");
            }

            //----------------------------------------Checks----------------------------------------//
            private static int CountCards(BattleController c)
            {
                int total = 0;
                foreach (PlayerState info in new PlayerState[] { c.PlayerInfo, c.EnemyInfo })
                    total += info.AllCards().Count(id => !CardIds.IsSummoned(id)) - info.LeaderHornRows.Count;
                return total;
            }

            private void CheckConservation(BattleController c)
            {
                if (expectedCards < 0 || c.battleState == BattleState.Start)
                    return;
                int now = CountCards(c);
                if (now != expectedCards)
                {
                    failures.Add("[" + Label() + "] CARD COUNT changed: " + expectedCards + " -> " + now + " (" + Signature(c) + ")");
                    expectedCards = now; // report each change once
                }
            }

            // The board must show exactly the model's cards (the refresh reuses card objects instead of rebuilding)
            private void CheckBoardShowsModel(BattleController c)
            {
                if (boardMismatchMatch == match)
                    return;
                List<string> problems = new List<string>();
                foreach (bool player in new[] { true, false })
                {
                    PlayerState info = player ? c.PlayerInfo : c.EnemyInfo;
                    Transform field = (player ? c.PlayerField : c.EnemyField).transform;
                    string side = player ? "player " : "opponent ";
                    // Hands as drawn, rows in The Witcher 3's order
                    Compare(field.Find("Hand"), info.HandList, side + "hand", problems);
                    foreach (Row row in Rows.Units)
                    {
                        Transform row_t = field.Find(row.ToString());
                        Compare(row_t.Find("Unit"), CardOrder.Sorted(info.UnitRow(row), c.Db), side + row, problems);
                        Compare(row_t.Find("Special"), info.SpecialRow(row), side + row + " horn", problems);
                    }
                    Compare(field.Find("Discard/Pile"), info.DiscardList, side + "discard pile", problems);
                }
                Compare(c.WeatherField.transform, c.weatherList, "weather", problems);
                Count(problems.Count == 0 ? "board shows the model" : "board mismatch");
                if (problems.Count > 0)
                {
                    failures.Add("[" + Label() + "] BOARD shows other cards than the game: " + string.Join("; ", problems));
                    boardMismatchMatch = match; // report once per match
                }
            }

            private static void Compare(Transform container, List<int> ids, string what, List<string> problems)
            {
                List<string> shown = new List<string>();
                foreach (Transform child in container)
                    if (child.GetComponent<CardFace>() != null)
                        shown.Add(child.name);
                string expected = string.Join(",", ids), actual = string.Join(",", shown);
                if (expected != actual)
                    problems.Add(what + " shows [" + actual + "] instead of [" + expected + "]");
            }

            private static string Signature(BattleController c)
            {
                PlayerState p = c.PlayerInfo, e = c.EnemyInfo;
                return string.Format("{0}/{1} P[h{2} d{3} g{4} b{5} pass{6} lives{7}] E[h{8} d{9} g{10} b{11} pass{12} lives{13}] w{14} panel{15}",
                    c.battleState, c.pendingAction, p.HandList.Count, p.DeckList.Count, p.DiscardList.Count, p.UnitsOnBoard(), p.hasPassed, p.Lives,
                    e.HandList.Count, e.DeckList.Count, e.DiscardList.Count, e.UnitsOnBoard(), e.hasPassed, e.Lives, c.weatherList.Count, c.Panel.activeSelf);
            }

            private void Finish()
            {
                if (!Running)
                    return;
                Running = false;
                EndInputCheck();
                // The generated battle decks were temporary files, and so was the deck builder's test deck
                foreach (string temp in Directory.GetFiles(Application.temporaryCachePath, "autoplay_*.json"))
                    File.Delete(temp);
                foreach (string file in testFiles)
                    if (File.Exists(file))
                        File.Delete(file);
                OpponentDecks.Override = null;
                if (Gwent.Replays.ReplayLibrary.FolderOverride != null && Directory.Exists(Gwent.Replays.ReplayLibrary.FolderOverride))
                    Directory.Delete(Gwent.Replays.ReplayLibrary.FolderOverride, true);
                Gwent.Replays.ReplayLibrary.FolderOverride = null;
                // What the run changed in the player's settings: the battle deck, the opponent, the touch controls switch
                if (string.IsNullOrEmpty(savedBattleDeck))
                    PlayerPrefs.DeleteKey("playerDeckPath");
                else
                    PlayerPrefs.SetString("playerDeckPath", savedBattleDeck);
                // Keys the player never had are deleted (not left empty), and the deletions saved: a batch run's editor
                // quits without saving them
                if (savedOpponent == null)
                    PlayerPrefs.DeleteKey("opponentDeck");
                else
                    OpponentDecks.Chosen = savedOpponent;
                if (savedDifficulty < 0)
                    PlayerPrefs.DeleteKey("aiDifficulty");
                else
                    GameSettings.AiDifficulty = (AiDifficulty)savedDifficulty;
                PlayerPrefs.Save();
                DeckStorage.BattleDeckPath = null;
                GameInput.SimulateTouch = savedTouch;
                EditorApplication.update -= Tick;
                Application.logMessageReceived -= OnLog;
                Time.timeScale = 1f;

                StringBuilder report = new StringBuilder();
                bool ok = failures.Count == 0 && errors.Count == 0;
                report.AppendLine("Gwent autoplay: " + (ok ? "PASSED" : "FAILED") + " - " + totalMatches + " matches, seed " + seed);
                report.AppendLine();
                report.AppendLine("Results:");
                foreach (string result in results)
                    report.AppendLine("  " + result);
                report.AppendLine();
                report.AppendLine("Coverage (actions performed by the random player, and checks):");
                foreach (KeyValuePair<string, int> stat in stats.OrderBy(s => s.Key))
                    report.AppendLine("  " + stat.Key + ": " + stat.Value);
                report.AppendLine();
                report.AppendLine("Failures: " + failures.Count);
                if (failures.Count > 0 && GameLog.FilePath != null)
                    report.AppendLine("  (each one is in the game log too, next to what the game was doing: " + GameLog.FilePath + ", event autoplay_failure)");
                foreach (string failure in failures)
                    report.AppendLine("  " + failure);
                report.AppendLine("Errors logged: " + errors.Count + (ignoredEditorErrors > 0 ? " (ignored " + ignoredEditorErrors + " known Unity editor-internal error(s))" : ""));
                foreach (string error in errors.Take(40))
                    report.AppendLine("  " + error);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                File.WriteAllText(reportPath, report.ToString());
                Debug.Log(report.ToString());

                if (batch)
                    EditorApplication.Exit(ok ? 0 : 1);
                else
                    EditorApplication.isPlaying = false;
            }

            //----------------------------------------Deck generation----------------------------------------//
            // One valid random deck per leader: faction + neutral units (incl. heroes), and a few specials
            private List<Deck> GenerateDecks(CardDatabase db)
            {
                List<Deck> generated = new List<Deck>();
                foreach (Faction faction in Factions.Playable)
                    foreach (int leader in LeaderIds.ByFaction[faction])
                    {
                        List<Card> pool = db.All.Where(card => card.IsUnit && !CardIds.IsSummoned(card._id)
                            && (card.faction == faction || card.faction == Faction.Neutral)).ToList();
                        List<int> cards = new List<int>();
                        foreach (Card card in pool.OrderBy(x => rnd.Next()))
                        {
                            int copies = 1 + rnd.Next(DeckRules.MaxCopies(card));
                            for (int i = 0; i < copies && cards.Count < 26; i++)
                                cards.Add(card._id);
                        }
                        int[] specials = { CardIds.Decoy, CardIds.Scorch, CardIds.CommanderHorn, CardIds.BitingFrost, CardIds.ImpenetrableFog, CardIds.TorrentialRain, CardIds.ClearWeather,
                            CardIds.SkelligeStorm, CardIds.Mardroeme };
                        foreach (int special in specials.Where(id => DeckRules.Fits(db.Get(id), faction)).OrderBy(x => rnd.Next()).Take(4 + rnd.Next(4)))
                            cards.Add(special);

                        Deck deck = new Deck { Faction = faction, Leader = leader, Name = faction.Code() + " / " + db.Get(leader).name, Cards = cards };
                        List<string> problems = DeckRules.Validate(deck, db);
                        if (problems.Count == 0)
                            generated.Add(deck);
                        else
                            failures.Add("Generated deck invalid (" + deck.Name + "): " + problems[0]);
                    }
                return generated;
            }
        }

        //----------------------------------------Real mouse input----------------------------------------//
        private enum InputScenario
        {
            RedrawSlowClicksThenDone, // two slow clicks (no redraw), a slow double-click on the front card (redraw), a quick
                                      // double-click on a card further away (brings it forward, no redraw), then Done
            RedrawTwice,              // a burst of four clicks on the front card (one redraw only), then the carousel's front
                                      // card (ends it)
            RedrawCarousel,           // carousel: Right arrow, click a side card, Enter redraws it, right-click keeps the hand
            RedrawSpace,              // the Done button shows Space, and Space keeps the hand
            SelectCard,               // hovering at a card's lower edge keeps it raised, left-click selects it, a second
                                      // click deselects it, right-click does nothing
            PauseMenu,                // Escape pauses and resumes; the menu button and Resume do the same
            GiveUpAsked,              // the cursor over the player's deck is a hand; a click asks first; Escape keeps playing
            PassWithSpace,            // a short press of Space does not pass, holding it does
            KeyboardPlay,             // Right raises a hand card (shown on the right), Right again moves on, Enter takes it,
                                      // Down chooses a place, Enter plays it there (Escape puts back a card with no place)
            TouchControls,            // touch (simulated): a held press on the leader, its button and a hand card shows them
                                      // and is no tap; a tap selects a card, another puts it back
            GameSpeed,                // pause menu > Settings > Game: Fast, and after Resume the battle runs faster
            PickerKeys,               // the card picker (the carousel): Right and Left move, Down goes to Skip (Enter there
                                      // asks first, Escape keeps choosing), Up comes back, Enter picks the front card
            DiscardPile,              // a click opens a discard pile in the carousel, Left goes down it, Escape closes it
            LeaderLift,               // a click lifts the leader (out of its slot to the right, its places lit, its crown a
                                      // glowing button), Escape and a click on the empty slot put it back, the crown does
                                      // nothing before, a hand card takes its place, a lit place does not use it, the crown does
            DeckBuilder,              // double-clicks add and remove cards and are refused past the copy and special limits,
                                      // a held press (touch) shows a card, the leader picker, the header's faction arrows,
                                      // the deck dropdown, a card dragged into the deck and out of it, a vertical drag scrolls
            PlayerSheet,              // a click on the opponent's band opens their sheet (their faction's ability, their leader's,
                                      // their cards), Escape closes it; a click on yours opens yours, a click outside closes it
            PinchZoom,                // two fingers (a virtual touchscreen) zoom the board in, the first one on a hand card,
                                      // which is not selected; the zoom-out button and Escape bring the whole board back;
                                      // zoomed, one finger drags the board (from a hand card, which is not selected); the
                                      // lock stops a tap on a hand card, unlocked it selects; a double tap on the table
                                      // zooms in there and out again
            MainMenuSettings,         // hovering moves the highlight, Settings opens, the volume slider works, Escape closes;
                                      // Escape on the menu itself asks to quit, a second one closes the question; a newer
                                      // build in a feed shows the update notice, which opens what's new
            MenuPlay,                 // Play opens the opponent picker; choosing one remembers it and starts the battle
        }

        // Plays part of the game like a person would: a virtual mouse and keyboard at human speed, going through the
        // Input System, the EventSystem and the game's own handlers. Its timeline waits for the game as a person would:
        // it advances at most MaxTick a frame (a stalled editor slows it down instead of firing a move, a press, a release
        // and a check in one frame), and a step that only checks (it sends no input) and fails is tried again every frame
        // until it passes or its patience runs out, the steps after it waiting meanwhile (a slow screen is waited for, and
        // the next clicks never land on the wrong one).
        private class InputCheck
        {
            private const double PressDuration = 0.1;
            private const double MaxTick = 0.05;       // the most the timeline advances in a frame
            private const double CheckPatience = 3;    // how long (real seconds) a failing check step is tried again

            private readonly BattleController c;
            private readonly Action<bool, string> report;
            private readonly List<KeyValuePair<double, Action>> steps = new List<KeyValuePair<double, Action>>();
            private int next;
            private double elapsed, lastTick;          // the timeline's time, and the real time it last advanced
            private double failingSince = -1;          // real time the current check step began failing (-1: it is not)
            private int sent;                          // input events sent so far
            private List<KeyValuePair<bool, string>> results; // the checks of the step running
            private Mouse mouse;
            private readonly List<double> releases = new List<double>();
            private Keyboard keyboard;
            private float savedMasterVolume = -1;
            private bool settingsChanged;
            private InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
            private InputSettings.BackgroundBehavior savedBackgroundBehavior;
            private Transform card;
            private bool savedTouch;
            private bool touchChanged;
            private int savedSpeed = -1;
            private int savedMatchDifficulty = -1;
            private int difficultyBefore;
            private string captionBefore;

            public bool Done { get { return next >= steps.Count; } }

            // The check sets the game's time scale itself (the runner leaves it alone)
            public bool OwnsTimeScale { get; private set; }

            // The times are when the button goes down, in seconds. deck_path: the deck builder's test deck; opponent: the
            // one to choose in the opponent picker.
            public InputCheck(BattleController controller, InputScenario scenario, Action<bool, string> report, string deck_path = null, TextAsset opponent = null)
            {
                c = controller;
                this.report = report;
                lastTick = EditorApplication.timeSinceStartup;

                At(0, Setup);
                InputScenario[] with_hand = { InputScenario.RedrawSlowClicksThenDone, InputScenario.RedrawTwice, InputScenario.RedrawCarousel, InputScenario.RedrawSpace,
                    InputScenario.SelectCard, InputScenario.PassWithSpace, InputScenario.KeyboardPlay, InputScenario.TouchControls, InputScenario.LeaderLift,
                    InputScenario.PinchZoom };
                if (c != null && with_hand.Contains(scenario))
                    At(0, () => check(CardPosition() != Vector2.zero, "the hand has cards to click"));
                if (scenario == InputScenario.RedrawSlowClicksThenDone)
                {
                    // The virtual clicks are timed by editor ticks: when the editor stalls, the real gap between two clicks
                    // is not the planned one, and a check about that gap says so instead of failing
                    int exchanged = 0;
                    Click(0.4, FrontHandCard);
                    Click(1.2, FrontHandCard);
                    At(1.8, () =>
                    {
                        double gap = ClickGap(0, 1);
                        exchanged = c.PlayerInfo.CardsExchanged;
                        check(gap < 0.55 || exchanged == 0, "two clicks " + gap.ToString("0.00") + " s apart on a hand card do not redraw it");
                    });
                    // Slow, but with a margin under the 0.5 s window
                    Click(2.2, FrontHandCard);
                    Click(2.58, FrontHandCard);
                    At(3.2, () =>
                    {
                        double gap = ClickGap(2, 3);
                        check(gap > 0.45 || (c.PlayerInfo.CardsExchanged == exchanged + 1 && c.PlayerInfo.HandList.Count == 10),
                            "a slow double-click (" + gap.ToString("0.00") + " s) on the front card in the hand redraws it");
                        exchanged = c.PlayerInfo.CardsExchanged;
                    });
                    // A quick double-click on a card further away only brings it forward: never a redraw by accident
                    Click(3.5, FarHandCard);
                    Click(3.64, () => farCard != null ? ScreenPoint(farCard) : CardPosition());
                    At(4.1, () =>
                    {
                        if (c.PlayerInfo.CanExchange && farCard != null)
                            check(c.PlayerInfo.CardsExchanged == exchanged && Picker() != null && Picker().FocusedCardId.ToString() == farCard.name,
                                "a quick double-click on a card further away brings it forward without redrawing it");
                    });
                    // Two redraws end the phase on their own (possible after a stall): then there is no Done to click
                    Click(4.4, () => c.PlayerInfo.CanExchange ? DonePosition() : CardPosition());
                    At(4.9, () => check(!c.PlayerInfo.CanExchange, "the Done button ends the redraw"));
                }
                else if (scenario == InputScenario.RedrawTwice)
                {
                    // Clicks 3 and 4 land on the card that replaced the redrawn one: they must not redraw it too
                    Click(0.4, FrontHandCard);
                    Click(0.54, FrontHandCard);
                    Click(0.68, FrontHandCard);
                    Click(0.82, FrontHandCard);
                    At(1.5, () => check(c.PlayerInfo.CardsExchanged == 1 && c.PlayerInfo.HandList.Count == 10, "a burst of four clicks redraws one card only"));
                    At(1.5, () => check(Picker() != null && Picker().CardCount == 10 && c.PlayerInfo.HandList.Contains(Picker().FocusedCardId), "the redraw carousel follows the hand"));
                    Click(1.8, CarouselCard(-1));
                    At(2.4, () => check(c.PlayerInfo.CardsExchanged == 2 && !c.PlayerInfo.CanExchange, "clicking the carousel's front card redraws it (and ends the redraw)"));
                }
                else if (scenario == InputScenario.RedrawCarousel)
                {
                    Key(0.4, UnityEngine.InputSystem.Key.RightArrow);
                    At(0.8, () => check(Picker() != null && Picker().FocusIndex == 1, "the Right arrow brings the next card forward"));
                    Click(1.1, CarouselCard(3));
                    At(1.6, () => check(Picker() != null && Picker().FocusIndex == 3, "clicking a side card brings it forward"));
                    Key(1.9, UnityEngine.InputSystem.Key.Enter);
                    At(2.4, () => check(c.PlayerInfo.CardsExchanged == 1, "Enter redraws the front card"));
                    Click(2.7, CardPosition, MouseButton.Right);
                    At(3.2, () => check(!c.PlayerInfo.CanExchange && c.PlayerInfo.CardsExchanged == 1, "right-click keeps the rest of the hand"));
                }
                else if (scenario == InputScenario.RedrawSpace)
                {
                    At(0.3, () =>
                    {
                        GameObject done = GameObject.Find("Button Done");
                        check(done != null && done.transform.Find("Key Space") != null, "the Done button shows its key, Space, in a cap");
                    });
                    Key(0.5, UnityEngine.InputSystem.Key.Space);
                    At(1.0, () => check(!c.PlayerInfo.CanExchange && c.PlayerInfo.CardsExchanged == 0, "Space keeps the hand"));
                }
                else if (scenario == InputScenario.KeyboardPlay)
                    KeyboardPlay();
                else if (scenario == InputScenario.TouchControls)
                    TouchControls();
                else if (scenario == InputScenario.GameSpeed)
                    GameSpeed();
                else if (scenario == InputScenario.PickerKeys)
                    PickerKeys();
                else if (scenario == InputScenario.DiscardPile)
                    DiscardPile();
                else if (scenario == InputScenario.PinchZoom)
                    PinchZoom();
                else if (scenario == InputScenario.PlayerSheet)
                {
                    // On the band's background, right of the gems (the portrait is the quick chat's in a LAN battle)
                    Func<bool, Func<Vector2>> band = mine => () =>
                    {
                        RectTransform cover = c.PanelOf(mine).Band;
                        Vector3[] corners = new Vector3[4];
                        cover.GetWorldCorners(corners);
                        return new Vector2(Mathf.Lerp(corners[0].x, corners[2].x, 0.62f), Mathf.Lerp(corners[0].y, corners[2].y, 0.25f));
                    };
                    Click(0.3, band(false));
                    At(0.9, () => check(Gwent.Battle.PlayerSheet.IsOpen && !Gwent.Battle.PlayerSheet.ShowsPlayer && Time.timeScale == 0,
                        "a click on the opponent's band opens their sheet (the battle waits)"));
                    At(1.0, () =>
                    {
                        GameObject sheet = GameObject.Find("PlayerSheet");
                        string ability = c.EnemyInfo.Faction.Ability();
                        check(sheet != null && sheet.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text == ability),
                            "the sheet tells their faction's ability (" + ability + ")");
                    });
                    Key(1.2, UnityEngine.InputSystem.Key.Escape);
                    At(1.7, () => check(!Gwent.Battle.PlayerSheet.IsOpen && !PauseMenu.IsOpen && Time.timeScale > 0, "Escape closes it, and only it"));
                    Click(2.0, band(true));
                    At(2.6, () => check(Gwent.Battle.PlayerSheet.IsOpen && Gwent.Battle.PlayerSheet.ShowsPlayer, "a click on your band opens your sheet"));
                    Click(2.9, () => new Vector2(Screen.width * 0.6f, Screen.height * 0.5f));
                    At(3.5, () => check(!Gwent.Battle.PlayerSheet.IsOpen && c.selectedCard == null, "a click outside closes it, and does nothing else"));
                }
                else if (scenario == InputScenario.LeaderLift)
                    LeaderLift();
                else if (scenario == InputScenario.DeckBuilder)
                    DeckBuilderClicks(deck_path);
                else if (scenario == InputScenario.MenuPlay)
                {
                    string entry = opponent != null ? "Opponent " + CardLibrary.ParseDeck(opponent.text, opponent.name).Name : "(no opponent deck)";
                    Click(0.3, Named("Item Play"));
                    At(0.8, () => check(GameObject.Find("OpponentPicker") != null, "Play opens the opponent picker"));
                    Click(1.1, Named(entry));
                    At(1.8, () => check(opponent != null && OpponentDecks.Chosen == opponent.name && LoadingScreen.Showing,
                        "choosing an opponent remembers it and starts the battle, behind the loading screen"));
                }
                else if (scenario == InputScenario.PauseMenu)
                {
                    Key(0.3, UnityEngine.InputSystem.Key.Escape);
                    At(0.8, () => check(PauseMenu.IsOpen && Time.timeScale == 0, "Escape pauses the battle (time stops)"));
                    Key(1.1, UnityEngine.InputSystem.Key.Escape);
                    At(1.6, () => check(!PauseMenu.IsOpen && Time.timeScale > 0, "Escape again resumes it"));
                    At(1.8, () =>
                    {
                        // At the board's top left: the screen's on 16:9, lower on a squarer screen (a batch run's 4:3), the
                        // board fitting it with room above and below
                        Vector2 at = ScreenPoint(GameObject.Find("PauseButton").transform);
                        check(at.x < Screen.width * 0.15f && at.y > Screen.height * 0.75f, "the menu button is at the top left (" + at + ")");
                    });
                    Click(1.9, Named("PauseButton"));
                    At(2.4, () => check(PauseMenu.IsOpen, "the menu button opens the pause menu"));
                    At(2.5, () => check(UIScreens.Find("Item Leave battle") != null && UIScreens.Find("Item Main menu") == null,
                        "while the battle is on, the menu offers Leave battle (not Main menu)"));
                    Click(2.8, Named("Item Leave battle"));
                    At(3.3, () => check(UIScreens.Find("Button No") != null && !c.IsBattleOver, "Leave battle asks first"));
                    Key(3.6, UnityEngine.InputSystem.Key.Escape);
                    At(4.1, () => check(UIScreens.Find("Button No") == null && PauseMenu.IsOpen && !c.IsBattleOver, "Escape stays in the battle, the menu still open"));
                    Click(4.4, Named("Item Resume"));
                    At(4.9, () => check(!PauseMenu.IsOpen && Time.timeScale > 0, "Resume closes it"));
                    // The "?" beside the menu button: How to play over the battle, which waits; Escape closes it
                    Click(5.2, Named("HelpButton"));
                    At(5.8, () => check(UIScreens.Find("Item The battle") != null && Time.timeScale == 0, "the ? opens How to play over the battle (it waits)"));
                    Key(6.1, UnityEngine.InputSystem.Key.Escape);
                    At(6.7, () => check(UIScreens.Find("Item The battle") == null && !PauseMenu.IsOpen && Time.timeScale > 0, "Escape closes it, and the battle goes on"));
                }
                else if (scenario == InputScenario.GiveUpAsked)
                {
                    // The deck's own object is no UI rectangle: aim at the image that takes its clicks
                    Func<Vector2> deck = () => ScreenPoint(UnityEngine.Object.FindAnyObjectByType<DeckGiveUp>().GetComponentsInChildren<Graphic>().First(g => g.raycastTarget).rectTransform);
                    Move(0.3, deck);
                    At(0.7, () => check(GameCursor.Current == GameCursor.State.GiveUp, "the cursor over the player's deck is a hand (give up)"));
                    Click(1.0, deck);
                    At(1.5, () => check(UIScreens.Find("Button No") != null && !c.IsBattleOver, "a click on the deck asks before giving up"));
                    Key(1.8, UnityEngine.InputSystem.Key.Escape);
                    At(2.3, () => check(UIScreens.Find("Button No") == null && !c.IsBattleOver && c.battleState == BattleState.PlayerTurn, "Escape keeps playing"));
                }
                else if (scenario == InputScenario.PassWithSpace)
                {
                    PassButton pass = UnityEngine.Object.FindAnyObjectByType<PassButton>(FindObjectsInactive.Include);
                    float hold = pass != null ? pass.requiredHoldTime : 1f;
                    KeyHold(0.3, UnityEngine.InputSystem.Key.Space, 0.25);
                    At(0.9, () => check(!c.PlayerInfo.hasPassed, "a short press of Space does not pass"));
                    KeyHold(1.2, UnityEngine.InputSystem.Key.Space, hold + 0.4);
                    At(1.2 + hold + 0.8, () => check(c.PlayerInfo.hasPassed, "holding Space passes"));
                }
                else if (scenario == InputScenario.MainMenuSettings)
                {
                    Move(0.3, Named("Item Deck builder"));
                    At(0.6, () => check(Highlighted("Item Deck builder"), "hovering a menu entry highlights it"));
                    Click(0.9, Named("Item Settings"));
                    At(1.4, () => check(SettingsPanel.IsOpen, "clicking Settings opens the settings"));
                    // It opens on Game: the volumes are on the Audio tab
                    Click(1.7, Named("Button Audio"));
                    Click(2.2, MasterVolumeAt(0.25f));
                    At(2.7, () => check(Mathf.Abs(GameSettings.MasterVolume - 0.25f) < 0.06f, "clicking the master volume slider sets the volume"));
                    Key(3.0, UnityEngine.InputSystem.Key.Escape);
                    At(3.5, () => check(!SettingsPanel.IsOpen, "Escape closes the settings"));
                    Click(3.8, Named("Item How to play"));
                    At(4.3, () => check(GameObject.Find("RulesPage") != null, "How to play opens the rules"));
                    Click(4.6, Named("Item Abilities"));
                    At(5.1, () => check(UIScreens.Find("Entry Spy") != null && UIScreens.Find("Entry Your hand") == null, "a rules topic shows its own entries"));
                    Key(5.4, UnityEngine.InputSystem.Key.Escape);
                    At(5.9, () => check(GameObject.Find("RulesPage") == null, "Escape closes the rules"));
                    // Nothing over the menu: Escape asks to quit (Quit not focused), a second Escape answers Cancel
                    Key(6.2, UnityEngine.InputSystem.Key.Escape);
                    At(6.7, () => check(UIScreens.Find("Button Yes") is UnityEngine.UIElements.Button quit && quit.text == "Quit",
                        "Escape on the main menu asks to quit"));
                    Key(7.0, UnityEngine.InputSystem.Key.Escape);
                    At(7.5, () => check(UIScreens.Find("Button Yes") == null && !UIOverlay.HasModal, "a second Escape stays (the question closes)"));
                    // A newer build in a feed (a file here): the menu shows its notice, which opens what's new; Escape closes it
                    At(7.7, () =>
                    {
                        string feed = System.IO.Path.Combine(Application.temporaryCachePath, "autoplay-version.json");
                        System.IO.File.WriteAllText(feed, "{\"build\": " + (BuildInfo.Number + 1) + ", \"commit\": \"0000abcd\", \"date\": \"2026-10-26\", "
                            + "\"notes\": \"### Battles\\n\\n- A **new** thing.\\n- Another one.\"}");
                        Gwent.Updates.UpdateCheck.FeedOverride = "file://" + feed;
                        Gwent.Updates.UpdateCheck.Reset();
                        Gwent.Updates.UpdateCheck.Begin();
                    });
                    At(8.6, () => check(UIScreens.Find("Button Update") is UnityEngine.UIElements.Button notice && notice.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex
                        && notice.text.Contains((BuildInfo.Number + 1).ToString()), "a newer build in the feed shows the menu's notice"));
                    Click(8.9, Named("Button Update"));
                    At(9.5, () => check(UpdateDialog.IsOpen && UIScreens.Find("Notes") is UnityEngine.UIElements.ScrollView notes && notes.contentContainer.childCount == 3,
                        "the notice opens what's new (a heading, two points)"));
                    Key(9.8, UnityEngine.InputSystem.Key.Escape);
                    At(10.3, () => check(!UpdateDialog.IsOpen && !UIOverlay.HasModal, "Escape closes it"));
                    At(10.4, () =>
                    {
                        Gwent.Updates.UpdateCheck.FeedOverride = null;
                        Gwent.Updates.UpdateCheck.Reset();
                    });
                }
                else
                {
                    // A plain unit (when the hand has one), so its right and wrong rows are known
                    At(0.1, PickPlainUnit);
                    // The pointer just above the card's lower edge: the card rises and must stay up, not bob
                    Move(0.3, CardLowerEdge);
                    bool stayed_up = true;
                    foreach (double t in new[] { 0.6, 0.7, 0.8, 0.9 })
                        At(t, () => stayed_up &= card != null && card.GetComponent<CardHover>().isCardUp);
                    At(0.95, () => check(stayed_up, "a card hovered at its lower edge stays raised"));
                    At(0.95, () => check(GameCursor.Current == GameCursor.State.Interact, "the cursor turns gold over a playable hand card"));
                    Click(1.4, CardPosition, MouseButton.Right);
                    At(1.9, () => check(c.selectedCard == null, "right-clicking a hand card does not select it"));
                    Click(2.2, CardPosition);
                    At(2.7, () => check(card != null && c.selectedCard == card.gameObject, "left-clicking a hand card selects it (clicked " + Hovered() + ", " + c.battleState + ")"));
                    // The opponent's side of the unit's row: a wrong place, the card must stay selected in the hand
                    int hand_before = -1;
                    Move(2.9, WrongRow);
                    Move(3.1, WrongRow); // again, in case a real mouse moved in between (the cursor follows the last mouse used)
                    At(3.2, () => { if (plainUnit) check(GameCursor.Current == GameCursor.State.Invalid, "the cursor shows \"no\" over a row the card cannot go to (" + GameCursor.Current + " over " + Hovered() + ", " + c.battleState + ")"); });
                    At(3.25, () => hand_before = c.PlayerInfo.HandList.Count);
                    Click(3.4, WrongRow);
                    At(3.9, () => { if (plainUnit) check(c.selectedCard == card.gameObject && c.PlayerInfo.HandList.Count == hand_before, "clicking a wrong row does not play the card"); });
                    Click(4.2, CardPosition);
                    At(4.7, () => check(c.selectedCard == null, "clicking the selected card again deselects it"));
                }
            }

            //----------------------------------------Scenarios----------------------------------------//
            private void KeyboardPlay()
            {
                BattleKeys keys = c.GetComponent<BattleKeys>();
                GameObject first = null;
                int id = -1, copies = 0, places = 0;
                At(0.1, () => check(keys != null, "the battle has its keyboard controls"));
                if (keys == null)
                    return;
                Key(0.3, UnityEngine.InputSystem.Key.RightArrow);
                At(0.7, () =>
                {
                    first = keys.Focused;
                    CardHover hover = first != null ? first.GetComponent<CardHover>() : null;
                    check(hover != null && hover.isCardUp && hover.bigImage != null, "the Right arrow raises a hand card and shows it on the right");
                });
                Key(1.0, UnityEngine.InputSystem.Key.RightArrow);
                At(1.4, () => check(c.PlayerInfo.HandList.Count < 2 || (first != null && keys.Focused != null && keys.Focused != first && !first.GetComponent<CardHover>().isCardUp),
                    "Right again moves on to the next card, and the first one goes down"));
                Key(1.7, UnityEngine.InputSystem.Key.Enter);
                At(2.1, () =>
                {
                    check(keys.Focused != null && c.selectedCard == keys.Focused && CardShowcase.IsShown, "Enter takes the card: it goes to the showcase");
                    id = c.selectedCard != null ? int.Parse(c.selectedCard.name) : -1;
                });
                Key(2.4, UnityEngine.InputSystem.Key.DownArrow);
                At(2.8, () =>
                {
                    places = c.selectedCard == null ? 0 : c.PlayerField.GetComponent<RowPicker>().highLightedRows.Count + c.EnemyField.GetComponent<RowPicker>().highLightedRows.Count
                        + (c.WeatherField.GetComponent<WeatherSlot>().isWeatherCard ? 1 : 0)
                        + c.PlayerField.GetComponentsInChildren<Transform>().Count(t => t.name == BoardView.DecoyFrame);
                    check(places == 0 || (keys.Place != null && keys.Place.transform.Find("KeyPlace") != null), "Down chooses a place for it, framed (" + places + " places)");
                    copies = c.PlayerInfo.HandList.Count(h => h == id);
                });
                KeyAt(3.1, () => keys.Place != null ? UnityEngine.InputSystem.Key.Enter : UnityEngine.InputSystem.Key.Escape);
                At(3.6, () =>
                {
                    if (places > 0)
                        check(c.selectedCard == null && (c.PlayerInfo.HandList.Count(h => h == id) == copies - 1 || c.IsPresenting), "Enter plays it there");
                    else
                        check(c.selectedCard == null, "Escape puts back a card that has nowhere to go");
                });
            }

            private void TouchControls()
            {
                bool leader_usable = false;
                CardHover leader = null;
                At(0.1, () =>
                {
                    savedTouch = GameInput.SimulateTouch;
                    touchChanged = true;
                    GameInput.SimulateTouch = true;
                    leader_usable = c.CanUseLeader(true);
                    Transform holder = c.PlayerField.transform.Find("Leader/Holder");
                    leader = holder != null ? holder.GetComponentInChildren<CardHover>() : null;
                    check(leader != null, "the leader card is on the board");
                });
                Hold(0.4, () => leader != null ? ScreenPoint(leader.transform) : Vector2.zero, 0.8);
                At(1.0, () => check(leader != null && leader.bigImage != null, "a finger held on the leader card shows it on the right (" + PressState(leader) + ")"));
                At(1.5, () => check(leader != null && leader.bigImage == null && c.CanUseLeader(true) == leader_usable && !c.LeaderSelected, "lifting the finger closes it, and the leader is neither chosen nor used"));
                Hold(1.8, () => ScreenPoint(c.PlayerLeader.Button.transform), 0.8);
                At(2.4, () => check(GameObject.Find("Tooltip") != null, "a finger held on the leader's button shows its state"));
                At(2.9, () => check(GameObject.Find("Tooltip") == null && c.CanUseLeader(true) == leader_usable, "lifting the finger hides it, and the leader is not used"));
                Hold(3.2, CardPosition, 0.8);
                At(3.8, () => check(card != null && card.GetComponent<CardHover>().bigImage != null, "a finger held on a hand card shows it on the right (" + PressState(card) + ")"));
                At(4.3, () => check(card != null && card.GetComponent<CardHover>().bigImage == null && c.selectedCard == null, "lifting the finger closes it, and the card is not selected"));
                Click(4.6, CardPosition);
                At(5.1, () => check(card != null && c.selectedCard == card.gameObject, "a tap selects a card"));
                Click(5.4, CardPosition);
                At(5.9, () => check(c.selectedCard == null, "a second tap puts it back"));
            }

            private void GameSpeed()
            {
                OwnsTimeScale = true;
                At(0.1, () =>
                {
                    savedMatchDifficulty = (int)GameSettings.AiDifficulty;
                    savedSpeed = GameSettings.GameSpeed;
                    GameSettings.GameSpeed = 1;
                    Time.timeScale = GameSettings.GameTimeScale;
                });
                Key(0.3, UnityEngine.InputSystem.Key.Escape);
                Click(0.9, Named("Item Settings"));
                Click(1.5, Named("Button Game"));
                Click(2.0, () =>
                {
                    Stepper stepper = SettingsStepper("Row Game speed");
                    check(stepper != null, "the settings' Game tab shows the game speed");
                    return stepper != null ? ScreenPoint(stepper.Right) : Vector2.zero;
                });
                At(2.5, () =>
                {
                    Stepper stepper = SettingsStepper("Row Game speed");
                    check(GameSettings.GameSpeed == 2, "its right arrow sets it to " + GameSettings.GameSpeedNames[2] + " (speed " + GameSettings.GameSpeed
                        + ", stepper " + (stepper != null ? stepper.Index.ToString() : "gone") + ", over " + Hovered() + ")");
                });
                // The opponent's difficulty, on the same tab: one step of its arrows changes it and the caption under it
                Click(2.7, () =>
                {
                    Stepper stepper = SettingsStepper("Row Difficulty");
                    UnityEngine.UIElements.Label caption = SettingsCaption("Row Difficulty");
                    check(stepper != null && caption != null && stepper.Index == (int)GameSettings.AiDifficulty,
                        "the settings' Game tab shows the opponent's difficulty (" + GameSettings.AiDifficulty + ")");
                    if (stepper == null || caption == null)
                        return Vector2.zero;
                    difficultyBefore = stepper.Index;
                    captionBefore = caption.text;
                    return ScreenPoint(stepper.Index > 0 ? stepper.Left : stepper.Right);
                });
                At(3.2, () =>
                {
                    Stepper stepper = SettingsStepper("Row Difficulty");
                    UnityEngine.UIElements.Label caption = SettingsCaption("Row Difficulty");
                    string about = caption != null ? caption.text : "";
                    int shown = stepper != null ? stepper.Index : -1;
                    check(shown >= 0 && shown != difficultyBefore && (int)GameSettings.AiDifficulty == shown && stepper.Label.text == GameSettings.AiDifficultyNames[shown],
                        "its arrow changes the difficulty (" + GameSettings.AiDifficulty + ", stepper " + shown + ", over " + Hovered() + ")");
                    check(about.Length > 0 && about != captionBefore, "the caption follows the difficulty (\"" + about + "\")");
                });
                Key(3.5, UnityEngine.InputSystem.Key.Escape);
                Click(4.0, Named("Item Resume"));
                At(4.5, () => check(!PauseMenu.IsOpen && GameSettings.GameTimeScale > 1.5f && Mathf.Approximately(Time.timeScale, GameSettings.GameTimeScale),
                    "after Resume the battle runs at that speed (time scale " + Time.timeScale + ")"));
            }

            // UI Toolkit's Q (an extension method: this file keeps uGUI's names, so it does not import UnityEngine.UIElements)
            private static T Query<T>(UnityEngine.UIElements.VisualElement root, string name = null, string class_name = null) where T : UnityEngine.UIElements.VisualElement
            {
                return UnityEngine.UIElements.UQueryExtensions.Q<T>(root, name, class_name);
            }

            // A settings row's stepper and the caption under its label (UI Toolkit)
            private static Stepper SettingsStepper(string row)
            {
                UnityEngine.UIElements.VisualElement element = UIScreens.Find(row);
                return element != null ? Query<Stepper>(element) : null;
            }

            private static UnityEngine.UIElements.Label SettingsCaption(string row)
            {
                UnityEngine.UIElements.VisualElement element = UIScreens.Find(row);
                return element != null ? Query<UnityEngine.UIElements.Label>(element, "Caption") : null;
            }

            private void LeaderLift()
            {
                Func<GameObject> leader_object = () =>
                {
                    Transform holder = c.PlayerField.transform.Find("Leader/Holder");
                    CardSelect leader = holder != null ? holder.GetComponentInChildren<CardSelect>() : null;
                    return leader != null ? leader.gameObject : null;
                };
                Func<Vector2> leader_card = () => leader_object() != null ? ScreenPoint(leader_object().transform) : Vector2.zero;
                Func<Vector2> crown = () => ScreenPoint(c.PlayerLeader.Button.transform);
                // The card's slot is empty while it is lifted (the card shows on the right)
                Func<bool> slot_empty = () =>
                {
                    CanvasGroup group = leader_object() != null ? leader_object().GetComponent<CanvasGroup>() : null;
                    return group != null && group.alpha < 0.01f;
                };
                Func<bool> crown_button = () => c.PlayerLeader.Button.interactable;
                int leader_id = c.PlayerInfo.Leader;
                int targets = LeaderIds.WeatherPickRow(leader_id) != null || LeaderIds.HornRow(leader_id) != null || LeaderIds.ScorchRow(leader_id) != null
                    || leader_id == LeaderIds.FoltestLordCommander || leader_id == LeaderIds.EredinBringerOfDeath || leader_id == LeaderIds.EmhyrTheRelentless ? 1
                    : leader_id == LeaderIds.FrancescaHopeOfTheAenSeidhe || leader_id == LeaderIds.CrachAnCraite ? 2 : 0;

                At(0.2, () => check(!crown_button(), "the crown is no button before the leader is lifted"));
                Click(0.4, leader_card);
                At(0.9, () => check(c.LeaderSelected && CardShowcase.IsShown && slot_empty() && crown_button() && c.CanUseLeader(true) && c.LeaderTargets.Count == targets,
                    "a click lifts the leader: out of its slot (empty: " + slot_empty() + ") to the right, its crown a button (" + crown_button() + "), "
                    + targets + " place(s) lit (" + c.LeaderTargets.Count + "), not used yet"));
                Key(1.2, UnityEngine.InputSystem.Key.Escape);
                At(1.8, () => check(!c.LeaderSelected && !slot_empty() && !crown_button() && c.CanUseLeader(true) && !PauseMenu.IsOpen,
                    "Escape puts it back in its slot, and the crown is no button again"));
                Click(2.1, crown);
                At(2.5, () => check(!c.LeaderSelected && c.CanUseLeader(true), "a click on the crown alone neither lifts nor uses it"));
                Click(2.8, leader_card);
                Click(3.3, leader_card);
                At(3.9, () => check(!c.LeaderSelected && !slot_empty() && c.CanUseLeader(true), "a click on its empty slot puts it back"));
                Click(4.2, leader_card);
                Click(4.7, CardPosition);
                At(5.2, () => check(!c.LeaderSelected && card != null && c.selectedCard == card.gameObject && c.CanUseLeader(true), "a hand card takes its place"));
                Click(5.5, CardPosition);
                At(6.0, () => check(c.selectedCard == null, "(and goes back)"));
                Click(6.3, leader_card);
                Click(6.8, () =>
                {
                    GameObject place = c.LeaderTargets.Count > 0 ? c.LeaderTargets[0] : null;
                    return place != null ? ScreenPoint(place.transform) : leader_card();
                });
                if (targets > 0)
                    At(7.3, () => check(c.LeaderSelected && c.CanUseLeader(true), "a click on a place it lit does not use it (the crown does)"));
                else
                    Click(7.1, leader_card); // put back by its slot, lifted again
                Click(7.6, crown);
                At(8.2, () => check(!c.LeaderSelected && !c.PlayerInfo.canLeader && !crown_button(), "a click on the glowing crown uses the ability"));
            }

            // The picker is The Witcher 3's carousel under a title band
            private void PickerKeys()
            {
                BattlePickers panel = c.Panel.GetComponent<BattlePickers>();
                Func<GameObject> selected = () => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                At(0.1, () =>
                {
                    TextMeshProUGUI title = panel.Picker != null ? panel.Picker.transform.parent.Find("Band").GetComponentInChildren<TextMeshProUGUI>() : null;
                    check(panel.Picker != null && panel.Picker.CardCount == panel.Cards.Count && title != null && title.text.Length > 0,
                        "the picker shows its cards in the carousel, under a title (\"" + (title != null ? title.text : "") + "\")");
                });
                Key(0.3, UnityEngine.InputSystem.Key.RightArrow);
                At(0.7, () => check(panel.Picker != null && panel.Picker.FocusIndex == 1, "the Right arrow brings the next card to the front"));
                Key(1.0, UnityEngine.InputSystem.Key.LeftArrow);
                At(1.4, () => check(panel.Picker != null && panel.Picker.FocusIndex == 0, "Left brings back the first"));
                Key(1.7, UnityEngine.InputSystem.Key.DownArrow);
                At(2.0, () => check(panel.Picker != null && selected() == panel.Picker.BandButton.gameObject, "Down goes to the band's ×"));
                // Giving the choice up asks first (closing a look does not): Enter on the × asks, Escape keeps choosing
                double later = 0;
                if (panel.Picker != null && panel.Picker.Picks)
                {
                    Key(2.3, UnityEngine.InputSystem.Key.Enter);
                    At(2.8, () => check(UIScreens.Find("Button No") != null && c.pendingAction == PendingAction.PanelChoice, "Skip asks before the choice is given up"));
                    Key(3.1, UnityEngine.InputSystem.Key.Escape);
                    At(3.6, () => check(UIScreens.Find("Button No") == null && panel.Picker != null && c.pendingAction == PendingAction.PanelChoice, "Escape keeps choosing"));
                    later = 1.6;
                }
                Key(2.3 + later, UnityEngine.InputSystem.Key.UpArrow);
                At(2.6 + later, () => check(panel.Picker != null && selected() != panel.Picker.BandButton.gameObject, "Up comes back to the cards"));
                Key(2.9 + later, UnityEngine.InputSystem.Key.Enter);
                At(3.4 + later, () => check(!c.Panel.activeInHierarchy && c.pendingAction != PendingAction.PanelChoice, "Enter picks the front card (a look at the opponent's hand closes)"));
            }

            // A discard pile, opened by a click, in the carousel with its top card in front; the battle waits
            private void DiscardPile()
            {
                bool mine = c.PlayerInfo.DiscardList.Count >= 2;
                List<int> pile = mine ? c.PlayerInfo.DiscardList : c.EnemyInfo.DiscardList;
                int count = pile.Count;
                Click(0.3, () => ScreenPoint((mine ? c.PlayerField : c.EnemyField).transform.Find("Discard")));
                At(0.8, () =>
                {
                    CardPicker picker = UnityEngine.Object.FindAnyObjectByType<CardPicker>();
                    check(DiscardViewer.IsOpen && Time.timeScale == 0 && picker != null && picker.CardCount == count && picker.FocusIndex == count - 1,
                        "a click on a discard pile shows it in the carousel, its top card in front, and the battle waits");
                });
                Key(1.1, UnityEngine.InputSystem.Key.LeftArrow);
                At(1.5, () =>
                {
                    CardPicker picker = UnityEngine.Object.FindAnyObjectByType<CardPicker>();
                    check(picker != null && picker.FocusIndex == count - 2, "the Left arrow goes down the pile");
                });
                Key(1.8, UnityEngine.InputSystem.Key.Escape);
                At(2.3, () => check(!DiscardViewer.IsOpen && Time.timeScale > 0 && !PauseMenu.IsOpen, "Escape puts it away, and the battle goes on"));
            }

            // The test deck is shown; the collection lists the deck's faction (and neutrals)
            private void DeckBuilderClicks(string deck_path)
            {
                DeckBuilder dc = DeckBuilder.Instance;
                CardGrid browse = UnityEngine.Object.FindObjectsByType<CardGrid>().FirstOrDefault(m => !m.CompareTag("Player"));
                At(0, () => check(dc != null && browse != null, "the deck builder shows the deck and the collection"));
                if (dc == null || browse == null)
                    return;
                Func<int> count = () => dc.my_deck.Cards.Count;
                Func<int> saved = () => { Deck deck = DeckStorage.Load(deck_path); return deck != null ? deck.Cards.Count : -1; };
                int before = 0;

                // Each card once, with its count (The Witcher 3): the copies in the deck, the copies left in the collection
                int added = -1;
                At(0.1, () =>
                {
                    before = count();
                    int kinds = dc.my_deck.Cards.Distinct().Count(id => dc.deckCollection.toggleBtn.isOn || dc.Db.Get(id).faction != Faction.Neutral);
                    check(Shown(dc.deckCollection).Count == kinds && BadgeOf(dc.deckCollection, CardIds.BitingFrost) == 3,
                        "the deck shows each card once with its count (Biting Frost x" + BadgeOf(dc.deckCollection, CardIds.BitingFrost) + ")");
                });
                // Add, then two cards that cannot be added: a hero already in the deck (not offered), an 11th special
                DoubleClickAt(0.4, () =>
                {
                    Vector2 at = AddableCard();
                    added = addTarget != null ? addTarget.GetComponent<CardRef>().Card._id : -1;
                    return at;
                });
                At(1.0, () =>
                {
                    check(count() == before + 1 && saved() == before + 1, "double-clicking a card in the collection adds it to the deck (and saves it)");
                    int left = added >= 0 ? DeckRules.MaxCopies(dc.Db.Get(added)) - 1 : -1;
                    check(added >= 0 && BadgeOf(browse, added) == left && SpentIn(browse, added) == (left == 0),
                        "the collection then counts one copy fewer (" + left + " left" + (left == 0 ? ": still shown, greyed" : "") + ")");
                });
                Click(1.3, () => ScreenPoint(browse.currentSprites[4].transform));
                At(1.8, () => check(Shown(browse).Count > 0 && Shown(browse).All(id => BadgeOf(browse, id) == 0 && SpentIn(browse, id)),
                    "with every hero in the deck, the collection shows each at 0, greyed (" + Shown(browse).Count + " shown)"));
                Click(2.1, () => ScreenPoint(browse.currentSprites[CardGrid.WeatherTab].transform));
                At(2.6, () => check(Shown(browse).Count > 0 && Shown(browse).All(id => dc.Db.Get(id).row == Row.Weather), "the Weather tab lists the weather cards only"));
                Click(2.9, () => ScreenPoint(browse.currentSprites[CardGrid.SpecialTab].transform));
                DoubleClickAt(3.4, FirstOpenCardOf(browse));
                At(4.0, () => check(count() == before + 1 && ToastText().Contains("special cards") && Shown(browse).All(id => dc.Db.Get(id).IsSpecial && dc.Db.Get(id).row != Row.Weather),
                    "the Specials tab lists the other special cards, and an 11th special card is refused (\"" + ToastText() + "\")"));
                Click(4.3, () => ScreenPoint(browse.currentSprites[0].transform));

                // Remove from the deck's list
                DoubleClickAt(4.6, FirstCardOf(dc.deckCollection));
                At(5.2, () => check(count() == before && saved() == before, "double-clicking a card of the deck takes it out (and saves it)"));

                // Touch (simulated): a finger held on a card shows it large, and adds nothing
                At(5.4, () =>
                {
                    savedTouch = GameInput.SimulateTouch;
                    touchChanged = true;
                    GameInput.SimulateTouch = true;
                });
                Hold(5.7, FirstCardOf(browse), 0.8);
                At(6.3, () => check(browse.cardDetails.activeSelf, "(touch) a finger held on a card shows it large (" + (browse.cardsArea.transform.childCount > 0 ? PressState(browse.cardsArea.transform.GetChild(0)) : "no card") + ")"));
                At(6.8, () => check(!browse.cardDetails.activeSelf && count() == before, "(touch) lifting the finger puts it away, and the card is not added"));
                At(6.9, () => GameInput.SimulateTouch = savedTouch);

                // The leader: its carousel, the deck's leader in front; Escape leaves it, a side leader brought to the front
                // and clicked becomes the deck's (saved)
                int leader = -1, first_leader = -1;
                Click(7.2, () => ScreenPoint(dc.deckSummary.leaderGo.transform));
                At(7.6, () =>
                {
                    first_leader = dc.my_deck.Leader;
                    check(LeaderChoice.IsOpen && LeaderChoice.Picker.CardCount == LeaderIds.ByFaction[Faction.Monsters].Length
                        && LeaderChoice.Picker.FocusedCardId == first_leader,
                        "a click on the leader opens the faction's leaders in the carousel, the deck's in front");
                });
                Key(7.7, UnityEngine.InputSystem.Key.Escape);
                At(8.0, () => check(!LeaderChoice.IsOpen && dc.my_deck.Leader == first_leader && DeckStorage.Load(deck_path).Leader == first_leader,
                    "Escape closes the leaders and leaves the deck's leader as it was"));
                Click(8.2, () => ScreenPoint(dc.deckSummary.leaderGo.transform));
                Click(8.6, () =>
                {
                    CardCarousel carousel = LeaderChoice.Picker;
                    int side = carousel != null ? (carousel.FocusIndex + 1) % carousel.CardCount : -1;
                    return carousel != null && side != carousel.FocusIndex ? ScreenPoint(carousel.CardAt(side)) : Vector2.zero;
                });
                At(9.0, () =>
                {
                    leader = LeaderChoice.Picker != null ? LeaderChoice.Picker.FocusedCardId : -1;
                    check(leader >= 0 && leader != first_leader && LeaderChoice.IsOpen && dc.my_deck.Leader == first_leader,
                        "a click on a side leader brings it to the front, and chooses nothing yet");
                });
                Click(9.7, () => LeaderChoice.Picker != null ? ScreenPoint(LeaderChoice.Picker.CardAt(LeaderChoice.Picker.FocusIndex)) : Vector2.zero);
                At(10.2, () =>
                {
                    Deck deck = DeckStorage.Load(deck_path);
                    check(dc.my_deck.Leader == leader && deck != null && deck.Leader == leader && !LeaderChoice.IsOpen,
                        "a click on the front leader makes it the deck's leader (saved) and closes the carousel");
                });

                // The header's arrows: the previous faction (its cards, its perk, its decks), then back
                Click(10.5, () => ScreenPoint(dc.deckCollection.header.previousButton.transform));
                At(11.8, () => check(ShowsFaction(dc, browse, Faction.Scoiatael), "the header's left arrow switches to the previous faction: its cards, its ability and its decks"));
                Click(12.1, () => ScreenPoint(dc.deckCollection.header.nextButton.transform));
                At(13.4, () => check(ShowsFaction(dc, browse, Faction.Monsters), "the right arrow switches back"));

                // The deck dropdown
                Click(13.7, () => ScreenPoint(dc.deckSummary.deckPicker.transform));
                Click(14.3, () =>
                {
                    GameObject list = GameObject.Find("Dropdown List");
                    string wanted = DeckStorage.Load(deck_path).Name;
                    Toggle item = list != null ? list.GetComponentsInChildren<Toggle>().FirstOrDefault(t => t.name.EndsWith(": " + wanted)) : null;
                    check(item != null, "the deck dropdown lists the test deck");
                    return item != null ? ScreenPoint(item.transform) : Vector2.zero;
                });
                At(14.9, () =>
                {
                    string wanted = DeckStorage.Load(deck_path).Name;
                    check(DeckStorage.SamePath(dc.currentDeckPath, deck_path) && dc.my_deck.Name == wanted, "picking the deck in the dropdown shows it");
                    check(DeckStorage.SamePath(PlayerPrefs.GetString("playerDeckPath", ""), deck_path) && dc.BattleDeckStatus == "Battle deck: " + wanted,
                        "the valid deck picked becomes the battle deck, as the status line says (\"" + dc.BattleDeckStatus + "\")");
                    before = count();
                });

                // Dragging: a card of the collection onto the deck's panel goes in; one of the deck onto the collection's
                // goes out; one dropped back on its own grid stays as it was; a drag up the collection scrolls it
                int dragged = -1;
                Drag(15.2, () =>
                {
                    Vector2 at = AddableCard();
                    dragged = addTarget != null ? addTarget.GetComponent<CardRef>().Card._id : -1;
                    return at;
                }, () => ColumnPoint(dc.deckCollection, 60));
                At(16.1, () => check(dragged >= 0 && count() == before + 1 && saved() == before + 1 && dc.my_deck.Cards.Contains(dragged),
                    "a card dragged from the collection into the deck's column (dropped above its cards, on its title) goes in (and is saved)"));
                Drag(16.4, FirstCardOf(dc.deckCollection), () => ColumnPoint(browse, -200));
                At(17.3, () => check(count() == before && saved() == before, "a card dragged from the deck onto the collection's cards goes out (and is saved)"));
                Drag(17.6, FirstCardOf(browse), () => FirstCardOf(browse)() + new Vector2(-120, 0));
                At(18.5, () => check(count() == before && CardMotion.Layer.childCount == 0, "a card dropped back on its own grid stays as it was, its copy gone"));
                ScrollRect scroll = browse.cardsArea.transform.parent.GetComponent<ScrollRect>();
                float scrolled = 0;
                At(18.7, () => scrolled = scroll.verticalNormalizedPosition);
                Drag(18.8, FirstCardOf(browse), () => FirstCardOf(browse)() + new Vector2(0, 220));
                At(19.7, () => check(count() == before && (scroll.content.rect.height <= ((RectTransform)scroll.transform).rect.height + 1 || scroll.verticalNormalizedPosition != scrolled),
                    "a drag up the collection scrolls it and adds nothing (" + scrolled.ToString("0.00") + " to " + scroll.verticalNormalizedPosition.ToString("0.00") + ")"));
            }

            // What the pointer is on, and the long press of an element (for failure messages)
            private string PressState(Component target)
            {
                LongPress press = target != null ? target.GetComponent<LongPress>() : null;
                if (press == null)
                    return "no long press on it, over " + Hovered();
                System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object data = typeof(LongPress).GetField("press", flags).GetValue(press);
                return "pressed " + (data != null) + ", showing " + typeof(LongPress).GetField("showing", flags).GetValue(press) + ", touch " + GameInput.IsTouch
                    + ", showcase " + CardShowcase.IsShown + ", over " + Hovered() + (c != null ? ", " + c.battleState : "");
            }

            private static bool ShowsFaction(DeckBuilder dc, CardGrid browse, Faction faction)
            {
                return dc.deckCollection.faction == faction && browse.faction == faction && dc.my_deck.Faction == faction
                    && dc.deckCollection.factionPerk.text == faction.Ability()
                    && dc.deckSummary.myPaths.All(p => OpponentDecks.IsPreset(p)
                        ? DeckStorage.Load(p).Faction == faction // the faction's presets
                        : DeckStorage.SamePath(Path.GetDirectoryName(p), DeckStorage.FactionFolder(faction)));
            }

            // The card ids a card list shows, and the count on one of them (-1 when not shown)
            private static List<int> Shown(CardGrid collection)
            {
                List<int> ids = new List<int>();
                foreach (Transform card in collection.cardsArea.transform)
                    ids.Add(card.GetComponent<CardRef>().Card._id);
                return ids;
            }

            private static int BadgeOf(CardGrid collection, int id)
            {
                foreach (Transform card in collection.cardsArea.transform)
                    if (card.GetComponent<CardRef>().Card._id == id)
                    {
                        Transform badge = card.Find(CardGrid.CountBadge);
                        int count;
                        return badge != null && int.TryParse(badge.GetComponentInChildren<TextMeshProUGUI>().text.TrimStart('x'), out count) ? count : -1;
                    }
                return -1;
            }

            private Transform addTarget;

            // Whether the collection shows that card spent: all its copies in the deck, greyed under its veil
            private static bool SpentIn(CardGrid collection, int id)
            {
                foreach (Transform card in collection.cardsArea.transform)
                    if (card.GetComponent<CardRef>().Card._id == id)
                        return CardGrid.IsSpent(card.gameObject);
                return false;
            }

            // A point in a grid's column: its card panel's middle across, that far above the panel's top (below it, negative)
            private static Vector2 ColumnPoint(CardGrid grid, float above)
            {
                RectTransform panel = (RectTransform)grid.cardsArea.GetComponentInParent<ScrollRect>().transform;
                Vector3[] corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                Canvas canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 bottom_left = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 top_right = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                return new Vector2((bottom_left.x + top_right.x) / 2, Mathf.Clamp(top_right.y + above, 5, Screen.height - 5));
            }

            // One of the collection's first cards the deck has room for (not a special: their limit is checked apart)
            private Vector2 AddableCard()
            {
                DeckBuilder dc = DeckBuilder.Instance;
                CardGrid browse = UnityEngine.Object.FindObjectsByType<CardGrid>().FirstOrDefault(m => !m.CompareTag("Player"));
                Transform target = null;
                for (int i = 0; browse != null && i < Mathf.Min(4, browse.cardsArea.transform.childCount) && target == null; i++)
                {
                    Transform t = browse.cardsArea.transform.GetChild(i);
                    Card shown = dc.Db.Get(t.GetComponent<CardRef>().Card._id);
                    if (!shown.IsSpecial && dc.my_deck.Cards.Count(x => x == shown._id) < DeckRules.MaxCopies(shown))
                        target = t;
                }
                check(target != null, "the collection's first cards include one the deck has room for");
                addTarget = target;
                return target != null ? ScreenPoint(target) : Vector2.zero;
            }

            // The first card of the collection that is not spent (one the deck could still take)
            private Func<Vector2> FirstOpenCardOf(CardGrid collection)
            {
                return () =>
                {
                    Transform open = collection.cardsArea.transform.Cast<Transform>().FirstOrDefault(card => !CardGrid.IsSpent(card.gameObject));
                    check(open != null, "the collection shows a card the deck could still take");
                    return open != null ? ScreenPoint(open) : Vector2.zero;
                };
            }

            // The first card of a card list (the deck's or the collection's), where it is when the click happens
            private Func<Vector2> FirstCardOf(CardGrid collection)
            {
                return () =>
                {
                    Transform area = collection.cardsArea.transform;
                    check(area.childCount > 0, "the " + (collection.CompareTag("Player") ? "deck" : "collection") + " shows cards");
                    return area.childCount > 0 ? ScreenPoint(area.GetChild(0)) : Vector2.zero;
                };
            }

            public void Tick()
            {
                double now = EditorApplication.timeSinceStartup;
                if (failingSince < 0) // a check step being tried again holds the timeline
                    elapsed += Math.Min(now - lastTick, MaxTick);
                lastTick = now;
                while (next < steps.Count && elapsed >= steps[next].Key)
                {
                    if (!Run(steps[next].Value, now))
                        return; // tried again next frame
                    next++;
                }
            }

            // Runs a step, its checks held until it is done; false when it only checks, fails and is to be tried again
            private bool Run(Action step, double now)
            {
                int sent_before = sent;
                results = new List<KeyValuePair<bool, string>>();
                step();
                List<KeyValuePair<bool, string>> got = results;
                results = null;
                if (got.Exists(result => !result.Key) && sent == sent_before)
                {
                    if (failingSince < 0)
                        failingSince = now;
                    if (now - failingSince < CheckPatience)
                        return false;
                }
                failingSince = -1;
                foreach (KeyValuePair<bool, string> result in got)
                    report(result.Key, result.Value);
                return true;
            }

            // A check: held while its step runs (Run decides), told at once otherwise
            private void check(bool ok, string what)
            {
                if (results != null)
                    results.Add(new KeyValuePair<bool, string>(ok, what));
                else
                    report(ok, what);
            }

            public void Teardown()
            {
                next = steps.Count;
                if (mouse != null)
                {
                    InputSystem.RemoveDevice(mouse);
                    mouse = null;
                }
                if (keyboard != null)
                {
                    InputSystem.RemoveDevice(keyboard);
                    keyboard = null;
                }
                if (touchscreen != null)
                {
                    InputSystem.RemoveDevice(touchscreen);
                    touchscreen = null;
                }
                if (savedMasterVolume >= 0 && !Mathf.Approximately(GameSettings.MasterVolume, savedMasterVolume))
                    GameSettings.MasterVolume = savedMasterVolume;
                if (touchChanged)
                    GameInput.SimulateTouch = savedTouch;
                if (savedSpeed >= 0)
                    GameSettings.GameSpeed = savedSpeed;
                if (savedMatchDifficulty >= 0)
                    GameSettings.AiDifficulty = (AiDifficulty)savedMatchDifficulty;
                if (settingsChanged)
                {
                    InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
                    InputSystem.settings.backgroundBehavior = savedBackgroundBehavior;
                    settingsChanged = false;
                }
            }

            // Steps run in time order (a check during a hold comes before its release), same times in the order given
            private void At(double time, Action action)
            {
                int index = steps.FindIndex(step => step.Key > time);
                steps.Insert(index < 0 ? steps.Count : index, new KeyValuePair<double, Action>(time, action));
            }

            // Finds the target first (a step of its own: it waits for the target, its checks tried again until it is on
            // screen), moves onto it, then presses and releases the button
            private void Click(double time, Func<Vector2> position, MouseButton button = MouseButton.Left)
            {
                Vector2 at = Vector2.zero;
                At(time - 0.05, () => at = position());
                At(time - 0.05, () => Send(at, button, false));
                At(time, () => Send(at, button, true));
                At(time + PressDuration, () => { Send(at, button, false); releases.Add(EditorApplication.timeSinceStartup); });
            }

            // Holds the left button down (a finger held on the screen, with touch controls)
            private void Hold(double time, Func<Vector2> position, double duration)
            {
                Vector2 at = Vector2.zero;
                At(time - 0.05, () => at = position());
                At(time - 0.05, () => Send(at, MouseButton.Left, false));
                At(time, () => Send(at, MouseButton.Left, true));
                At(time + duration, () => Send(at, MouseButton.Left, false));
            }

            // Two clicks on the same spot, as quick as a person's double-click
            private void DoubleClickAt(double time, Func<Vector2> position)
            {
                Vector2 at = Vector2.zero;
                At(time - 0.05, () => at = position());
                At(time - 0.05, () => Send(at, MouseButton.Left, false));
                foreach (double press in new[] { time, time + 0.2 })
                {
                    At(press, () => Send(at, MouseButton.Left, true));
                    At(press + PressDuration, () => Send(at, MouseButton.Left, false));
                }
            }

            // Real time between two clicks of this check (counted from 0), as they were sent
            private double ClickGap(int first, int second)
            {
                return second < releases.Count ? releases[second] - releases[first] : 0;
            }

            // Presses on one point, moves to another in steps (as a hand does) and lets go there
            private void Drag(double time, Func<Vector2> from, Func<Vector2> to, double seconds = 0.4)
            {
                Vector2 a = Vector2.zero, b = Vector2.zero;
                At(time - 0.05, () => { a = from(); b = to(); });
                At(time - 0.05, () => Send(a, MouseButton.Left, false));
                At(time, () => Send(a, MouseButton.Left, true));
                for (int i = 1; i <= 8; i++)
                {
                    float k = i / 8f;
                    At(time + seconds * k, () => Send(Vector2.Lerp(a, b, k), MouseButton.Left, true));
                }
                At(time + seconds + 0.05, () => Send(b, MouseButton.Left, false));
            }

            private void Move(double time, Func<Vector2> position)
            {
                Vector2 at = Vector2.zero;
                At(time, () => at = position());
                At(time, () => Send(at, MouseButton.Left, false));
            }

            private void KeyHold(double time, Key key, double duration)
            {
                At(time, () => SendKeys(new KeyboardState(key)));
                At(time + duration, () => SendKeys(new KeyboardState()));
            }

            // A key chosen when it is pressed
            private void KeyAt(double time, Func<Key> key)
            {
                At(time, () => SendKeys(new KeyboardState(key())));
                At(time + PressDuration, () => SendKeys(new KeyboardState()));
            }

            private void Key(double time, Key key)
            {
                At(time, () => SendKeys(new KeyboardState(key)));
                At(time + PressDuration, () => SendKeys(new KeyboardState()));
            }
            private void Setup()
            {
                // Input reaches the game without the Game view having focus (batch mode has none). The settings are changed
                // in place and restored afterwards: replacing the settings object would destroy the Input System's own one.
                InputSettings settings = InputSystem.settings;
                savedEditorBehavior = settings.editorInputBehaviorInPlayMode;
                savedBackgroundBehavior = settings.backgroundBehavior;
                settingsChanged = true;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                mouse = InputSystem.AddDevice<Mouse>("AutoplayMouse");
                keyboard = InputSystem.AddDevice<Keyboard>("AutoplayKeyboard");
                savedMasterVolume = GameSettings.MasterVolume;

                InputSystemUIInputModule module = EventSystem.current != null ? EventSystem.current.currentInputModule as InputSystemUIInputModule : null;
                check(module != null, "the scene uses the Input System UI module");
                if (module != null && module.actionsAsset != null)
                {
                    module.actionsAsset.Disable();
                    module.actionsAsset.Enable();
                }
            }

            // Two fingers part on the board, the first one starting on a hand card: the board zooms in under them, and no
            // card is selected or played (their release lands on the pinch's cover); the zoom-out button brings it back;
            // a second pinch, then Escape: back too, and the pause menu stays closed. Zoomed again: one finger dragged from
            // a hand card moves the board and selects nothing; locked, a tap on a hand card does nothing; unlocked, it
            // selects it (Escape puts it back); zoomed out, the lock is off. A double tap on the table zooms in, another out.
            private void PinchZoom()
            {
                int hand = 0;
                Vector2 start = Vector2.zero;
                At(0.2, () =>
                {
                    touchscreen = InputSystem.AddDevice<Touchscreen>("AutoplayTouch");
                    hand = c.PlayerInfo.HandList.Count;
                    start = CardPosition();
                    check(BoardZoom.Zoom == 1 && !BoardZoom.Zoomed, "the board starts whole");
                });
                Pinch(0.4, () => start, 60, 260);
                At(1.6, () => check(BoardZoom.Zoom > 1.4f && BoardZoom.Zoomed, "two fingers parting zoom the board in (x" + BoardZoom.Zoom.ToString("0.00") + ")"));
                At(1.7, () => check(c.selectedCard == null && c.PlayerInfo.HandList.Count == hand && !c.IsPresenting,
                    "the pinch selected and played nothing, its first finger on a hand card"));
                At(1.8, () => check(BoardZoom.ZoomOutButton != null && BoardZoom.ZoomOutButton.GetComponent<CanvasGroup>().alpha > 0.5f, "the zoom-out button shows"));
                Click(2.1, () => ScreenPoint(BoardZoom.ZoomOutButton.transform));
                At(3.0, () => check(BoardZoom.Zoom == 1 && !BoardZoom.Zoomed, "the zoom-out button brings the whole board back"));
                Pinch(3.2, () => start, 60, 220);
                At(4.3, () => check(BoardZoom.Zoomed, "a second pinch zooms in again"));
                Key(4.5, UnityEngine.InputSystem.Key.Escape);
                At(5.4, () => check(BoardZoom.Zoom == 1 && !PauseMenu.IsOpen, "Escape zooms out (the menu stays closed)"));

                Pinch(5.6, () => start, 60, 260);
                Vector2 pan = Vector2.zero;
                At(6.6, () => pan = BoardZoom.Pan);
                Drag(6.7, () => CardPosition(), new Vector2(-160, 120)); // up: the hand stays in sight
                At(7.6, () => check((BoardZoom.Pan - pan).magnitude > 60 && c.selectedCard == null && c.PlayerInfo.HandList.Count == hand,
                    "one finger drags the zoomed board (moved " + (BoardZoom.Pan - pan).magnitude.ToString("0") + "), from a hand card it selects nothing"));
                Tap(7.9, () => ScreenPoint(BoardZoom.LockButton.transform)); // a finger: the padlock is a touch screen's
                At(8.3, () => check(BoardZoom.Locked, "the padlock locks the board"));
                Tap(8.5, () => CardPosition());
                At(9.0, () => check(c.selectedCard == null, "locked, a tap on a hand card selects nothing"));
                Tap(9.2, () => ScreenPoint(BoardZoom.LockButton.transform));
                At(9.6, () => check(!BoardZoom.Locked, "the padlock unlocks it"));
                Tap(9.8, () => CardPosition());
                At(10.3, () => check(c.selectedCard != null, "unlocked, a tap on a hand card selects it"));
                Key(10.5, UnityEngine.InputSystem.Key.Escape);
                At(11.0, () => check(c.selectedCard == null && BoardZoom.Zoomed, "Escape puts the card back first, the board stays zoomed"));
                Tap(11.2, () => ScreenPoint(BoardZoom.LockButton.transform));
                Key(11.6, UnityEngine.InputSystem.Key.Escape);
                At(12.5, () => check(!BoardZoom.Zoomed && !BoardZoom.Locked, "zoomed out, the board is unlocked"));

                Vector2 table = Vector2.zero;
                At(12.6, () =>
                {
                    table = TablePoint();
                    check(table.x >= 0, "a spot of bare table to double tap");
                });
                Tap(12.7, () => table);
                Tap(12.9, () => table);
                At(13.8, () => check(BoardZoom.Zoom > 1.8f && !PauseMenu.IsOpen && c.selectedCard == null,
                    "a double tap on the table zooms in (x" + BoardZoom.Zoom.ToString("0.00") + ")"));
                Tap(14.0, () => table);
                Tap(14.2, () => table);
                At(15.1, () => check(BoardZoom.Zoom == 1, "another double tap zooms back out"));
                At(15.2, () =>
                {
                    if (touchscreen != null)
                        InputSystem.RemoveDevice(touchscreen);
                    touchscreen = null;
                });
            }

            // One finger down at a point and up again at once
            private void Tap(double time, Func<Vector2> at)
            {
                Vector2 point = Vector2.zero;
                At(time, () =>
                {
                    point = at();
                    Touch(1, point, UnityEngine.InputSystem.TouchPhase.Began);
                });
                At(time + 0.08, () => Touch(1, point, UnityEngine.InputSystem.TouchPhase.Ended));
            }

            // One finger down at a point, moved by a distance in ten steps, then lifted
            private void Drag(double time, Func<Vector2> from, Vector2 by)
            {
                Vector2 at = Vector2.zero;
                At(time, () =>
                {
                    at = from();
                    Touch(1, at, UnityEngine.InputSystem.TouchPhase.Began);
                });
                const int moves = 10;
                for (int i = 1; i <= moves; i++)
                {
                    float t = i / (float)moves;
                    At(time + 0.1 + i * 0.04, () => Touch(1, at + by * t, UnityEngine.InputSystem.TouchPhase.Moved));
                }
                At(time + 0.6, () => Touch(1, at + by, UnityEngine.InputSystem.TouchPhase.Ended));
            }

            // A point of the screen where a tap lands on the table itself (nothing there answers it), near the middle
            private static Vector2 TablePoint()
            {
                List<RaycastResult> hits = new List<RaycastResult>();
                for (int ring = 0; ring < 12; ring++)
                    for (int side = 0; side < 8; side++)
                    {
                        float angle = side * Mathf.PI / 4;
                        Vector2 point = new Vector2(Screen.width / 2f, Screen.height / 2f) + ring * 40f * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        hits.Clear();
                        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                        GameObject hit = hits.Count > 0 ? hits[0].gameObject : null;
                        if (hit != null && (ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) != null || ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit) != null))
                            continue;
                        return point;
                    }
                return new Vector2(-1, -1);
            }

            // Two fingers down around a point, a gap apart, parting to wide, then lifted
            private void Pinch(double time, Func<Vector2> around, float gap, float wide)
            {
                Vector2 at = Vector2.zero;
                At(time, () =>
                {
                    at = around();
                    Touch(1, at, UnityEngine.InputSystem.TouchPhase.Began);
                    Touch(2, at + new Vector2(gap, 0), UnityEngine.InputSystem.TouchPhase.Began);
                });
                const int moves = 10;
                for (int i = 1; i <= moves; i++)
                {
                    float spread = Mathf.Lerp(gap, wide, i / (float)moves);
                    At(time + 0.1 + i * 0.05, () =>
                    {
                        Touch(1, at - new Vector2((spread - gap) / 2, 0), UnityEngine.InputSystem.TouchPhase.Moved);
                        Touch(2, at + new Vector2(gap + (spread - gap) / 2, 0), UnityEngine.InputSystem.TouchPhase.Moved);
                    });
                }
                At(time + 0.75, () =>
                {
                    Touch(1, at - new Vector2((wide - gap) / 2, 0), UnityEngine.InputSystem.TouchPhase.Ended);
                    Touch(2, at + new Vector2(gap + (wide - gap) / 2, 0), UnityEngine.InputSystem.TouchPhase.Ended);
                });
            }

            private Touchscreen touchscreen;

            private void Touch(int id, Vector2 position, UnityEngine.InputSystem.TouchPhase phase)
            {
                sent++;
                if (touchscreen != null)
                    InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = phase, position = position, pressure = 1 });
            }

            private void Send(Vector2 position, MouseButton button, bool pressed)
            {
                sent++;
                if (mouse != null)
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(button, pressed));
            }

            private void SendKeys(KeyboardState state)
            {
                sent++;
                if (keyboard != null)
                    InputSystem.QueueStateEvent(keyboard, state);
            }

            // A card in the middle of the hand (another one once a redraw has rebuilt the hand)
            private Vector2 CardPosition()
            {
                Transform hand = c.PlayerField.transform.Find("Hand");
                if (card == null && hand.childCount > 0)
                    card = hand.GetChild(hand.childCount / 2);
                return card != null ? ScreenPoint(card) : Vector2.zero;
            }

            // The hand card at the front of the redraw carousel (a double-click on it redraws it)
            // The hand card at the carousel's front: by its place in the hand (with two alike, the other is another card)
            private Vector2 FrontHandCard()
            {
                RedrawPicker picker = Picker();
                if (picker != null)
                {
                    int index = 0;
                    foreach (Transform t in c.PlayerField.transform.Find("Hand"))
                    {
                        if (t.GetComponent<CardFace>() == null)
                            continue;
                        if (index++ == picker.FocusIndex)
                            return ScreenPoint(t);
                    }
                }
                return CardPosition();
            }

            // A hand card that is not at the front of the carousel (remembered for the second click)
            private Transform farCard;

            private Vector2 FarHandCard()
            {
                RedrawPicker picker = Picker();
                farCard = null;
                foreach (Transform t in c.PlayerField.transform.Find("Hand"))
                    if (picker != null && t.name != picker.FocusedCardId.ToString())
                        farCard = t;
                return farCard != null ? ScreenPoint(farCard) : CardPosition();
            }

            private static RedrawPicker Picker()
            {
                return UnityEngine.Object.FindAnyObjectByType<RedrawPicker>();
            }

            // A card of the redraw carousel (-1: the front one), where it is when the click happens
            private Func<Vector2> CarouselCard(int index)
            {
                return () =>
                {
                    RedrawPicker picker = Picker();
                    RectTransform card = picker != null ? picker.CardAt(index < 0 ? picker.FocusIndex : index) : null;
                    check(card != null, "the redraw carousel shows card " + index);
                    return card != null ? ScreenPoint(card) : Vector2.zero;
                };
            }

            private bool plainUnit;

            // Prefers a unit that is not a spy or agile: its own row is the only right place
            private void PickPlainUnit()
            {
                foreach (Transform child in c.PlayerField.transform.Find("Hand"))
                {
                    Card stats = child.GetComponent<CardFace>().Card;
                    if (stats.faction != Faction.Special && Rows.IsUnitRow(stats.row) && stats.ability != Ability.Spy)
                    {
                        card = child;
                        plainUnit = true;
                        return;
                    }
                }
            }

            // What the UI raycast hit under the mouse, as a path (for failure messages)
            private string Hovered()
            {
                InputSystemUIInputModule module = EventSystem.current != null ? EventSystem.current.currentInputModule as InputSystemUIInputModule : null;
                if (module == null || mouse == null)
                    return "nothing";
                GameObject hovered = module.GetLastRaycastResult(mouse.deviceId).gameObject;
                string path = "";
                for (Transform t = hovered != null ? hovered.transform : null; t != null; t = t.parent)
                    path = "/" + t.name + path;
                return path == "" ? "nothing" : path;
            }

            // The opponent's row of the same kind as the selected unit
            private Vector2 WrongRow()
            {
                if (!plainUnit || card == null)
                    return CardPosition();
                Row row = card.GetComponent<CardFace>().Card.row;
                Transform target = c.EnemyField.transform.Find(row.ToString()).Find("Unit");
                return ScreenPoint(target);
            }

            // 3 pixels above the bottom edge of the (not yet raised) card
            private Vector2 CardLowerEdge()
            {
                CardPosition();
                if (card == null)
                    return Vector2.zero;
                RectTransform rect = (RectTransform)card;
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector2 bottom = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[3]) / 2);
                return bottom + new Vector2(0, 3);
            }

            private Vector2 DonePosition()
            {
                GameObject done = GameObject.Find("Button Done");
                check(done != null, "the redraw prompt shows a Done button");
                return done != null ? ScreenPoint(done.transform) : Vector2.zero;
            }

            // An active UI object by name (looked up when the click happens)
            // An object by name, or an element of a UI Toolkit screen
            private Func<Vector2> Named(string name)
            {
                return () =>
                {
                    GameObject target = GameObject.Find(name);
                    UnityEngine.UIElements.VisualElement element = target == null ? UIScreens.Find(name) : null;
                    check(target != null || element != null && Settled(element), "\"" + name + "\" is on screen");
                    return target != null ? ScreenPoint(target.transform) : element != null ? ScreenPoint(element) : Vector2.zero;
                };
            }

            // Whether a UI Toolkit element is laid out and stands still: where it was the last time this was asked (a menu
            // or dialog that has finished opening; asked again every frame by a check that waits)
            private readonly Dictionary<UnityEngine.UIElements.VisualElement, Rect> lastBounds = new Dictionary<UnityEngine.UIElements.VisualElement, Rect>();

            private bool Settled(UnityEngine.UIElements.VisualElement element)
            {
                Rect bounds = element.worldBound;
                if (float.IsNaN(bounds.x) || bounds.width <= 0 || bounds.height <= 0)
                    return false;
                Rect was;
                bool still = lastBounds.TryGetValue(element, out was) && Vector2.Distance(was.center, bounds.center) < 0.5f && Mathf.Abs(was.width - bounds.width) < 0.5f;
                lastBounds[element] = bounds;
                return still;
            }

            // The object or UI Toolkit element of that name has the keyboard's focus (the highlight menus share with the mouse)
            private static bool Highlighted(string name)
            {
                GameObject target = GameObject.Find(name);
                if (target != null)
                    return EventSystem.current != null && EventSystem.current.currentSelectedGameObject == target;
                UnityEngine.UIElements.VisualElement element = UIScreens.Find(name);
                return element != null && element.focusController != null && element.focusController.focusedElement == element;
            }

            // A point on the settings' master volume slider, at that fraction of its travel
            private Func<Vector2> MasterVolumeAt(float fraction)
            {
                return () =>
                {
                    UnityEngine.UIElements.VisualElement row = UIScreens.Find("Row Master volume");
                    UnityEngine.UIElements.Slider slider = row != null ? Query<UnityEngine.UIElements.Slider>(row) : null;
                    check(slider != null && Settled(slider), "the settings show the master volume slider");
                    if (slider == null)
                        return Vector2.zero;
                    // Where the knob's middle stands at that value: its travel is the drag area less the knob's width
                    Rect area = Query<UnityEngine.UIElements.VisualElement>(slider, null, UnityEngine.UIElements.Slider.dragContainerUssClassName).worldBound;
                    float knob = Query<UnityEngine.UIElements.VisualElement>(slider, null, UnityEngine.UIElements.Slider.draggerUssClassName).worldBound.width;
                    return ScreenPoint(slider.panel, new Vector2(area.xMin + knob / 2 + fraction * (area.width - knob), area.center.y));
                };
            }

            // The middle of a UI Toolkit element, in the input's screen pixels (from the bottom left). Solved from where screen
            // points land on the panel, so it holds whatever the panel's scaling; ScreenToPanel counts from the top left.
            private static Vector2 ScreenPoint(UnityEngine.UIElements.VisualElement element)
            {
                return ScreenPoint(element.panel, element.worldBound.center);
            }

            private static Vector2 ScreenPoint(UnityEngine.UIElements.IPanel panel, Vector2 point)
            {
                Vector2 origin = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
                Vector2 x = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, Vector2.right) - origin;
                Vector2 y = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, Vector2.up) - origin;
                return new Vector2((point.x - origin.x) / x.x, Screen.height - (point.y - origin.y) / y.y);
            }

            private static Vector2 ScreenPoint(Transform target)
            {
                RectTransform rect = (RectTransform)target;
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Canvas canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                return RectTransformUtility.WorldToScreenPoint(camera, (corners[0] + corners[2]) / 2);
            }
        }
    }
}
