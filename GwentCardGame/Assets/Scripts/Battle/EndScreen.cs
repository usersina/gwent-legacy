using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.Net;
using Gwent.Rules;
using Gwent.Story;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // The end of the battle: The Witcher 3's emblem of the outcome, the round tally, a scoreboard with both sides (leader
    // portrait, name, faction, every round's score, a laurel behind each round's winning score) and the way on, as card
    // games show it: one gold button (its key, Enter, beside its name) and round icon buttons beside it, each named under
    // it with its key on its ring (as the story map's tools). Against the AI, Play again; in a LAN battle, Rematch (once
    // both players ask, the next battle loads; over the buttons, what the other player is at: asking too, away, gone)
    // and, for its guest, the fair play check of the battle (FairPlay); Save replay (S), to watch the battle again from
    // the main menu (a LAN guest's, once its fair play check passed); Main menu (Esc). A story's battle that gave cards
    // shows them on their own first, large (Collect, then the scoreboard), what it gave in a line under the scoreboard,
    // and leads back to its map: won, Back to the map, Play again (R: a rematch, crowns only); lost, Play again; a
    // tournament's Next round. Built when the battle ends, over the dimmed board.
    public class EndScreen : MonoBehaviour
    {
        private const float SideColumn = 400;
        private const float RoundColumn = 170;
        private const float RowHeight = 116;
        private const float BarY = -372;      // the way on: its gold button's middle, and its round buttons'
        private const float LineY = -292;     // a line between the scoreboard and the way on (a story's spoils, a rematch's status)
        private const float FairPlayY = -500; // a LAN guest's fair play check, under the way on
        private const float RingSize = 76;
        private const float RingOffset = 330; // from the gold button's middle to a round button's
        private static readonly Vector2 MainSize = new Vector2(380, 76);

        private bool lanBattle;
        private LanSession lan;         // the LAN battle's session, while it lasts
        private int battle;             // the session's battle shown: another one is the rematch
        private string opponentName;
        private Button primary, again, collect; // the way on (Enter); Play again as a round button (R); a story's Collect
        private Button rematch, saveReplay;
        private Image saveReplayIcon;
        private TextMeshProUGUI saveReplayLabel, status, fairPlay;
        private RectTransform summary;  // the scoreboard and the way on (after a story's cards, once collected)
        private bool loading;
        private readonly RectTransform[] portraits = new RectTransform[2]; // the scoreboard's: the player's, the opponent's

        // A side's portrait on the scoreboard (what its chat bubbles come out of: ChatView); null before it shows
        public RectTransform Portrait(bool player)
        {
            return portraits[player ? 0 : 1];
        }

        // gave_up: the player gave up; opponent_gave_up: the opponent did (still there, or gone: the status line says);
        // opponent_left: a LAN host's guest away, its host claimed the win; lan_battle: a LAN battle (its session may be gone
        // already: the other player left); ended_away: its host ended it while this guest was away; replay: a replay's end
        // (Watch again, back to the replays); story: a battle of the story
        public void Show(BattleState outcome, BattleController.BattleSide player, BattleController.BattleSide opponent, bool gave_up = false, bool opponent_gave_up = false, bool opponent_left = false, bool lan_battle = false, bool ended_away = false, bool replay = false, StoryBattle story = null)
        {
            UITheme theme = UITheme.Current;
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            RectTransform content = UIKit.Rect("Content", transform).Stretch();
            UIMotion.Appear(content.gameObject);

            string emblem = outcome == BattleState.Won ? "win" : outcome == BattleState.Lost ? "lose" : "draw";
            // Rounds as they were won (a drawn round counts for nobody, a Nilfgaard tie for Nilfgaard)
            int player_rounds = player.RoundsWon.Count(won => won), opponent_rounds = opponent.RoundsWon.Count(won => won);
            int drawn = player.RoundsScore.Count - player_rounds - opponent_rounds;
            string drawn_text = drawn == 1 ? ", one round drawn" : drawn > 1 ? ", " + drawn + " rounds drawn" : "";
            // A replay seen from the other side: the bottom side by its name, not "You"
            string you = replay && Gwent.Replays.ReplayPlayer.Current != null && Gwent.Replays.ReplayPlayer.Current.Switched ? player.Name : "You";
            string tally = outcome == BattleState.Won ? you + " won the battle, " + Rounds(player_rounds) + " to " + opponent_rounds + drawn_text
                : outcome == BattleState.Lost ? (lan_battle || story != null ? opponent.Name : "The opponent") + " won the battle, " + Rounds(opponent_rounds) + " to " + player_rounds + drawn_text
                : "Neither side came out ahead" + drawn_text;
            if (gave_up)
                tally = you + " gave up the battle";
            else if (opponent_gave_up)
                tally = (lan_battle || story != null ? opponent.Name : "The opponent") + " gave up the battle";
            else if (opponent_left)
                tally = opponent.Name + " left the battle";
            else if (ended_away)
                tally = opponent.Name + " ended the battle while you were away";

            // A faint light behind the emblem lifts it off the dimmed board (the emblem brings its own colour)
            Image glow = UIKit.NewImage(content, "Glow", theme.radial, UITheme.WithAlpha(theme.textBright, 0.1f));
            glow.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 340), new Vector2(1100, 560));
            // The crown over crossed swords, the skull over broken blades, the scales: its plaque names the outcome
            Image crest = UIKit.NewImage(content, "Emblem", SpriteCache.Load("W3/End/" + emblem), Color.white);
            crest.preserveAspect = true;
            crest.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 345), new Vector2(525, 333));
            TextMeshProUGUI subtitle = UIKit.Text(content, tally, UIKit.TextStyle.Body, TextAlignmentOptions.Center);
            subtitle.color = theme.textSecondary;
            subtitle.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1200, 44));

            summary = UIKit.Rect("Summary", content).Stretch();
            BuildScoreboard(summary, player, opponent);

            if (replay)
            {
                ShowReplayButtons(summary);
                return;
            }
            if (story != null)
            {
                ShowStory(content, story);
                return;
            }

            // A LAN battle whose session is gone (the other player left): back to the main menu only
            lanBattle = lan_battle;
            lan = lan_battle ? LanSession.Current : null;
            bool menu_only = lan_battle && lan == null;
            SaveReplayRing(summary, -RingOffset);
            if (menu_only)
            {
                primary = Main(summary, "Main menu", ToMainMenu);
                UIKit.Focus(primary);
                return;
            }
            Ring(summary, "Main menu", "Main menu", theme.iconHome, "Esc", RingOffset, ToMainMenu);
            if (lan != null)
            {
                battle = lan.Battle;
                opponentName = opponent.Name;
                rematch = primary = Main(summary, "Rematch", () => lan.AskRematch());
                status = UIKit.Text(summary, "", UIKit.TextStyle.Body, TextAlignmentOptions.Center);
                status.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, LineY), new Vector2(1200, 44));
                if (!lan.Hosting)
                {
                    fairPlay = UIKit.Text(summary, "", UIKit.TextStyle.Caption, TextAlignmentOptions.Center);
                    fairPlay.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, FairPlayY), new Vector2(1400, 36));
                }
                ShowRematch();
                UIKit.Focus(rematch);
                return;
            }
            primary = Main(summary, "Play again", PlayAgain);
            UIKit.Focus(primary);
        }

        //----------------------------------------The way on----------------------------------------//
        // The gold button under the scoreboard, its key (Enter) beside its name
        private static Button Main(RectTransform parent, string text, System.Action on_click)
        {
            Button button = UIKit.Button(parent, text, UIKit.ButtonStyle.Primary, on_click, MainSize);
            ((RectTransform)button.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(0, BarY), MainSize);
            if (GameInput.IsTouch)
                return button;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            RectTransform cap = UIKit.KeyCap(button.transform, "Enter", 28, UITheme.Current.background);
            float text_width = label.GetPreferredValues(label.text).x, gap = 14;
            float width = text_width + gap + cap.sizeDelta.x;
            label.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2((text_width - width) / 2, 1), new Vector2(text_width + 4, MainSize.y));
            cap.Place(new Vector2(0.5f, 0.5f), new Vector2((width - cap.sizeDelta.x) / 2, 0), cap.sizeDelta);
            return button;
        }

        // A round button beside it: its icon in a gold ring, its key on the ring's foot, its name under it
        private static Button Ring(RectTransform parent, string name, string label, Sprite icon, string key, float x, System.Action on_click)
        {
            UITheme theme = UITheme.Current;
            Button button = UIKit.RoundButton(parent, "Button " + name, null, icon, on_click, RingSize, true);
            // Round at this size: a circle and a ring (the small button's rounded outline would show square)
            ((Image)button.targetGraphic).sprite = theme.circle;
            ((Image)button.targetGraphic).type = Image.Type.Simple;
            Image outline = button.transform.Find("Ring").GetComponent<Image>();
            outline.sprite = theme.ring;
            outline.type = Image.Type.Simple;
            RectTransform ring = (RectTransform)button.transform;
            ring.Place(new Vector2(0.5f, 0.5f), new Vector2(x, BarY), new Vector2(RingSize, RingSize));
            TextMeshProUGUI text = UIKit.Text(ring, label, UIKit.TextStyle.Caption, TextAlignmentOptions.Center);
            text.gameObject.name = "Label";
            text.color = theme.textSecondary;
            text.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, -38), new Vector2(260, 34));
            if (!GameInput.IsTouch && key != null)
            {
                RectTransform cap = UIKit.KeyCap(ring, key, 26);
                Image under = UIKit.NewImage(cap, "Under", theme.panelSprite, UITheme.WithAlpha(theme.panelRaised, 1));
                under.rectTransform.Stretch();
                under.transform.SetAsFirstSibling(); // the ring's line doesn't run through the key's name
                cap.Place(new Vector2(0.5f, 0), Vector2.zero, cap.sizeDelta);
            }
            UIKit.GrowTouchArea(ring, new Vector2(220, 160)); // a finger's, its name under it included
            return button;
        }

        // The keys on the buttons: Enter (the gold button, while nothing else has the focus), R (Play again), S (Save
        // replay); not while a message is typed (a LAN battle's talk) or something is open over the screen
        private void Keys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || loading || UIOverlay.HasModal || LoadingScreen.Showing)
                return;
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<TMP_InputField>() != null)
                return;
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && selected == null)
                Press(collect != null ? collect : primary);
            else if (keyboard.rKey.wasPressedThisFrame)
                Press(again);
            else if (keyboard.sKey.wasPressedThisFrame)
                Press(saveReplay);
        }

        private static void Press(Button button)
        {
            if (button != null && button.isActiveAndEnabled && button.IsInteractable())
                button.onClick.Invoke();
        }

        //----------------------------------------Save replay----------------------------------------//
        private void SaveReplayRing(RectTransform parent, float x)
        {
            saveReplay = Ring(parent, "Save replay", "Save replay", UITheme.Current.iconFilm, "S", x, SaveReplay);
            saveReplayIcon = saveReplay.transform.Find("Icon").GetComponent<Image>();
            saveReplayLabel = saveReplay.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            ShowSaveReplay();
        }

        private void SaveReplay()
        {
            BattleController battle_screen = BattleController.Instance;
            if (battle_screen == null || !battle_screen.CanSaveReplay)
                return;
            if (!battle_screen.SaveReplay())
                UIOverlay.Toast("The replay could not be saved");
            ShowSaveReplay();
        }

        // Save replay while it can be (a LAN guest's: once its fair play check passed); once saved, a tick and Saved
        private void ShowSaveReplay()
        {
            if (saveReplay == null)
                return;
            BattleController battle_screen = BattleController.Instance;
            bool saved = battle_screen != null && battle_screen.ReplaySaved;
            bool can = battle_screen != null && battle_screen.CanSaveReplay;
            if (saveReplay.interactable != can)
                saveReplay.interactable = can;
            string text = saved ? "Saved" : "Save replay";
            if (saveReplayLabel.text != text)
            {
                saveReplayLabel.text = text;
                saveReplayIcon.sprite = saved ? UITheme.Current.iconCheck : UITheme.Current.iconFilm;
            }
        }

        //----------------------------------------Rematch----------------------------------------//
        private void Update()
        {
            ShowSaveReplay();
            Keys();
            if (!lanBattle || rematch == null || loading)
                return;
            if (lan != null && lan.State == LanSession.Stage.Battle && lan.Battle != battle)
            {
                // Both asked: the next battle
                loading = true;
                Time.timeScale = 1f;
                LoadingScreen.Load(SceneNames.Game);
                return;
            }
            ShowRematch();
        }

        // Rematch can be asked once the battle is over on both devices and the other player is there; over it, what the
        // other player is at
        private void ShowRematch()
        {
            UITheme theme = UITheme.Current;
            bool gone = lan == null || lan.State == LanSession.Stage.Closed;
            bool can = !gone && lan.Linked && lan.Over && !lan.AskedRematch;
            if (rematch.interactable != can)
                rematch.interactable = can; // the keys stay on it (moved to Main menu, a second press would leave)
            status.text = gone ? opponentName + " left"
                : !lan.Linked ? opponentName + " is away"
                : lan.AskedRematch ? "Waiting for " + opponentName
                : lan.TheyAskedRematch ? opponentName + " wants a rematch"
                : "";
            status.color = !gone && lan.Linked && lan.TheyAskedRematch && !lan.AskedRematch ? theme.goldBright : theme.textMuted;
            if (fairPlay != null && !gone && lan.FairChecked)
            {
                fairPlay.text = lan.FairProblem == null ? "Fair play checked: the shuffle was shared, and every move followed the rules"
                    : "Fair play check failed. " + lan.FairProblem;
                fairPlay.color = lan.FairProblem == null ? theme.textMuted : new Color(0.9f, 0.42f, 0.36f);
            }
        }

        //----------------------------------------Scoreboard----------------------------------------//
        private void BuildScoreboard(RectTransform parent, BattleController.BattleSide player, BattleController.BattleSide opponent)
        {
            UITheme theme = UITheme.Current;
            float width = SideColumn + 3 * RoundColumn + 60;
            RectTransform board = UIKit.Panel(parent, "Scoreboard").Place(new Vector2(0.5f, 0.5f), new Vector2(0, -85), new Vector2(width, 70 + 2 * RowHeight + 20));

            for (int round = 0; round < 3; round++)
            {
                TextMeshProUGUI header = UIKit.Text(board, "Round " + (round + 1), UIKit.TextStyle.Tab, TextAlignmentOptions.Center);
                header.color = theme.textMuted;
                header.rectTransform.Place(new Vector2(0, 1), new Vector2(30 + SideColumn + (round + 0.5f) * RoundColumn, -40), new Vector2(RoundColumn, 40));
            }
            UIKit.Divider(board, -70, 30);

            AddRow(board, 0, player, player.Name, opponent);
            UIKit.Divider(board, -70 - RowHeight, 30);
            AddRow(board, 1, opponent, opponent.Name, player);
        }

        private static string Rounds(int count)
        {
            return count + (count == 1 ? " round" : " rounds");
        }

        private void AddRow(RectTransform board, int index, BattleController.BattleSide info, string name, BattleController.BattleSide other)
        {
            List<int> scores = info.RoundsScore;
            UITheme theme = UITheme.Current;
            float y = -70 - index * RowHeight - RowHeight / 2;

            // Leader portrait in a gold ring
            Image mask = UIKit.NewImage(board, "Portrait", theme.circle, Color.white);
            mask.rectTransform.Place(new Vector2(0, 1), new Vector2(30 + 56, y), new Vector2(84, 84));
            portraits[index] = mask.rectTransform;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Image portrait = UIKit.NewImage(mask.transform, "Art", null, Color.white);
            CardArt.ShowPortrait(portrait, info.Leader);
            Image ring = UIKit.NewImage(board, "Ring", theme.ring, theme.gold);
            ring.rectTransform.Place(new Vector2(0, 1), new Vector2(30 + 56, y), new Vector2(92, 92));

            TextMeshProUGUI who = UIKit.Text(board, name, UIKit.TextStyle.Heading, TextAlignmentOptions.BottomLeft);
            who.fontSize = 32;
            // A long name (a LAN player's, up to 16) stops short of the first round's score: smaller, then cut with "…"
            who.fontSizeMax = 32;
            who.fontSizeMin = 18;
            who.enableAutoSizing = true;
            who.overflowMode = TextOverflowModes.Ellipsis;
            who.color = theme.text;
            who.rectTransform.Place(new Vector2(0, 1), new Vector2(30 + 120, y + 4), new Vector2(270, 44), new Vector2(0, 0));
            TextMeshProUGUI faction = UIKit.Text(board, Factions.DisplayName(info.Faction), UIKit.TextStyle.Caption, TextAlignmentOptions.TopLeft);
            faction.rectTransform.Place(new Vector2(0, 1), new Vector2(30 + 120, y - 2), new Vector2(270, 34), new Vector2(0, 1));

            for (int round = 0; round < 3; round++)
            {
                Vector2 cell = new Vector2(30 + SideColumn + (round + 0.5f) * RoundColumn, y);
                bool played = round < scores.Count;
                bool won = round < info.RoundsWon.Count && info.RoundsWon[round];
                bool lost = round < other.RoundsWon.Count && other.RoundsWon[round];
                if (won)
                {
                    Image laurel = UIKit.NewImage(board, "Laurel", SpriteCache.Load("Various/pass"), UITheme.WithAlpha(theme.gold, 0.85f));
                    laurel.preserveAspect = true;
                    laurel.rectTransform.Place(new Vector2(0, 1), cell, new Vector2(110, 96));
                }
                TextMeshProUGUI score = UIKit.Text(board, played ? scores[round].ToString() : "–", UIKit.TextStyle.Heading, TextAlignmentOptions.Center);
                score.fontSize = 44;
                score.color = !played ? theme.textMuted : won ? theme.goldBright : lost ? theme.textMuted : theme.text;
                score.rectTransform.Place(new Vector2(0, 1), cell, new Vector2(RoundColumn, 60));
            }
        }

        //----------------------------------------A replay's end----------------------------------------//
        // Watch it again from the start, or back to the replays (Esc)
        private void ShowReplayButtons(RectTransform parent)
        {
            primary = Main(parent, "Watch again", () => Gwent.Replays.ReplayPlayer.Current?.Restart());
            Ring(parent, "Replays", "Replays", UITheme.Current.iconFilm, "Esc", RingOffset, Gwent.Replays.ReplayPlayer.Leave);
            UIKit.Focus(primary);
        }

        public void PlayAgain()
        {
            Time.timeScale = 1f;
            LoadingScreen.Load(SceneNames.Game);
        }

        // Out of the battle: the main menu, or a story's map
        public void ToMainMenu()
        {
            Time.timeScale = 1f;
            if (StoryBattle.Current != null)
            {
                StoryBattle.Leave();
                return;
            }
            LanSession.Current?.Leave();
            LoadingScreen.Load(SceneNames.Menu);
        }

        //----------------------------------------------The story----------------------------------------------//
        // The way on: won, Back to the map, Play again (a rematch: crowns only) beside it; lost, Play again, the map beside
        // it; a tournament's next round while its run goes on (over, the map). What it gave under the scoreboard; the cards
        // won on their own first
        private void ShowStory(RectTransform content, StoryBattle story)
        {
            UITheme theme = UITheme.Current;
            System.Action start = () => StoryBattle.Start(story.Place);
            if (story.Won && story.RoundsLeft)
            {
                primary = Main(summary, "Next round", start);
                SaveReplayRing(summary, -RingOffset);
                Ring(summary, "Back to the map", "Map", theme.iconMap, "Esc", RingOffset, ToMainMenu);
            }
            else if (story.Won || story.Round != null)
            {
                primary = Main(summary, "Back to the map", ToMainMenu);
                if (story.Round == null)
                    again = Ring(summary, "Play again", "Play again", theme.iconAgain, "R", -RingOffset, start);
                SaveReplayRing(summary, story.Round == null ? RingOffset : -RingOffset);
            }
            else
            {
                primary = Main(summary, "Play again", start);
                SaveReplayRing(summary, -RingOffset);
                Ring(summary, "Back to the map", "Map", theme.iconMap, "Esc", RingOffset, ToMainMenu);
            }
            ShowSpoils(summary, story);
            if (story.Won && story.Reward != null && story.Reward.Cards.Count > 0)
            {
                summary.gameObject.SetActive(false);
                ShowReward(content, story);
            }
            else
                UIKit.Focus(primary);
        }

        // Under the scoreboard: what the battle gave (the cards, small; the crowns) and the tournament's next round, or
        // what a loss means
        private static void ShowSpoils(RectTransform parent, StoryBattle story)
        {
            UITheme theme = UITheme.Current;
            Line line = new Line(parent, "Spoils", LineY);
            StoryReward reward = story.Won ? story.Reward : null;
            if (reward == null)
            {
                if (!story.Won)
                    line.Text(StoryLossText(story), theme.textSecondary);
                line.Done();
                return;
            }
            CardDatabase db = CardLibrary.Get();
            foreach (int card in reward.Cards.Take(RewardCardsShown))
            {
                Image art = line.Picture("Small " + db.Get(card).DisplayName, null, Color.white, new Vector2(34, 53));
                CardArt.Show(art, card);
            }
            if (reward.Cards.Count > 0)
                line.Text(reward.Cards.Count <= 2 ? string.Join(", ", reward.Cards.Select(card => db.Get(card).DisplayName)) : reward.Cards.Count + " cards", theme.text);
            if (reward.Crowns > 0)
            {
                line.Picture("Coin", theme.iconCoin, theme.goldBright, new Vector2(34, 34));
                line.Text("+" + reward.Crowns, theme.goldBright);
            }
            // What it opened shows with its cards; without any (rare), here
            foreach (var (_, text) in Opened(story, reward.Cards.Count == 0))
                line.Text("·  " + text, theme.goldBright);
            line.Done();
        }

        // What a loss means: a tournament's run over (enter again from the map), or nothing lost
        private static string StoryLossText(StoryBattle story)
        {
            return story.Round != null ? story.Place.Name + " is over for this time: enter again from the map" : "Nothing lost: play them again whenever you like";
        }

        // What the battle opened (the quests done, the regions, the whole collection) and the tournament's next round;
        // with_opened false: the next round only
        private static List<(Sprite icon, string text)> Opened(StoryBattle story, bool with_opened = true)
        {
            UITheme theme = UITheme.Current;
            StoryReward reward = story.Reward;
            List<(Sprite, string)> lines = new List<(Sprite, string)>();
            if (reward != null && with_opened)
            {
                lines.AddRange(reward.Quests.Select(quest => (theme.iconQuest, quest.Name + " complete")));
                lines.AddRange(reward.Regions.Select(region => (theme.iconTravel, region.Name + " is open")));
                if (reward.Cards.Count > 0 && StoryGame.Progress.CollectionComplete)
                    lines.Add((theme.iconCards, "Card Collector: every Gwent card is yours"));
            }
            if (story.Won && story.RoundsLeft)
            {
                StoryRound next = StoryGame.Progress.NextRound(story.Place);
                if (next != null)
                    lines.Add((theme.iconGoblet, "Next: " + next.Name));
            }
            return lines;
        }

        //----------------------------------------The cards won----------------------------------------//
        // The cards a win gave on their own, large: each turns over from its back, what it is under it (new, or how many
        // you have now; a quest's reward); beside them the crowns (counted up), the quests done, the regions opened;
        // Collect (Enter) shows the scoreboard and the way on
        private const int RewardCardsShown = 5;
        private const float CardAspect = 0.642f, CardY = -72, RewardGap = 40, InfoGap = 64, InfoWidth = 500, InfoRow = 54, CrownsRow = 86;

        private void ShowReward(RectTransform content, StoryBattle story)
        {
            UITheme theme = UITheme.Current;
            CardDatabase db = CardLibrary.Get();
            StoryReward reward = story.Reward;
            RectTransform view = UIKit.Rect("Reward", content).Stretch();
            List<int> cards = reward.Cards.Take(RewardCardsShown).ToList();
            List<(Sprite icon, string text)> lines = Opened(story);
            if (reward.Cards.Count > cards.Count)
                lines.Insert(0, (theme.iconCards, "And " + (reward.Cards.Count - cards.Count) + " more cards"));
            bool info = reward.Crowns > 0 || lines.Count > 0;

            float height = cards.Count <= 2 ? 360 : cards.Count == 3 ? 320 : 270;
            float width = Mathf.Round(height * CardAspect);
            float cards_width = cards.Count * width + (cards.Count - 1) * RewardGap;
            float left = -(cards_width + (info ? InfoGap + InfoWidth : 0)) / 2;
            for (int i = 0; i < cards.Count; i++)
            {
                int card = cards[i];
                Vector2 at = new Vector2(left + i * (width + RewardGap) + width / 2, CardY);
                Image face = UIKit.NewImage(view, "Won " + db.Get(card).DisplayName, null, Color.white, true);
                face.rectTransform.Place(new Vector2(0.5f, 0.5f), at, new Vector2(width, height));
                CardArt.Show(face, card);
                face.gameObject.AddComponent<UITooltip>().text = () => db.Get(card).DisplayName;
                face.rectTransform.localScale = new Vector3(0, 1, 1);
                Image back = UIKit.NewImage(view, "Back", CardArt.Back(card), Color.white);
                back.rectTransform.Place(new Vector2(0.5f, 0.5f), at, new Vector2(width, height));
                TextMeshProUGUI what = UIKit.Text(view, WhatItIs(card, reward, db), UIKit.TextStyle.Caption, TextAlignmentOptions.Center);
                what.color = theme.goldBright;
                what.enableAutoSizing = true;
                what.fontSizeMin = 16;
                what.fontSizeMax = what.fontSize;
                what.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(at.x, CardY - height / 2 - 30), new Vector2(width + RewardGap - 8, 34));
                StartCoroutine(TurnOver(back, face, 0.45f + i * 0.35f));
            }

            if (info)
            {
                // Beside the cards, its rows centred on them
                float x = left + cards_width + InfoGap;
                float rows = (reward.Crowns > 0 ? CrownsRow : 0) + lines.Count * InfoRow;
                float y = CardY + rows / 2;
                if (reward.Crowns > 0)
                {
                    Image coin = UIKit.NewImage(view, "Coin", theme.iconCoin, theme.goldBright);
                    coin.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(x + 28, y - CrownsRow / 2), new Vector2(56, 56));
                    TextMeshProUGUI amount = UIKit.Text(view, "+0", UIKit.TextStyle.Heading, TextAlignmentOptions.MidlineLeft);
                    amount.fontSize = 56;
                    amount.color = theme.goldBright;
                    amount.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(x + 72 + 150, y - CrownsRow / 2), new Vector2(300, 70));
                    StartCoroutine(CountUp(amount, reward.Crowns, 0.45f + cards.Count * 0.35f));
                    y -= CrownsRow;
                }
                foreach ((Sprite icon, string text) in lines)
                {
                    Image mark = UIKit.NewImage(view, "Mark", icon, theme.goldBright);
                    mark.preserveAspect = true;
                    mark.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(x + 18, y - InfoRow / 2), new Vector2(34, 34));
                    TextMeshProUGUI said = UIKit.Text(view, text, UIKit.TextStyle.Body, TextAlignmentOptions.MidlineLeft);
                    said.color = theme.text;
                    said.enableAutoSizing = true;
                    said.fontSizeMin = 18;
                    said.fontSizeMax = said.fontSize;
                    said.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(x + 52 + (InfoWidth - 52) / 2, y - InfoRow / 2), new Vector2(InfoWidth - 52, InfoRow));
                    y -= InfoRow;
                }
            }

            collect = Main(view, "Collect", () =>
            {
                Destroy(view.gameObject);
                collect = null;
                summary.gameObject.SetActive(true);
                UIMotion.Appear(summary.gameObject);
                UIKit.Focus(primary);
            });
            UIKit.Focus(collect);
        }

        // Under a card won: new, or how many you have now; a quest's reward says so
        private static string WhatItIs(int card, StoryReward reward, CardDatabase db)
        {
            int owned = StoryGame.Progress != null ? StoryGame.Progress.Owned(card) : 1;
            string fresh = owned <= 1 ? (db.Get(card).IsLeader ? "new leader" : "new card") : "you have " + owned;
            if (reward.Quests.Any(quest => quest.Reward.Contains(card)))
                return "Quest reward · " + fresh;
            return char.ToUpper(fresh[0]) + fresh.Substring(1);
        }

        // A card turned over: its back narrows to nothing, then its face widens from nothing
        private static IEnumerator TurnOver(Image back, Image face, float delay)
        {
            for (float t = 0; t < delay; t += Time.unscaledDeltaTime)
                yield return null;
            if (back == null || face == null)
                yield break;
            GameAudio.PlaySfx("card_draw");
            const float half = 0.14f;
            for (float t = 0; t < half && back != null; t += Time.unscaledDeltaTime)
            {
                back.rectTransform.localScale = new Vector3(1 - t / half, 1, 1);
                yield return null;
            }
            if (back == null || face == null)
                yield break;
            Destroy(back.gameObject);
            for (float t = 0; t < half && face != null; t += Time.unscaledDeltaTime)
            {
                face.rectTransform.localScale = new Vector3(t / half, 1, 1);
                yield return null;
            }
            if (face != null)
                face.rectTransform.localScale = Vector3.one;
        }

        // The crowns counted up from nothing, once the cards are turned over
        private static IEnumerator CountUp(TextMeshProUGUI amount, int crowns, float delay)
        {
            for (float t = 0; t < delay; t += Time.unscaledDeltaTime)
                yield return null;
            const float seconds = 0.6f;
            for (float t = 0; t < seconds && amount != null; t += Time.unscaledDeltaTime)
            {
                amount.text = "+" + Mathf.RoundToInt(crowns * Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
            if (amount != null)
                amount.text = "+" + crowns;
        }

        // Pieces side by side (small cards, words, a coin), centred on a line
        private class Line
        {
            private const float Gap = 12;
            private readonly RectTransform row;
            private readonly List<RectTransform> pieces = new List<RectTransform>();

            public Line(RectTransform parent, string name, float y)
            {
                row = UIKit.Rect(name, parent).Place(new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(0, 60));
            }

            public Image Picture(string name, Sprite sprite, Color color, Vector2 size)
            {
                Image image = UIKit.NewImage(row, name, sprite, color);
                image.preserveAspect = sprite != null;
                image.rectTransform.sizeDelta = size;
                pieces.Add(image.rectTransform);
                return image;
            }

            public void Text(string text, Color color)
            {
                TextMeshProUGUI label = UIKit.Text(row, text, UIKit.TextStyle.Body, TextAlignmentOptions.Center);
                label.color = color;
                label.rectTransform.sizeDelta = new Vector2(label.GetPreferredValues(text).x + 2, 44);
                pieces.Add(label.rectTransform);
            }

            public void Done()
            {
                float x = -(pieces.Sum(piece => piece.sizeDelta.x) + Gap * (pieces.Count - 1)) / 2;
                foreach (RectTransform piece in pieces)
                {
                    piece.Place(new Vector2(0.5f, 0.5f), new Vector2(x + piece.sizeDelta.x / 2, 0), piece.sizeDelta);
                    x += piece.sizeDelta.x + Gap;
                }
            }
        }
    }
}
