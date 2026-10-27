using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.Net;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // The end of the battle: The Witcher 3's emblem of the outcome, the round tally, a scoreboard with both sides (leader
    // portrait, name, faction, every round's score, a laurel behind each round's winning score) and the way on: against
    // the AI, Play again; in a LAN battle, Rematch (once both players ask, the next battle loads; under the buttons, what
    // the other player is at: asking too, away, gone) and, for its guest, the fair play check of the battle (FairPlay);
    // Save replay, to watch the battle again from the main menu (a LAN guest's, once its fair play check passed); Main
    // menu. Built when the battle ends, over the dimmed board.
    public class EndScreen : MonoBehaviour
    {
        private const float SideColumn = 400;
        private const float RoundColumn = 170;
        private const float RowHeight = 116;
        private const float ButtonSpacing = 340; // from a button's middle to the next one's

        private bool lanBattle;
        private LanSession lan;         // the LAN battle's session, while it lasts
        private int battle;             // the session's battle shown: another one is the rematch
        private string opponentName;
        private Button rematch, menu, saveReplay;
        private TextMeshProUGUI status, fairPlay;
        private bool loading;
        private readonly RectTransform[] portraits = new RectTransform[2]; // the scoreboard's: the player's, the opponent's

        // A side's portrait on the scoreboard (what its chat bubbles come out of: ChatView); null before it shows
        public RectTransform Portrait(bool player)
        {
            return portraits[player ? 0 : 1];
        }

        // gave_up: the player gave up; opponent_left: the opponent did (a LAN battle's other player); lan_battle: a LAN
        // battle (its session may be gone already: the other player left); ended_away: its host ended it while this guest
        // was away; replay: a replay's end (Watch again, back to the replays)
        public void Show(BattleState outcome, BattleController.BattleSide player, BattleController.BattleSide opponent, bool gave_up = false, bool opponent_left = false, bool lan_battle = false, bool ended_away = false, bool replay = false)
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
                : outcome == BattleState.Lost ? (lan_battle ? opponent.Name : "The opponent") + " won the battle, " + Rounds(opponent_rounds) + " to " + player_rounds + drawn_text
                : "Neither side came out ahead" + drawn_text;
            if (gave_up)
                tally = you + " gave up the battle";
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

            BuildScoreboard(content, player, opponent);

            if (replay)
            {
                ShowReplayButtons(content);
                return;
            }

            // A LAN battle whose session is gone (the other player left): back to the main menu only
            lanBattle = lan_battle;
            lan = lan_battle ? LanSession.Current : null;
            bool menu_only = lan_battle && lan == null;
            saveReplay = UIKit.Button(content, "Save replay", UIKit.ButtonStyle.Secondary, SaveReplay, new Vector2(300, 72));
            menu = UIKit.Button(content, "Main menu", menu_only ? UIKit.ButtonStyle.Primary : UIKit.ButtonStyle.Secondary, ToMainMenu, new Vector2(300, 72));
            ShowSaveReplay();
            if (menu_only)
            {
                PlaceButtons(saveReplay, menu);
                UIKit.Focus(menu);
                return;
            }
            if (lan != null)
            {
                battle = lan.Battle;
                opponentName = opponent.Name;
                rematch = UIKit.Button(content, "Rematch", UIKit.ButtonStyle.Primary, () => lan.AskRematch(), new Vector2(300, 72));
                PlaceButtons(rematch, saveReplay, menu);
                status = UIKit.Text(content, "", UIKit.TextStyle.Body, TextAlignmentOptions.Center);
                status.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -432), new Vector2(1200, 44));
                if (!lan.Hosting)
                {
                    fairPlay = UIKit.Text(content, "", UIKit.TextStyle.Caption, TextAlignmentOptions.Center);
                    fairPlay.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -486), new Vector2(1400, 36));
                }
                ShowRematch();
                UIKit.Focus(rematch);
                return;
            }
            Button again = UIKit.Button(content, "Play again", UIKit.ButtonStyle.Primary, PlayAgain, new Vector2(300, 72));
            PlaceButtons(again, saveReplay, menu);
            UIKit.Focus(again);
        }

        // The buttons side by side, under the scoreboard
        private static void PlaceButtons(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
                ((RectTransform)buttons[i].transform).Place(new Vector2(0.5f, 0.5f), new Vector2((i - (buttons.Length - 1) / 2f) * ButtonSpacing, -350), new Vector2(300, 72));
        }

        //----------------------------------------Save replay----------------------------------------//
        private void SaveReplay()
        {
            BattleController battle_screen = BattleController.Instance;
            if (battle_screen == null || !battle_screen.CanSaveReplay)
                return;
            if (!battle_screen.SaveReplay())
                UIOverlay.Toast("The replay could not be saved");
            ShowSaveReplay();
        }

        // Save replay while it can be (a LAN guest's: once its fair play check passed); once saved, it says so
        private void ShowSaveReplay()
        {
            if (saveReplay == null)
                return;
            BattleController battle_screen = BattleController.Instance;
            bool saved = battle_screen != null && battle_screen.ReplaySaved;
            bool can = battle_screen != null && battle_screen.CanSaveReplay;
            if (saveReplay.interactable != can)
                saveReplay.interactable = can;
            TextMeshProUGUI label = saveReplay.GetComponentInChildren<TextMeshProUGUI>();
            string text = saved ? "Replay saved" : "Save replay";
            if (label.text != text)
                label.text = text;
        }

        //----------------------------------------Rematch----------------------------------------//
        private void Update()
        {
            ShowSaveReplay();
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

        // Rematch can be asked once the battle is over on both devices and the other player is there; under it, what the
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
        private void BuildScoreboard(RectTransform content, BattleController.BattleSide player, BattleController.BattleSide opponent)
        {
            UITheme theme = UITheme.Current;
            float width = SideColumn + 3 * RoundColumn + 60;
            RectTransform board = UIKit.Panel(content, "Scoreboard").Place(new Vector2(0.5f, 0.5f), new Vector2(0, -85), new Vector2(width, 70 + 2 * RowHeight + 20));

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

        //----------------------------------------Buttons----------------------------------------//
        // A replay's end: watch it again from the start, or back to the replays
        private void ShowReplayButtons(RectTransform content)
        {
            Button again = UIKit.Button(content, "Watch again", UIKit.ButtonStyle.Primary, () => Gwent.Replays.ReplayPlayer.Current?.Restart(), new Vector2(300, 72));
            ((RectTransform)again.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(-170, -350), new Vector2(300, 72));
            Button replays = UIKit.Button(content, "Replays", UIKit.ButtonStyle.Secondary, Gwent.Replays.ReplayPlayer.Leave, new Vector2(300, 72));
            ((RectTransform)replays.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(170, -350), new Vector2(300, 72));
            UIKit.Focus(again);
        }

        public void PlayAgain()
        {
            Time.timeScale = 1f;
            LoadingScreen.Load(SceneNames.Game);
        }

        public void ToMainMenu()
        {
            Time.timeScale = 1f;
            LanSession.Current?.Leave();
            LoadingScreen.Load(SceneNames.Menu);
        }
    }
}
