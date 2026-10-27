using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Net;
using Gwent.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Gwent.UI
{
    // The way from one screen to the next (the menu, the deck builder, a battle): the next screen's backdrop, dimmed, under
    // a notice in the battle's style. A battle shows both sides' crests either side of "Preparing the battle"; the menu and
    // the deck builder the hourglass and "Loading". It comes up over the screen left, the next one loads behind it, and
    // once that one is set up (the cards on show have their art, a battle its hand's, its first frames drawn) it fades up
    // and goes on (a battle opens: "You will go first"). Setting a screen up holds the game for a moment (most of the wait
    // on a phone), so nothing on the loading screen moves meanwhile: nothing looks stuck. Above every screen and dialog,
    // it takes the clicks and keys (UIOverlay.HasModal).
    public class LoadingScreen : MonoBehaviour
    {
        private const int SortingOrder = 900;     // above the menus (400), the battle's overlays (500) and dialogs (600)
        private const float FadeIn = 0.3f;
        private const float FadeOut = 0.6f;        // the next screen fading up
        private const float NoticeOut = 0.25f;     // the notice goes first
        private const float ShortestBattle = 1.2f; // a battle's shows this long at least (from asking): its crests are read
        private const float Shortest = 0.6f;       // the others' (one word)
        private const float ArtWait = 5f;          // the cards' art is waited for this long at most (the web downloads it)
        private const float Dim = 0.72f;           // the veil over the backdrop: the next screen barely visible
        private const float CrestX = 400;          // the crests' centres, from the middle
        private const float ArtCentre = 48;        // the emblems' art sits this far right of their frame's middle
        private const float HourglassWidth = 161;  // the hourglass's art, across
        private const float Gap = 36;              // between the hourglass and "Loading"

        private static LoadingScreen instance;

        private CanvasGroup screen;
        private CanvasGroup notice;

        // While it shows (a battle's opening waits)
        public static bool Showing { get { return instance != null; } }

        // The last screen's loading, from asking for its scene to its first frame drawn (seconds)
        public static float LastLoad { get; private set; }

        // Goes to the scene behind the loading screen (once: asking again while it shows does nothing)
        public static void Load(string scene)
        {
            if (instance != null)
                return;
            GameObject go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LoadingScreen>();
            instance.Build(scene);
            instance.Run(scene, go.GetCancellationTokenOnDestroy()).Forget();
        }

        private void Build(string scene)
        {
            UITheme theme = UITheme.Current;
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // the scene canvases' (the backdrop falls where theirs does)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            screen = gameObject.AddComponent<CanvasGroup>();
            screen.alpha = 0;

            // The next screen's backdrop, as it lays it: the battle's table and the menu's key art (covering the screen),
            // the deck builder's plain background
            RectTransform root = (RectTransform)transform;
            Image under = UIKit.NewImage(root, "Under", null, scene == SceneNames.Deck ? theme.background : Color.black);
            under.rectTransform.Stretch();
            under.raycastTarget = true; // takes the clicks
            if (scene == SceneNames.Game)
            {
                Image table = UIKit.NewImage(root, "Table", theme.table, Color.white);
                table.rectTransform.Cover(16f / 9f);
                table.raycastTarget = false;
            }
            else if (scene == SceneNames.Menu)
            {
                Image art = UIKit.NewImage(root, "KeyArt", CensoredArt.KeyArt(), Color.white);
                if (art.sprite != null)
                    art.rectTransform.Cover(art.sprite.rect.width / art.sprite.rect.height);
                art.raycastTarget = false;
            }
            Image veil = UIKit.NewImage(root, "Veil", null, new Color(0, 0, 0, Dim));
            veil.rectTransform.Stretch();
            veil.raycastTarget = false;

            RectTransform band = BattleBanner.Band("Notice", root);
            if (scene == SceneNames.Game)
            {
                // The decks the battle will play: a replay's two, a LAN battle's, or the player's and the AI's (a random one
                // is picked now)
                Gwent.Replays.ReplayPlayer replay = Gwent.Replays.ReplayPlayer.Current;
                LanSession lan = replay == null && LanSession.Current != null && LanSession.Current.State == LanSession.Stage.Battle ? LanSession.Current : null;
                Deck mine = replay != null ? replay.MyDeck : lan != null ? lan.MyDeck : BattleController.PlayerDeck(CardLibrary.Get(), out _);
                Deck opponent = replay != null ? replay.TheirDeck : lan != null ? lan.TheirDeck : null;
                if (replay == null && lan == null)
                {
                    TextAsset theirs = OpponentDecks.PickNext();
                    opponent = theirs != null ? CardLibrary.ParseDeck(theirs.text, theirs.name) : null;
                }
                BattleBanner.Emblem(band, -CrestX - ArtCentre, Crest(mine));
                BattleBanner.Emblem(band, CrestX - ArtCentre, Crest(opponent));
                BattleBanner.Text(band, replay != null ? "Preparing the replay" : "Preparing the battle");
            }
            else
            {
                // The hourglass and the word, centred together
                TextMeshProUGUI text = BattleBanner.Text(band, "Loading");
                float width = text.GetPreferredValues("Loading").x;
                float left = -(HourglassWidth + Gap + width) / 2;
                BattleBanner.Emblem(band, left + HourglassWidth / 2 - ArtCentre, Resources.Load<Sprite>("W3/Notifications/" + BattleBanner.RoundStart));
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.rectTransform.pivot = new Vector2(0, 0.5f);
                text.rectTransform.anchoredPosition = new Vector2(left + HourglassWidth + Gap, 0);
                text.transform.SetAsLastSibling();
            }
            foreach (Graphic graphic in band.GetComponentsInChildren<Graphic>())
                graphic.raycastTarget = false;
            notice = band.gameObject.AddComponent<CanvasGroup>();
        }

        private static Sprite Crest(Deck deck)
        {
            string emblem = deck != null ? BattleBanner.FactionEmblem(deck.Faction) : null;
            return emblem != null ? Resources.Load<Sprite>("W3/Notifications/" + emblem) : null;
        }

        private async UniTaskVoid Run(string scene, CancellationToken ct)
        {
            try
            {
                float asked = Time.realtimeSinceStartup;
                await Fade(screen, 1, FadeIn, ct);
                await UniTask.Yield(PlayerLoopTiming.Update, ct); // drawn whole before the next screen holds the game

                BattleController before = BattleController.Instance; // Play again: the battle being left
                float loading = Time.realtimeSinceStartup;
                await SceneManager.LoadSceneAsync(scene).ToUniTask(cancellationToken: ct);
                BattleController battle = null;
                if (scene == SceneNames.Game)
                {
                    await UniTask.WaitUntil(() => BattleController.Instance != null && BattleController.Instance != before, cancellationToken: ct);
                    battle = BattleController.Instance;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct); // its first frame has been drawn
                LastLoad = Time.realtimeSinceStartup - loading;
                GameLog.Event(LogCategory.Game, "scene_loaded", "scene", scene, "seconds", LastLoad.ToString("0.00"));

                while ((CardArt.Waiting || battle != null && !battle.HandArtLoaded) && Time.realtimeSinceStartup - loading < ArtWait)
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                for (int frame = 0; frame < 2; frame++) // the cards' art drawn, the first frames' work done
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float shortest = scene == SceneNames.Game ? ShortestBattle : Shortest;
                while (Time.realtimeSinceStartup - asked < shortest)
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);

                Fade(notice, 0, NoticeOut, ct).Forget();
                await Fade(screen, 0, FadeOut, ct);
            }
            finally
            {
                if (this != null)
                    Destroy(gameObject);
            }
        }

        // A group's alpha to the one given, eased over that long (real time: the game speed does not apply)
        private static async UniTask Fade(CanvasGroup group, float to, float seconds, CancellationToken ct)
        {
            float from = group.alpha;
            for (float age = 0; age < seconds; age += Time.unscaledDeltaTime)
            {
                if (group == null)
                    return;
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, age / seconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (group != null)
                group.alpha = to;
        }
    }
}
