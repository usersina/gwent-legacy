using System;
using System.Collections.Generic;
using System.Linq;
using Gwent.Rules;
using UnityEngine;

namespace Gwent.Core
{
    // The player's settings, saved in PlayerPrefs and applied as soon as they change: audio (applied by GameAudio),
    // display, graphics (the effects: read where they play), game speed and the AI's difficulty. Window mode and resolution are only applied once the player has changed them, so Unity's own
    // window handling stays in charge until then.
    public static class GameSettings
    {
        private const string MasterVolumeKey = "masterVolume";
        private const string MusicVolumeKey = "musicVolume"; // same keys as the old audio buttons, so saved volumes carry over
        private const string SfxVolumeKey = "sfxVolume";
        private const string MuteInBackgroundKey = "muteInBackground";
        private const string FullscreenKey = "fullscreen";
        private const string ResolutionWidthKey = "resolutionWidth";
        private const string ResolutionHeightKey = "resolutionHeight";
        private const string FrameRateKey = "frameRate";
        private const string MenuScaleKey = "menuScale";
        private const string GameSpeedKey = "gameSpeed";
        private const string AiDifficultyKey = "aiDifficulty";
        private const string CensoredArtKey = "censoredArt";
        private const string CheckForUpdatesKey = "checkForUpdates";
        private const string ParticleDensityKey = "particleDensity";

        // Frame rate choices: VSync follows the screen, 0 is unlimited (desktop); phones have no VSync choice
        public const int VSync = -1;
        public static readonly int[] DesktopFrameRates = { VSync, 30, 60, 120, 144, 0 };
        public static readonly int[] MobileFrameRates = { 30, 60, 120 };
        public static readonly float[] MenuScales = { 0.9f, 1f, 1.15f, 1.3f };
        public static readonly string[] GameSpeedNames = { "Slow", "Normal", "Fast" };
        private static readonly float[] GameSpeedScales = { 0.75f, 1f, 3f };
        public static readonly string[] AiDifficultyNames = { "Easy", "Normal", "Hard" };

        // Raised after any change (GameAudio listens; the overlay canvas reads the menu size every frame)
        public static event Action Changed;

        public static bool IsMobile { get { return Application.isMobilePlatform; } }

        // In a browser: no window to size, no frame rate to choose (the browser paces the frames), nothing to quit
        public static bool IsWeb { get { return Application.platform == RuntimePlatform.WebGLPlayer; } }

        //----------------------------------------Audio (0..1)----------------------------------------//
        public static float MasterVolume
        {
            get { return PlayerPrefs.GetFloat(MasterVolumeKey, 1f); }
            set { SetFloat(MasterVolumeKey, Mathf.Clamp01(value)); }
        }

        public static float MusicVolume
        {
            get { return PlayerPrefs.GetFloat(MusicVolumeKey, 1f); }
            set { SetFloat(MusicVolumeKey, Mathf.Clamp01(value)); }
        }

        public static float SfxVolume
        {
            get { return PlayerPrefs.GetFloat(SfxVolumeKey, 1f); }
            set { SetFloat(SfxVolumeKey, Mathf.Clamp01(value)); }
        }

        public static bool MuteInBackground
        {
            get { return PlayerPrefs.GetInt(MuteInBackgroundKey, 1) == 1; }
            set { SetInt(MuteInBackgroundKey, value ? 1 : 0); }
        }

        //----------------------------------------Display----------------------------------------//
        public static bool Fullscreen
        {
            get { return PlayerPrefs.HasKey(FullscreenKey) ? PlayerPrefs.GetInt(FullscreenKey) == 1 : Screen.fullScreenMode != FullScreenMode.Windowed; }
            set
            {
                PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
                Saved(ApplyWindow);
            }
        }

        public static Vector2Int Resolution
        {
            get
            {
                int width = PlayerPrefs.GetInt(ResolutionWidthKey, 0);
                int height = PlayerPrefs.GetInt(ResolutionHeightKey, 0);
                return width > 0 && height > 0 ? new Vector2Int(width, height) : new Vector2Int(Screen.width, Screen.height);
            }
            set
            {
                PlayerPrefs.SetInt(ResolutionWidthKey, value.x);
                PlayerPrefs.SetInt(ResolutionHeightKey, value.y);
                Saved(ApplyWindow);
            }
        }

        // The screen's sizes, largest first (at least 1024x576), always including the current one
        public static List<Vector2Int> AvailableResolutions()
        {
            List<Vector2Int> sizes = Screen.resolutions
                .Select(r => new Vector2Int(r.width, r.height))
                .Where(r => r.x >= 1024 && r.y >= 576)
                .ToList();
            sizes.Add(Resolution);
            return sizes.Distinct().OrderByDescending(r => r.x * r.y).ThenByDescending(r => r.x).ToList();
        }

