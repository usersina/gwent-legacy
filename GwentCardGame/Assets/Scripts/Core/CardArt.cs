using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Gwent.Rules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

namespace Gwent.Core
{
    // The cards' full art (the whole card: the previews, the showcase, the pickers, the deck builder), from Addressables
    // at "CardArt/<id>", a bundle per card (see GwentContent), or its censored version "CardArt/<id>-censored" when there
    // is one and Censored artwork is on (CensoredArt). A card's art is loaded the first time it is shown, and released
    // once no open scene has shown it. Until it is there the image shows the back of the card's faction: nothing waits
    // for it, and on the web a card turns face up as its art arrives. Which cards have a censored version is read from
    // the content's catalog first (an original never shows while the setting is on); the setting changed, every image
    // on show turns to the other version (the one it showed staying until the other is there). A battle asks for both
    // decks' art as it starts (Preload), so its cards are face up by the time they are seen.
    public static class CardArt
    {
        private class Entry
        {
            public AsyncOperationHandle<Sprite> handle;
            public readonly HashSet<Scene> scenes = new HashSet<Scene>(); // the open scenes that asked for it
        }

        // An image or element showing a card's art: how to show it there again
        private class Target
        {
            public int card;
            public Scene scene;
            public Action<Sprite> show;
            public Func<bool> alive;
        }

        private const string Prefix = "CardArt/";
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(); // by address
        private static readonly Dictionary<object, string> waiting = new Dictionary<object, string>(); // the art each image or element waits for
        private static readonly Dictionary<object, Target> targets = new Dictionary<object, Target>();
        private static readonly HashSet<int> censored = new HashSet<int>(); // the cards with a censored version
        private static bool catalogRead;
        private static event Action CatalogRead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            entries.Clear();
            waiting.Clear();
            targets.Clear();
            censored.Clear();
            catalogRead = false;
            CatalogRead = null;
            SceneManager.sceneUnloaded -= Unloaded;
            SceneManager.sceneUnloaded += Unloaded;
            CensoredArt.Changed -= ShowAgain;
            CensoredArt.Changed += ShowAgain;
        }

