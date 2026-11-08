using System;
using System.Collections.Generic;
using System.Linq;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // The settings screen (Game, Display, Graphics, Audio; it opens on Game), opened from the main menu and the pause menu.
    // Every change applies and saves at once; Escape, right click or Back closes it.
    // A UI Toolkit dialog: Assets/UI/Toolkit/Settings.uxml; its rows are made here, they depend on the platform.
    public static class SettingsPanel
    {
        private static readonly string[] Tabs = { "Game", "Display", "Graphics", "Audio" };

        // The rows, captions, groups and options are written here in English, which is also what names them (the autoplay
        // finds "Row Difficulty"); what is shown is the UI table's entry under the key given here
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>
        {
            { "Language", "settings.language" },
            { "Takes effect at once", "settings.language.caption" },
            { "Game speed", "settings.speed" },
            { "Cards, notices and the opponent", "settings.speed.caption" },
            { "Speed/Slow", "settings.speed.slow" },
            { "Speed/Normal", "settings.speed.normal" },
            { "Speed/Fast", "settings.speed.fast" },
            { "Difficulty", "settings.difficulty" },
            { "Difficulty/Easy", "settings.difficulty.easy" },
            { "Difficulty/Normal", "settings.difficulty.normal" },
            { "Difficulty/Hard", "settings.difficulty.hard" },
            { "Your opponent plays like a beginner", "settings.difficulty.easy.caption" },
            { "Your opponent saves cards for later rounds", "settings.difficulty.normal.caption" },
            { "Your opponent thinks the battle through", "settings.difficulty.hard.caption" },
            { "Censored artwork", "settings.censored" },
            { "Covered versions of some cards' art", "settings.censored.caption" },
            { "Check for updates", "settings.updates" },
            { "On launch, from the game's page on GitHub", "settings.updates.caption" },
            { "Master volume", "settings.volume.master" },
            { "Music", "settings.volume.music" },
            { "Sound effects", "settings.volume.sfx" },
            { "Mute when in the background", "settings.mute" },
            { "When another window is in front", "settings.mute.caption" },
            { "Fullscreen", "settings.fullscreen" },
            { "Esc leaves it", "settings.fullscreen.caption" },
            { "Window", "settings.window" },
            { "Windowed", "settings.windowed" },
            { "Resolution", "settings.resolution" },
            { "Frame rate", "settings.framerate" },
            { "Unlimited", "settings.framerate.unlimited" },
            { "Lower values save battery", "settings.framerate.battery" },
            { "VSync follows your screen", "settings.framerate.vsync" },
            { "Menu size", "settings.menusize" },
            { "Menus and dialogs only", "settings.menusize.caption" },
            { "Preset", "settings.preset" },
            { "Sets everything below", "settings.preset.caption" },
            { "Preset/Low", "settings.preset.low" },
            { "Preset/Medium", "settings.preset.medium" },
            { "Preset/High", "settings.preset.high" },
            { "Preset/Custom", "settings.preset.custom" },
            { "Particle density", "settings.particles" },
            { "Fewer sparks, flames and raindrops", "settings.particles.caption" },
            { "Weather", "settings.group.weather" },
            { "Battle effects", "settings.group.battle" },
            { "Motion", "settings.group.motion" },
            { "Menus", "settings.group.menus" },
            { "Animated weather", "settings.fx.weather" },
            { "Off: still pictures", "settings.fx.weather.caption" },
            { "Lightning and thunder", "settings.fx.storm" },
            { "In Skellige Storm", "settings.fx.storm.caption" },
            { "Clear Weather's sun", "settings.fx.sun" },
            { "The light sweeping the weather away", "settings.fx.sun.caption" },
            { "Hero landing", "settings.fx.hero" },
            { "Off: the card's edge flashes gold", "settings.fx.hero.caption" },
            { "Scorch fire", "settings.fx.scorch" },
            { "Off: the cards char, without fire", "settings.fx.scorch.caption" },
            { "Ability flares", "settings.fx.flares" },
            { "Off: the ability's icon only", "settings.fx.flares.caption" },
            { "Medic flames", "settings.fx.medic" },
            { "Over the discard pile", "settings.fx.medic.caption" },
            { "Horn glow", "settings.fx.horn" },
            { "Along a horned row", "settings.fx.horn.caption" },
            { "Landing wisps", "settings.fx.wisps" },
            { "As a card lands", "settings.fx.wisps.caption" },
            { "Shine and pulsing", "settings.fx.shine" },
            { "The light across a card, pulsing frames", "settings.fx.shine.caption" },
            { "Menu animations", "settings.fx.menu" },
            { "The title, the rules coming in", "settings.fx.menu.caption" },
        };

        private static string S(string english)
        {
            return Loc.T(Loc.UI, Keys[english]);
        }

        // The names of an option list (GameSettings' names, which the log keeps in English) in the current language
        private static string[] Options(string group, string[] names)
        {
            return names.Select(name => S(group + "/" + name)).ToArray();
        }

        // What each difficulty means, under its name
        private static readonly string[] DifficultyCaptions =
        {
            "Your opponent plays like a beginner",
            "Your opponent saves cards for later rounds",
            "Your opponent thinks the battle through",
        };

        private static GameObject dialog;
        private static Action onClosed;
        private static int currentTab;

        public static bool IsOpen { get { return dialog != null; } }

        public static void Open(Action on_closed = null, int tab = 0)
        {
            onClosed = on_closed;
            currentTab = tab;
            dialog = UIScreens.ShowDialog("Settings", UITheme.Current.settings, Bind, OnClosed);
        }

        public static void Close()
        {
            if (dialog != null)
                UIOverlay.CloseModal(dialog);
        }

        private static void OnClosed()
        {
            PlayerPrefs.Save();
            GameLog.Event(LogCategory.Ui, "settings_saved", "values", GameSettings.Describe());
            dialog = null;
            Action closed = onClosed;
            onClosed = null;
            if (closed != null)
                closed();
        }

        //----------------------------------------Layout----------------------------------------//
        private static void Bind(VisualElement root)
        {
            Focusable[] first =
            {
                BuildGamePage(root.Q("Game")),
                BuildDisplayPage(root.Q("Display")),
                BuildGraphicsPage(root.Q("Graphics")),
                BuildAudioPage(root.Q("Audio")),
            };
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                root.Q<Button>("Button " + Tabs[i]).clicked += () =>
                {
                    GameAudio.PlaySfx("ui_click");
                    ShowTab(root, index);
                };
            }
            root.Q<Button>("Button Reset to defaults").clicked += ResetToDefaults;
            Button close = root.Q<Button>("Button Close");
            close.clicked += Close;

            ShowTab(root, currentTab);
            root.RegisterCallback<NavigationMoveEvent>(change => Navigate(root, change), TrickleDown.TrickleDown);
            VisualElement focus = (first[currentTab] as VisualElement) ?? root.Q("Button Reset to defaults");
            root.schedule.Execute(() => UIScreens.Focus(focus));
        }

        // The keyboard on the settings, as the uGUI menus had it: Up and Down go from row to row (Unity's spatial
        // navigation wants the controls to overlap across, and would skip a switch at the far right), from the tabs to the
        // first row and from the last row to Reset to defaults, and stop there (Up from the tabs: the ×); Left and Right
        // move a volume by a tenth.
        private static void Navigate(VisualElement root, NavigationMoveEvent move)
        {
            VisualElement focused = root.focusController != null ? root.focusController.focusedElement as VisualElement : null;
            if (focused == null)
                return;
            bool horizontal = move.direction == NavigationMoveEvent.Direction.Left || move.direction == NavigationMoveEvent.Direction.Right;
            if (horizontal)
            {
                Slider slider = focused as Slider;
                if (slider == null)
                    return;
                slider.value = Mathf.Clamp01(slider.value + (move.direction == NavigationMoveEvent.Direction.Right ? 0.1f : -0.1f));
                move.StopPropagation();
                root.focusController.IgnoreEvent(move);
                return;
            }
            if (move.direction != NavigationMoveEvent.Direction.Up && move.direction != NavigationMoveEvent.Direction.Down)
                return;

            // The stops from top to bottom: the ×, the open tab, the page's rows, the footer (Reset to defaults)
            List<VisualElement> stops = new List<VisualElement> { root.Q("Button Close"), root.Q("Button " + Tabs[currentTab]) };
            stops.AddRange(root.Q(Tabs[currentTab]).Query(className: "setting__control").ToList());
            stops.Add(root.Q("Button Reset to defaults"));
            bool on_tabs = Tabs.Any(tab => root.Q("Button " + tab) == focused);
            int index = on_tabs ? 1 : stops.FindIndex(stop => stop == focused || stop.Contains(focused));
            if (index < 0)
                return;
            move.StopPropagation();
            root.focusController.IgnoreEvent(move); // or Unity's own navigation would move on from there as well
            int next = index + (move.direction == NavigationMoveEvent.Direction.Down ? 1 : -1);
            if (next < 0 || next >= stops.Count)
                return; // the top and the bottom hold (no wrapping round)
            // In a row, the control the focus lands on: a stepper keeps the side it was on, if it can
            VisualElement target = stops[next];
            Stepper stepper = target.Q<Stepper>();
            bool right_side = focused.name == "Right";
            if (stepper != null)
                target = right_side && stepper.Right.enabledSelf || !stepper.Left.enabledSelf ? stepper.Right : stepper.Left;
            else if (target.ClassListContains("setting__control"))
                target = target.Query<VisualElement>().Where(element => element.focusable && element.canGrabFocus).First() ?? target;
            target.Focus();
            // On a page that scrolls (Graphics), the row comes into view
            ScrollView scroll = target.GetFirstAncestorOfType<ScrollView>();
            VisualElement row = target.GetFirstAncestorOfType<VisualElement>();
            while (row != null && !row.ClassListContains("setting"))
                row = row.parent;
            if (scroll != null && row != null)
                scroll.ScrollTo(row);
        }

        private static void ShowTab(VisualElement root, int index)
        {
            currentTab = index;
            for (int i = 0; i < Tabs.Length; i++)
            {
                root.Q(Tabs[i]).style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                root.Q("Button " + Tabs[i]).EnableInClassList("tab--selected", i == index);
            }
        }

        // A labelled row (a caption under the label, if any); returns the place on the right where the control goes
        private static VisualElement Row(VisualElement page, string label, string caption = null)
        {
            VisualElement row = new VisualElement { name = "Row " + label };
            row.AddToClassList("setting");
            VisualElement labels = new VisualElement();
            labels.AddToClassList("setting__labels");
            Label text = new Label(S(label));
            text.AddToClassList("setting__label");
            labels.Add(text);
            if (caption != null)
            {
                Label small = new Label(S(caption)) { name = "Caption" };
                small.AddToClassList("caption");
                labels.Add(small);
            }
            row.Add(labels);
            VisualElement control = new VisualElement { name = "Control" };
            control.AddToClassList("setting__control");
            row.Add(control);
            page.Add(row);
            return control;
        }

        private static Slider VolumeRow(VisualElement page, string label, float value, Action<float> set)
        {
            VisualElement control = Row(page, label);
            Slider slider = new Slider(0, 1) { name = "Slider", fill = true };
            slider.AddToClassList("volume");
            slider.SetValueWithoutNotify(value);
            Label percent = new Label();
            percent.AddToClassList("setting__percent");
            Action<float> show = v => percent.text = Mathf.RoundToInt(v * 100) + "%";
            show(value);
            slider.RegisterValueChangedCallback(change =>
            {
                set(change.newValue);
                show(change.newValue);
            });
            control.Add(slider);
            control.Add(percent);
            return slider;
        }

        private static Switch SwitchRow(VisualElement page, string label, string caption, bool value, Action<bool> set)
        {
            Switch toggle = new Switch { name = "Switch" };
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(change => set(change.newValue));
            Row(page, label, caption).Add(toggle);
            return toggle;
        }

        private static Stepper StepperRow(VisualElement page, string label, string[] options, int selected, Action<int> set, string caption = null)
        {
            Stepper stepper = new Stepper { name = "Stepper" };
            stepper.Set(options, selected, set);
            Row(page, label, caption).Add(stepper);
            return stepper;
        }

        //----------------------------------------Pages----------------------------------------//
        // Each returns the control the keyboard starts on

        private static Focusable BuildAudioPage(VisualElement page)
        {
            Slider master = VolumeRow(page, "Master volume", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);
            VolumeRow(page, "Music", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
            VolumeRow(page, "Sound effects", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v);
            if (!GameSettings.IsMobile)
                SwitchRow(page, "Mute when in the background", "When another window is in front", GameSettings.MuteInBackground, v => GameSettings.MuteInBackground = v);
            return master;
        }

        private static Focusable BuildDisplayPage(VisualElement page)
        {
            Focusable first = null;
            if (GameSettings.IsWeb && !GameSettings.IsMobile)
            {
                // The browser decides the rest; fullscreen follows a click (browsers allow it only then). In a phone's
                // browser the page itself goes fullscreen at a tap, in landscape (the web template's index.html)
                first = SwitchRow(page, "Fullscreen", "Esc leaves it", Screen.fullScreen, v => Screen.fullScreen = v);
            }
            else if (!GameSettings.IsMobile)
            {
                Stepper window = StepperRow(page, "Window", new[] { S("Fullscreen"), S("Windowed") }, GameSettings.Fullscreen ? 0 : 1,
                    i => GameSettings.Fullscreen = i == 0);
                first = window.Left;

                List<Vector2Int> sizes = GameSettings.AvailableResolutions();
                Vector2Int current = GameSettings.Resolution;
                StepperRow(page, "Resolution", sizes.Select(r => r.x + " × " + r.y).ToArray(), Mathf.Max(0, sizes.IndexOf(current)),
                    i => GameSettings.Resolution = sizes[i]);
            }

            if (!GameSettings.IsWeb)
            {
                int[] rates = GameSettings.FrameRates;
                Stepper frame_rate = StepperRow(page, "Frame rate", rates.Select(FrameRateLabel).ToArray(),
                    Mathf.Max(0, Array.IndexOf(rates, GameSettings.FrameRate)), i => GameSettings.FrameRate = rates[i],
                    GameSettings.IsMobile ? "Lower values save battery" : "VSync follows your screen");
                if (first == null)
                    first = frame_rate.Left;
            }

            float[] scales = GameSettings.MenuScales;
            int scale_index = Array.FindIndex(scales, s => Mathf.Approximately(s, GameSettings.MenuScale));
            Stepper menu_size = StepperRow(page, "Menu size", scales.Select(s => Mathf.RoundToInt(s * 100) + "%").ToArray(), scale_index < 0 ? 1 : scale_index,
                i => GameSettings.MenuScale = scales[i], "Menus and dialogs only");
            return first ?? menu_size.Left;
        }

        // The effects: a preset on top that sets them all (and shows Custom when they match none), how many particles,
        // then each effect on or off, under headings (all shown, none folded away)
        private static Focusable BuildGraphicsPage(VisualElement page)
        {
            Stepper preset = null;
            Slider density = null;
            Label density_percent = null;
            Dictionary<GameSettings.Effect, Switch> switches = new Dictionary<GameSettings.Effect, Switch>();
            Action show_preset = () => preset.Show((int)GameSettings.Graphics);

            preset = StepperRow(page, "Preset", Options("Preset", GameSettings.GraphicsPresetNames), (int)GameSettings.Graphics, i =>
            {
                GameSettings.Graphics = (GameSettings.GraphicsPreset)i; // Custom: everything stays as it is
                density.SetValueWithoutNotify(GameSettings.ParticleDensity);
                density_percent.text = Percent(GameSettings.ParticleDensity);
                foreach (KeyValuePair<GameSettings.Effect, Switch> row in switches)
                    row.Value.SetValueWithoutNotify(GameSettings.Shows(row.Key));
            }, "Sets everything below");

            VisualElement control = Row(page, "Particle density", "Fewer sparks, flames and raindrops");
            density = new Slider(GameSettings.MinParticleDensity, 1) { name = "Slider", fill = true };
            density.AddToClassList("volume");
            density.SetValueWithoutNotify(GameSettings.ParticleDensity);
            density_percent = new Label(Percent(GameSettings.ParticleDensity));
            density_percent.AddToClassList("setting__percent");
            density.RegisterValueChangedCallback(change =>
            {
                float snapped = Mathf.Round(change.newValue * 20) / 20; // by fives of a hundred
                if (!Mathf.Approximately(snapped, change.newValue))
                    density.SetValueWithoutNotify(snapped);
                GameSettings.ParticleDensity = snapped;
                density_percent.text = Percent(snapped);
                show_preset();
            });
            control.Add(density);
            control.Add(density_percent);

            Action<GameSettings.Effect, string, string> effect = (which, label, caption) =>
                switches[which] = SwitchRow(page, label, caption, GameSettings.Shows(which), on =>
                {
                    GameSettings.SetShows(which, on);
                    show_preset();
                });
            Group(page, "Weather");
            effect(GameSettings.Effect.AnimatedWeather, "Animated weather", "Off: still pictures");
            effect(GameSettings.Effect.StormLightning, "Lightning and thunder", "In Skellige Storm");
            effect(GameSettings.Effect.ClearWeatherSun, "Clear Weather's sun", "The light sweeping the weather away");
            Group(page, "Battle effects");
            effect(GameSettings.Effect.HeroLanding, "Hero landing", "Off: the card's edge flashes gold");
            effect(GameSettings.Effect.ScorchFire, "Scorch fire", "Off: the cards char, without fire");
            effect(GameSettings.Effect.AbilityFlares, "Ability flares", "Off: the ability's icon only");
            effect(GameSettings.Effect.MedicFlames, "Medic flames", "Over the discard pile");
            effect(GameSettings.Effect.HornGlow, "Horn glow", "Along a horned row");
            effect(GameSettings.Effect.LandingWisps, "Landing wisps", "As a card lands");
            Group(page, "Motion");
            effect(GameSettings.Effect.ShineAndPulsing, "Shine and pulsing", "The light across a card, pulsing frames");
            Group(page, "Menus");
            effect(GameSettings.Effect.MenuAnimations, "Menu animations", "The title, the rules coming in");
            Fades((ScrollView)page);
            return preset.Left;
        }

        // A scrolling page fades out at its top and bottom where there is more of it that way (the panel's colour over it)
        private static void Fades(ScrollView scroll)
        {
            Color panel = new Color(0.125f, 0.102f, 0.082f, 0.96f);
            VisualElement[] fades = new VisualElement[2];
            for (int i = 0; i < 2; i++)
            {
                VisualElement fade = new VisualElement { name = i == 0 ? "Fade top" : "Fade bottom", pickingMode = PickingMode.Ignore };
                fade.AddToClassList("settings__fade");
                fade.AddToClassList(i == 0 ? "settings__fade--top" : "settings__fade--bottom");
                fade.style.backgroundImage = new StyleBackground(UITheme.Current.gradientV);
                fade.style.unityBackgroundImageTintColor = panel;
                scroll.hierarchy.Add(fade); // over the rows, not one of them
                fades[i] = fade;
            }
            Action show = () =>
            {
                float at = scroll.scrollOffset.y, end = scroll.verticalScroller.highValue;
                fades[0].style.opacity = Mathf.Clamp01(at / 40);
                fades[1].style.opacity = Mathf.Clamp01((end - at) / 40);
            };
            scroll.verticalScroller.valueChanged += _ => show();
            scroll.RegisterCallback<GeometryChangedEvent>(_ => show());
        }

        private static string FrameRateLabel(int frame_rate)
        {
            return frame_rate == GameSettings.VSync ? "VSync" : frame_rate == 0 ? S("Unlimited") : frame_rate + " FPS";
        }

        private static string Percent(float value)
        {
            return Mathf.RoundToInt(value * 100) + "%";
        }

        // A heading over a group of rows
        private static void Group(VisualElement page, string title)
        {
            Label heading = new Label(S(title)) { name = "Group " + title };
            heading.AddToClassList("setting__group");
            page.Add(heading);
        }

        private static Focusable BuildGamePage(VisualElement page)
        {
            // The texts of every screen come from the language's tables, so a new language shows once the panel is rebuilt
            // (Reopen), after its tables are loaded
            string[] languages = Loc.Languages.Select(Loc.NameOf).ToArray();
            Stepper language = StepperRow(page, "Language", languages, Loc.Current,
                i => Loc.Set(i, () =>
                {
                    if (IsOpen)
                        Reopen();
                }), "Takes effect at once");
            Stepper speed = StepperRow(page, "Game speed", Options("Speed", GameSettings.GameSpeedNames), GameSettings.GameSpeed,
                i => GameSettings.GameSpeed = i, "Cards, notices and the opponent");

            Label about = null;
            StepperRow(page, "Difficulty", Options("Difficulty", GameSettings.AiDifficultyNames), (int)GameSettings.AiDifficulty, i =>
            {
                GameSettings.AiDifficulty = (AiDifficulty)i;
                if (about != null)
                    about.text = S(DifficultyCaptions[i]);
            }, DifficultyCaptions[(int)GameSettings.AiDifficulty]);
            about = page.Q("Row Difficulty").Q<Label>("Caption");
            SwitchRow(page, "Censored artwork", "Covered versions of some cards' art", GameSettings.CensoredArt, on => GameSettings.CensoredArt = on);
            SwitchRow(page, "Check for updates", "On launch, from the game's page on GitHub", GameSettings.CheckForUpdates,
                on => GameSettings.CheckForUpdates = on);
            return language.Left;
        }

        private static void ResetToDefaults()
        {
            GameSettings.ResetToDefaults();
            Reopen();
        }

        // Rebuilt with the values now (the defaults, another language), on the same tab
        private static void Reopen()
        {
            Action closed = onClosed;
            int tab = currentTab;
            onClosed = null;
            Close();
            Open(closed, tab);
        }
    }
}