        public static int[] FrameRates { get { return IsMobile ? MobileFrameRates : DesktopFrameRates; } }

        public static int FrameRate
        {
            get { return PlayerPrefs.GetInt(FrameRateKey, IsMobile ? 60 : VSync); }
            set
            {
                PlayerPrefs.SetInt(FrameRateKey, value);
                Saved(ApplyFrameRate);
            }
        }

        public static string FrameRateLabel(int frame_rate)
        {
            return frame_rate == VSync ? "VSync" : frame_rate == 0 ? "Unlimited" : frame_rate + " FPS";
        }

        // Size of the menus and dialogs (the battle board and the deck builder always fit the screen)
        public static float MenuScale
        {
            get { return PlayerPrefs.GetFloat(MenuScaleKey, 1f); }
            set { SetFloat(MenuScaleKey, value); }
        }

        //----------------------------------------Graphics----------------------------------------//
        // The effects, each on or off (off: lighter, the battle going on at once; an effect that tells something keeps a
        // light stand-in, see where it plays), and how many particles they emit. A preset sets them all; it is Custom
        // when they match none. Without a choice of their own, a phone has Medium's and the rest High's.
        public enum Effect
        {
            AnimatedWeather, // off: The Witcher 3's still pictures of the weather
            StormLightning,  // Skellige Storm's flashes, bolts and thunder
            ClearWeatherSun, // Clear Weather's sun sweeping the weather away
            HeroLanding,     // off: the hero's edge flashes gold
            ScorchFire,      // off: the scorched cards char, without fire
            AbilityFlares,   // off: the ability's icon pops over the card
            MedicFlames,
            HornGlow,
            LandingWisps,
            ShineAndPulsing, // the light across a focused card, pulsing frames, the leader's crown swelling (the frames stay)
            MenuAnimations,  // the main menu's title coming in and its sheen, the rules coming in one after another
        }

        public enum GraphicsPreset { Low, Medium, High, Custom }

        public static readonly string[] GraphicsPresetNames = { "Low", "Medium", "High", "Custom" };
        private static readonly string[] EffectKeys =
        {
            "animatedWeather", "stormLightning", "clearWeatherSun", "heroLanding", "scorchFire", "abilityFlares", "medicFlames",
            "hornGlow", "landingWisps", "shineAndPulsing", "menuAnimations",
        };
        private static readonly float[] PresetDensities = { 0.3f, 0.6f, 1f };
        public const float MinParticleDensity = 0.2f;

        // The preset a player has before choosing
        public static GraphicsPreset DefaultGraphics { get { return IsMobile ? GraphicsPreset.Medium : GraphicsPreset.High; } }

        // An effect in a preset: Low keeps only the horn's glow, Medium and High have them all
        private static bool InPreset(GraphicsPreset preset, Effect effect)
        {
            return preset != GraphicsPreset.Low || effect == Effect.HornGlow;
        }

        public static bool Shows(Effect effect)
        {
            return PlayerPrefs.GetInt(EffectKeys[(int)effect], InPreset(DefaultGraphics, effect) ? 1 : 0) == 1;
        }

        public static void SetShows(Effect effect, bool on)
        {
            SetInt(EffectKeys[(int)effect], on ? 1 : 0);
        }

        public static bool AnimatedWeather { get { return Shows(Effect.AnimatedWeather); } }