        // The cards with a censored version, from the catalog of the content (a catalog that fails to load: none)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ReadCatalog()
        {
            Addressables.InitializeAsync().Completed += done =>
            {
                foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator locator in Addressables.ResourceLocators)
                    foreach (object key in locator.Keys)
                        if (key is string address && address.StartsWith(Prefix) && address.EndsWith(CensoredArt.Suffix)
                            && int.TryParse(address.Substring(Prefix.Length, address.Length - Prefix.Length - CensoredArt.Suffix.Length), out int card_id))
                            censored.Add(card_id);
                catalogRead = true;
                Action read = CatalogRead;
                CatalogRead = null;
                read?.Invoke();
            };
        }

        // A card's art in the content (the original)
        public static string Address(int card_id)
        {
            return Prefix + card_id;
        }

        // The art a card shows: its censored version when the setting is on and it has one
        private static string Shown(int card_id)
        {
            return CensoredArt.On && censored.Contains(card_id) ? Address(card_id) + CensoredArt.Suffix : Address(card_id);
        }

        // Shows the card's art on the image: at once when it is loaded, otherwise the card's back until it is
        public static void Show(Image image, int card_id)
        {
            Show(image, image.gameObject.scene, card_id, sprite => image.sprite = sprite, () => image != null);
        }

        // The same as the background of a UI Toolkit element
        public static void Show(VisualElement element, int card_id)
        {
            // Gone with its dialog (the lobby closed as the art came): not shown
            Show(element, SceneManager.GetActiveScene(), card_id, sprite => element.style.backgroundImage = new StyleBackground(sprite), () => element.panel != null);
        }

        // A leader's round portrait (UI Toolkit): framed on the face of the version it shows (LeaderFaces), framed again
        // when the setting switches it; no card back while the art loads: the ring stays empty, then the face fades in
        // (portrait-art, Gwent.uss)
        public static void ShowPortrait(VisualElement element, int leader_id)
        {
            LeaderFaces.Frame(element, leader_id, ShowsCovered(leader_id));
            element.AddToClassList("portrait-art");
            element.style.opacity = 0;
            Show(element, SceneManager.GetActiveScene(), leader_id, sprite =>
            {
                LeaderFaces.Frame(element, leader_id, ShowsCovered(leader_id));
                element.style.backgroundImage = new StyleBackground(sprite);
                element.style.opacity = 1;
            }, () => element.panel != null, true);
        }

        // The same in a uGUI round mask (the battle's side panels, the end screen)
        public static void ShowPortrait(Image image, int leader_id)
        {
            Show(image, image.gameObject.scene, leader_id, sprite =>
            {
                image.sprite = sprite;
                LeaderFaces.Frame(image.rectTransform, leader_id, ShowsCovered(leader_id));
            }, () => image != null);
        }

        // The card shows its covered version (the setting is on and it has one)
        private static bool ShowsCovered(int card_id)
        {
            return Shown(card_id) != Address(card_id);
        }

        // keep: what it shows stays until the art is there (the other version of the same card, the setting changed)
        private static void Show(object target, Scene scene, int card_id, Action<Sprite> show, Func<bool> alive, bool keep = false)
        {
            targets[target] = new Target { card = card_id, scene = scene, show = show, alive = alive };
            if (!catalogRead)
            {
                // Which version it shows is known once the catalog is read
                if (!keep)
                    show(Back(card_id));
                string token = "catalog " + card_id;
                waiting[target] = token;
                CatalogRead += () =>
                {
                    if (alive() && waiting.TryGetValue(target, out string still) && still == token)
                        Show(target, scene, card_id, show, alive, true);
                };
                return;
            }
            string address = Shown(card_id);
            Entry entry = Request(address, scene);
            if (entry.handle.IsDone)
            {
                waiting.Remove(target);
                show(entry.handle.Status == AsyncOperationStatus.Succeeded ? entry.handle.Result : Back(card_id));
                return;
            }
            if (!keep)
                show(Back(card_id));
            waiting[target] = address;
            entry.handle.Completed += loaded =>
            {
                // The image may be gone, or show another card (or the other version) by now
                if (!alive() || !waiting.TryGetValue(target, out string wanted) || wanted != address)
                    return;
                waiting.Remove(target);
                if (loaded.Status == AsyncOperationStatus.Succeeded)
                    show(loaded.Result);
            };
        }

        // Censored artwork turned on or off: every image on show, the other version
        private static void ShowAgain()
        {
            foreach (KeyValuePair<object, Target> pair in targets.ToList())
            {
                Target target = pair.Value;
                if (Gone(pair.Key) || !target.alive())
                    targets.Remove(pair.Key);
                else
                    Show(pair.Key, target.scene, target.card, target.show, target.alive, true);
            }
        }

        // Whether an image or element on show still waits for its art (the loading screen waits for the next screen's)
        public static bool Waiting
        {
            get
            {
                foreach (object gone in waiting.Keys.Where(Gone).ToList())
                    waiting.Remove(gone);
                return waiting.Count > 0;
            }
        }

        // Loads the cards' art ahead of being shown (a battle's decks), for the scene open now. Completes once all of it
        // is there; a card whose art failed to load (Addressables logs why) keeps its back.
        public static async UniTask Preload(IEnumerable<int> card_ids, string what, CancellationToken ct)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Scene scene = SceneManager.GetActiveScene();
            await UniTask.WaitUntil(() => catalogRead, cancellationToken: ct);
            List<UniTask> loads = new List<UniTask>();
            int cards = 0;
            foreach (int card_id in card_ids.Distinct())
            {
                cards++;
                Entry entry = Request(Shown(card_id), scene);
                if (!entry.handle.IsDone)
                    loads.Add(Loaded(entry.handle, ct));
            }
            int loading = loads.Count;
            await UniTask.WhenAll(loads);
            GameLog.Event(LogCategory.Perf, "card_art_loaded", "for", what, "cards", cards, "loaded", loading, "ms", watch.ElapsedMilliseconds);
        }

        private static async UniTask Loaded(AsyncOperationHandle<Sprite> handle, CancellationToken ct)
        {
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // Addressables logged it already
            }
        }

        private static Entry Request(string address, Scene scene)
        {
            Entry entry;
            if (!entries.TryGetValue(address, out entry))
            {
                entry = new Entry { handle = Addressables.LoadAssetAsync<Sprite>(address) };
                entries[address] = entry;
            }
            entry.scenes.Add(scene);
            return entry;
        }

        // A scene closed: the art no other open scene asked for is released (its bundle unloads)
        private static void Unloaded(Scene scene)
        {
            foreach (string address in entries.Keys.ToList())
            {
                Entry entry = entries[address];
                entry.scenes.Remove(scene);
                if (entry.scenes.Count > 0)
                    continue;
                Addressables.Release(entry.handle);
                entries.Remove(address);
            }
            foreach (object gone in waiting.Keys.Where(Gone).ToList())
                waiting.Remove(gone);
            foreach (object gone in targets.Keys.Where(Gone).ToList())
                targets.Remove(gone);
        }

        // A destroyed image, or an element no longer on a panel
        private static bool Gone(object target)
        {
            VisualElement element = target as VisualElement;
            if (element != null)
                return element.panel == null;
            return target as UnityEngine.Object == null; // Unity's null: destroyed
        }

        // The back of the card's faction (special cards have the plain one)
        public static Sprite Back(int card_id)
        {
            CardDatabase db = CardLibrary.Get();
            Sprite back = db.Contains(card_id) ? SpriteCache.Load("Cards/Back/" + db.Get(card_id).faction.Code()) : null;
            return back != null ? back : SpriteCache.Load("Cards/Back/BACK");
        }
    }
}