        // A share of the particles the effects would emit (from MinParticleDensity to 1)
        public static float ParticleDensity
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(ParticleDensityKey, PresetDensities[(int)DefaultGraphics]), MinParticleDensity, 1); }
            set { SetFloat(ParticleDensityKey, Mathf.Clamp(value, MinParticleDensity, 1)); }
        }

        // The preset the effects are at (Custom: none), or set them all to one (Custom: changes nothing)
        public static GraphicsPreset Graphics
        {
            get
            {
                for (GraphicsPreset preset = GraphicsPreset.Low; preset < GraphicsPreset.Custom; preset++)
                {
                    bool same = Mathf.Abs(ParticleDensity - PresetDensities[(int)preset]) < 0.005f;
                    for (int i = 0; same && i < EffectKeys.Length; i++)
                        same = Shows((Effect)i) == InPreset(preset, (Effect)i);
                    if (same)
                        return preset;
                }
                return GraphicsPreset.Custom;
            }
            set
            {
                if (value == GraphicsPreset.Custom)
                    return;
                PlayerPrefs.SetFloat(ParticleDensityKey, PresetDensities[(int)value]);
                for (int i = 0; i < EffectKeys.Length; i++)
                    PlayerPrefs.SetInt(EffectKeys[i], InPreset(value, (Effect)i) ? 1 : 0);
                RaiseChanged(); // once, for them all
            }
        }

        //----------------------------------------Game----------------------------------------//
        // How fast a battle runs: the cards' moves and effects, the notices and the opponent's turns (0 slow, 1 normal,
        // 2 fast). Clicks and holds keep their real-time feel.
        public static int GameSpeed
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(GameSpeedKey, 1), 0, GameSpeedScales.Length - 1); }
            set { SetInt(GameSpeedKey, Mathf.Clamp(value, 0, GameSpeedScales.Length - 1)); }
        }

        // The battle's time scale at that speed
        public static float GameTimeScale { get { return GameSpeedScales[GameSpeed]; } }

        // How well the opponent plays (AiBrain); from its next move on
        public static AiDifficulty AiDifficulty
        {
            get { return (AiDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(AiDifficultyKey, (int)AiDifficulty.Normal), 0, AiDifficultyNames.Length - 1); }
            set { SetInt(AiDifficultyKey, Mathf.Clamp((int)value, 0, AiDifficultyNames.Length - 1)); }
        }

        // The censored versions of the art that has them (Gwent.Core.CensoredArt): on unless turned off
        public static bool CensoredArt
        {
            get { return PlayerPrefs.GetInt(CensoredArtKey, 1) == 1; }
            set { SetInt(CensoredArtKey, value ? 1 : 0); }
        }

        // On launch, whether a newer build is out (Gwent.Updates.UpdateCheck): on unless turned off
        public static bool CheckForUpdates
        {
            get { return PlayerPrefs.GetInt(CheckForUpdatesKey, 1) == 1; }
            set { SetInt(CheckForUpdatesKey, value ? 1 : 0); }
        }

        //----------------------------------------Apply / reset----------------------------------------//
        public static void ResetToDefaults()
        {
            bool window_was_set = WindowSet;
            foreach (string key in new[] { MasterVolumeKey, MusicVolumeKey, SfxVolumeKey, MuteInBackgroundKey, FullscreenKey,
                                           ResolutionWidthKey, ResolutionHeightKey, FrameRateKey, MenuScaleKey, ParticleDensityKey,
                                           GameSpeedKey, AiDifficultyKey, CensoredArtKey, CheckForUpdatesKey }.Concat(EffectKeys))
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            ApplyFrameRate();
            // Back to the game's default window: fullscreen at the screen's own resolution
            if (window_was_set && CanResizeWindow)
                Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
            RaiseChanged();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStartup()
        {
            ApplyFrameRate();
            if (WindowSet)
                ApplyWindow();
            GameLog.Event(LogCategory.Game, "settings", "values", Describe());
        }

        // Every setting as it stands, for the log
        public static System.Collections.Generic.Dictionary<string, object> Describe()
        {
            Dictionary<string, object> values = new Dictionary<string, object>
            {
                { "master_volume", MasterVolume }, { "music_volume", MusicVolume }, { "sfx_volume", SfxVolume },
                { "mute_in_background", MuteInBackground }, { "fullscreen", Fullscreen }, { "resolution", Resolution.x + "x" + Resolution.y },
                { "frame_rate", FrameRate }, { "menu_scale", MenuScale }, { "graphics", GraphicsPresetNames[(int)Graphics] },
                { "particle_density", ParticleDensity }, { "game_speed", GameSpeedNames[GameSpeed] }, { "difficulty", AiDifficulty },
                { "censored_art", CensoredArt }, { "check_for_updates", CheckForUpdates },
            };
            foreach (Effect effect in Enum.GetValues(typeof(Effect)))
                values[EffectKeys[(int)effect]] = Shows(effect);
            return values;
        }

        private static bool WindowSet { get { return PlayerPrefs.HasKey(FullscreenKey) || PlayerPrefs.HasKey(ResolutionWidthKey); } }

        // The editor's Game view and phones have no window to resize
        private static bool CanResizeWindow { get { return !IsMobile && !IsWeb && !Application.isEditor; } }

        private static void ApplyFrameRate()
        {
            if (IsWeb)
            {
                Application.targetFrameRate = -1;
                return;
            }
            int frame_rate = FrameRate;
            QualitySettings.vSyncCount = frame_rate == VSync && !IsMobile ? 1 : 0;
            Application.targetFrameRate = frame_rate > 0 ? frame_rate : -1;
        }

        private static void ApplyWindow()
        {
            if (!CanResizeWindow)
                return;
            FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Vector2Int size = Resolution;
            if (Screen.width != size.x || Screen.height != size.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(size.x, size.y, mode);
        }

        private static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            Saved(null);
        }

        private static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            Saved(null);
        }

        // Written to disk when the settings close (a slider changes the value every frame while dragged)
        private static void Saved(Action apply)
        {
            if (apply != null)
                apply();
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            if (Changed != null)
                Changed();
        }
    }
}
