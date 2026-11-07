using System;
using System.Collections.Generic;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // "How to play" (main menu): the rules of The Witcher 3's Gwent, one topic at a time. Topics on the left, the topic's
    // rules on the right (they scroll when long), each beside the game's own pieces as on the printed Gwent rule cards
    // (matt77hias/Gwent): faction shields, the hand count, row emblems, the abilities' medallions, life gems, The Witcher 3's
    // notices, cards, keys. The factions show their ability and leaders (hover a leader, or hold a finger on it, to read its
    // ability). A UI Toolkit dialog: Assets/UI/Toolkit/RulesPage.uxml, the topics and their rules made here, their words from the
    // Rules table.
    public static class RulesPage
    {
        // A rule: its pieces (see Piece), and the Rules table keys of its term and its explanation. Id is the English term,
        // kept apart from what is shown because it names the row (the autoplay test finds "Entry Spy" in any language).
        private struct Entry
        {
            public string[] Pieces;
            public string Id;
            public string TitleKey;
            public string TextKey;
        }

        // Id is the English name for the same reason; Key is the topic's part of its Rules keys (rules.<Key>.name)
        private struct Topic
        {
            public string Id;
            public string Key;
            public string IntroKey;
            public Entry[] Entries;
            public bool Factions;
        }

        private struct FactionTopic
        {
            public Faction Faction;
            public int[] Leaders;
        }

        // key: the entry's part of its Rules keys, rules.<key>.title and rules.<key>.text
        private static Entry E(string id, string key, params string[] pieces)
        {
            return new Entry { Id = id, TitleKey = "rules." + key + ".title", TextKey = "rules." + key + ".text", Pieces = pieces };
        }

        // An entry whose explanation is worded differently with touch and with the pointer: rules.<key>.text.<variant>
        private static Entry EV(string id, string key, string variant, params string[] pieces)
        {
            return new Entry { Id = id, TitleKey = "rules." + key + ".title", TextKey = "rules." + key + ".text." + variant, Pieces = pieces };
        }

        // Built when the page opens, so the controls follow the current input (touch or not). The words are the Rules table's
        // (Assets/Localization/Source/Rules.csv): rules.<topic>.name and .intro, rules.<topic>.<entry>.title and .text.
        // Controls and the touch / pointer wordings have a key of their own, because a gesture is not a click.
        private static Topic[] Topics()
        {
            bool touch = GameInput.IsTouch;
            string input = touch ? "touch" : "pointer";
            return new[]
            {
                new Topic
                {
                    Id = "The battle",
                    Key = "battle",
                    IntroKey = "rules.battle.intro",
                    Entries = new[]
                    {
                        E("Factions", "battle.factions", "shield:NR", "shield:SC", "shield:M", "shield:NF", "shield:SK"),
                        E("Hand cards", "battle.hand", "cards", "num:10"),
                        E("Your turn", "battle.turn", "notice:me_turn"),
                        E("Combat rows", "battle.rows", "emblem:0", "emblem:1", "emblem:2"),
                        E("Special cards", "battle.special", "medal:biting_frost", "medal:decoy", "medal:scorch"),
                        E("Passing", "battle.passing", "notice:round_passed"),
                        E("Life", "battle.life", "gem", "gem"),
                        E("Playing well", "battle.well", "notice:win_round"),
                    },
                },
                new Topic
                {
                    Id = "The board",
                    Key = "board",
                    IntroKey = "rules.board.intro",
                    Entries = new[]
                    {
                        E("Rows", "board.rows", "emblem:0", "emblem:1", "emblem:2"),
                        E("Strength", "board.strength", "score:24", "total:61"),
                        E("Agile", "board.agile", "medal:agile"),
                        E("Horn slot", "board.horn", "medal:commander_horn", "medal:mardroeme"),
                        E("Weather board", "board.weather", "medal:biting_frost", "medal:impenetrable_fog", "medal:torrential_rain"),
                        E("Whose turn", "board.turn", "notice:op_turn"),
                        EV("Discard piles", "board.discard", input, "back:NR", "back:M"),
                    },
                },
                new Topic
                {
                    Id = "Cards",
                    Key = "cards",
                    IntroKey = "rules.cards.intro",
                    Entries = new[]
                    {
                        E("Heroes", "cards.heroes", "card:67", "card:14"),
                        E("Units", "cards.units", "card:7", "card:4"),
                        E("Special cards", "cards.special", "card:202", "card:203"),
                        E("Leader", "cards.leader", "card:51", "crown"),
                    },
                },
                new Topic
                {
                    Id = "Abilities",
                    Key = "abilities",
                    IntroKey = "rules.abilities.intro",
                    Entries = new[]
                    {
                        E("Spy", "abilities.spy", "medal:spy"),
                        E("Medic", "abilities.medic", "medal:medic"),
                        E("Muster", "abilities.muster", "medal:muster"),
                        E("Tight bond", "abilities.tight_bond", "medal:tight_bond"),
                        E("Morale boost", "abilities.morale_boost", "medal:morale_boost"),
                        E("Commander's horn", "abilities.horn", "medal:commander_horn"),
                        E("Scorch", "abilities.scorch", "medal:scorch"),
                        E("Agile", "abilities.agile", "medal:agile"),
                        E("Berserker", "abilities.berserker", "medal:berserker", "medal:mardroeme"),
                        E("Avenger", "abilities.avenger", "card:16", "card:164", "medal:avenger"),
                    },
                },
                new Topic
                {
                    Id = "Special cards",
                    Key = "special",
                    IntroKey = "rules.special.intro",
                    Entries = new[]
                    {
                        E("Biting Frost", "special.biting_frost", "medal:biting_frost"),
                        E("Impenetrable Fog", "special.impenetrable_fog", "medal:impenetrable_fog"),
                        E("Torrential Rain", "special.torrential_rain", "medal:torrential_rain"),
                        E("Skellige Storm", "special.skellige_storm", "medal:skellige_storm"),
                        E("Clear Weather", "special.clear_weather", "medal:clear_weather"),
                        E("Commander's Horn", "special.horn", "medal:commander_horn"),
                        E("Decoy", "special.decoy", "medal:decoy"),
                        E("Scorch", "special.scorch", "medal:scorch"),
                        E("Mardroeme", "special.mardroeme", "medal:mardroeme"),
                    },
                },
                new Topic
                {
                    Id = "Factions",
                    Key = "factions",
                    IntroKey = "rules.factions.intro." + input,
                    Factions = true,
                },
                new Topic
                {
                    Id = "Controls",
                    Key = "controls",
                    IntroKey = "rules.controls.intro." + input,
                    Entries = touch ? new[]
                    {
                        E("Play a card", "controls.touch.play", "card:7"),
                        E("Leader", "controls.touch.leader", "crown"),
                        E("Choose cards", "controls.touch.choose", "card:7", "card:14"),
                        E("Pass", "controls.touch.pass", "key:Hold to pass"),
                        E("Look", "controls.touch.look", "card:14"),
                        E("Zoom", "controls.touch.zoom", "key:Pinch"),
                        E("A side at a glance", "controls.touch.glance", "crown"),
                        E("Chat", "controls.touch.chat", "key:..."),
                        E("Back", "controls.touch.back", "key:Back"),
                        E("Pause", "controls.touch.pause", "key:?"),
                        E("Give up", "controls.touch.give_up", "back:NR"),
                    } : new[]
                    {
                        E("Play a card", "controls.pointer.play", "key:<", "key:>", "key:Enter"),
                        E("Leader", "controls.pointer.leader", "key:X", "crown"),
                        E("Choose cards", "controls.pointer.choose", "key:Space", "key:Enter"),
                        E("Pass", "controls.pointer.pass", "key:Space"),
                        E("Look", "controls.pointer.look", "card:14"),
                        E("A side at a glance", "controls.pointer.glance", "crown"),
                        E("Chat", "controls.pointer.chat", "key:E", "key:T"),
                        E("Back", "controls.pointer.back", "key:Esc"),
                        E("Pause", "controls.pointer.pause", "key:?"),
                        E("Give up", "controls.pointer.give_up", "back:NR"),
                    },
                },
            };
        }

        private static readonly FactionTopic[] FactionList =
        {
            new FactionTopic { Faction = Faction.NorthernRealms, Leaders = new[] { 51, 52, 53, 54, 55 } },
            new FactionTopic { Faction = Faction.Nilfgaard, Leaders = new[] { 33, 34, 35, 36, 37 } },
            new FactionTopic { Faction = Faction.Scoiatael, Leaders = new[] { 57, 58, 59, 60, 61 } },
            new FactionTopic { Faction = Faction.Monsters, Leaders = new[] { 40, 41, 42, 43, 44 } },
            new FactionTopic { Faction = Faction.Skellige, Leaders = new[] { 157, 165 } },
        };

        // The pieces are drawn at this scale, so they sit in the page's icon column as its icons did
        private const float PieceScale = 0.7f;
        private const float ClusterHeight = 104;

        public static void Open(Action on_close)
        {
            UIScreens.ShowDialog("RulesPage", UITheme.Current.rulesPage, Bind, on_close);
        }

        private static void Bind(VisualElement root)
        {
            Topic[] topics = Topics();
            ScrollView view = root.Q<ScrollView>("Topic");
            view.mouseWheelScrollSize = 40;
            view.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            VisualElement list = root.Q("Topics");
            List<MenuEntry> tabs = new List<MenuEntry>();
            for (int i = 0; i < topics.Length; i++)
            {
                int index = i;
                // Named by the English id (the autoplay test finds it by name); the text is the current language's
                MenuEntry tab = new MenuEntry { name = "Item " + topics[i].Id, text = Loc.T(Loc.Rules, "rules." + topics[i].Key + ".name") };
                tab.AddToClassList("rules__topic");
                tab.clicked += () => Show(view, topics[index], tabs, index);
                list.Add(tab);
                tabs.Add(tab);
            }
            root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);

            Show(view, topics[0], tabs, 0);
            root.schedule.Execute(() => UIScreens.Focus(tabs[0]));
        }

        // The pieces' columns of a topic, to be made as wide as its widest group
        private class Columns
        {
            public readonly List<VisualElement> All = new List<VisualElement>();
            public float Widest;
        }

        private static void Show(ScrollView view, Topic topic, List<MenuEntry> tabs, int index)
        {
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].EnableInClassList("rules__topic--current", i == index);
            view.Clear();
            Label intro = new Label(Loc.T(Loc.Rules, topic.IntroKey));
            intro.AddToClassList("rules__intro");
            view.Add(intro);

            Columns columns = new Columns();
            int order = 0;
            if (topic.Factions)
                foreach (FactionTopic faction in FactionList)
                    FadeIn(AddFaction(view, faction, columns), order++);
            else
                foreach (Entry entry in topic.Entries)
                {
                    VisualElement text;
                    FadeIn(AddEntry(view, entry.Id, entry.Pieces, Loc.T(Loc.Rules, entry.TitleKey), Loc.T(Loc.Rules, entry.TextKey), columns, out text), order++);
                }
            // One column for the topic's pieces, as wide as its widest group: the texts line up, close to their pieces
            foreach (VisualElement column in columns.All)
                column.style.width = Mathf.Max(64, columns.Widest);
            view.scrollOffset = Vector2.zero;
        }

        // The rule's pieces beside its gold title and its text; id: the row's name (not what is shown, so it is the same in
        // every language); text: the text's column (for more below it)
        private static VisualElement AddEntry(VisualElement parent, string id, string[] pieces, string title_text, string text_text, Columns columns, out VisualElement text)
        {
            VisualElement row = new VisualElement { name = "Entry " + id };
            row.AddToClassList("rules__row");
            Cluster(row, pieces, columns);

            text = new VisualElement();
            text.AddToClassList("rules__text");
            Label title = new Label(title_text);
            title.AddToClassList("rules__title");
            Label body = new Label(text_text);
            body.AddToClassList("rules__line");
            text.Add(title);
            text.Add(body);
            row.Add(text);
            parent.Add(row);
            return row;
        }

        // The pieces side by side, overlapping a little where they do on the printed rule cards, the group centred in its
        // column and drawn at PieceScale (sizes below are the pieces' own)
        private static void Cluster(VisualElement row, string[] pieces, Columns columns)
        {
            VisualElement column = new VisualElement { name = "Pieces" };
            column.AddToClassList("rules__pieces");
            VisualElement group = new VisualElement { name = "Group", pickingMode = PickingMode.Ignore };

            List<VisualElement> made = new List<VisualElement>();
            float total = 0;
            foreach (string spec in pieces)
            {
                float overlap, rise;
                Vector2 size;
                VisualElement piece = Piece(spec, made.Count, out size, out overlap, out rise);
                if (piece == null)
                    continue;
                if (made.Count > 0)
                    total -= overlap;
                piece.style.left = total;
                piece.style.top = (ClusterHeight - size.y) / 2 - rise;
                piece.style.width = size.x;
                piece.style.height = size.y;
                made.Add(piece);
                total += size.x;
            }
            // Drawn left to right, the first on top (as the printed cards' fanned shields)
            for (int i = made.Count - 1; i >= 0; i--)
                group.Add(made[i]);

            group.style.position = Position.Absolute;
            group.style.width = total;
            group.style.height = ClusterHeight;
            group.style.left = Length.Percent(50);
            group.style.marginLeft = -total / 2;
            group.style.top = (ClusterHeight * PieceScale - ClusterHeight) / 2;
            group.style.scale = new Scale(new Vector2(PieceScale, PieceScale));
            column.Add(group);
            row.Add(column);
            columns.All.Add(column);
            columns.Widest = Mathf.Max(columns.Widest, total * PieceScale);
        }

        // One piece, from its description, at its own size; index: its place in the group; overlap: how far it slides
        // under the piece before it; rise: how much higher than the middle it sits
        private static VisualElement Piece(string spec, int index, out Vector2 size, out float overlap, out float rise)
        {
            UITheme theme = UITheme.Current;
            string kind = spec, arg = "";
            int colon = spec.IndexOf(':');
            if (colon > 0)
            {
                kind = spec.Substring(0, colon);
                arg = spec.Substring(colon + 1);
            }
            overlap = 0;
            rise = 0;
            size = Vector2.zero;
            switch (kind)
            {
                case "shield":
                    // Fanned, every other one a little higher
                    overlap = 24;
                    rise = index % 2 == 0 ? 5 : -5;
                    return Picture(SpriteCache.Load("Stats/faction/" + arg), size = new Vector2(76, 85));
                case "cards":
                    return Picture(SpriteCache.Load("Stats/cards"), size = new Vector2(46, 63));
                case "num":
                    {
                        overlap = -10;
                        VisualElement number = Box("Number", size = new Vector2(96, 80));
                        number.Add(Number(arg, 66, theme.goldBright));
                        return number;
                    }
                case "emblem":
                    overlap = 4;
                    return Picture(theme.rowEmblems[int.Parse(arg)], size = new Vector2(78, 78));
                case "medal":
                    overlap = 22;
                    return Picture(SpriteCache.Load("UI/Rules/" + arg), size = new Vector2(98, 98));
                case "gem":
                    overlap = -8;
                    return Picture(theme.gem, size = new Vector2(58, 59));
                case "notice":
                    return Picture(SpriteCache.Load("W3/Notifications/" + arg), size = new Vector2(126, 90));
                case "card":
                    overlap = -8;
                    return CardPicture(int.TryParse(arg, out int card_id) ? CensoredArt.BoardPicture(card_id) : null, size = new Vector2(64, 99));
                case "back":
                    overlap = 38;
                    return CardPicture(SpriteCache.Load("Cards/Back/" + arg), size = new Vector2(64, 99));
                case "crown":
                    return Picture(theme.leaderReady, size = new Vector2(76, 76));
                case "score":
                    {
                        overlap = -10;
                        VisualElement disc = Picture(theme.scoreDisc, size = new Vector2(64, 65));
                        if (disc != null)
                            disc.Add(Number(arg, 32, new Color(0.24f, 0.15f, 0.07f)));
                        return disc;
                    }
                case "total":
                    {
                        VisualElement laurel = Picture(SpriteCache.Load("Various/pass"), size = new Vector2(112, 97));
                        VisualElement disc = Picture(theme.scoreTotal, new Vector2(86, 86));
                        if (laurel == null || disc == null)
                            return laurel;
                        disc.style.left = (112 - 86) / 2f;
                        disc.style.top = (97 - 86) / 2f - 2;
                        disc.style.width = 86;
                        disc.style.height = 86;
                        disc.Add(Number(arg, 34, new Color(0.11f, 0.08f, 0.05f)));
                        laurel.Add(disc);
                        return laurel;
                    }
                case "key":
                    overlap = -10;
                    return Key(arg, out size);
            }
            return null;
        }

        private static VisualElement Box(string name, Vector2 size)
        {
            VisualElement box = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            box.AddToClassList("piece");
            return box;
        }

        // A picture of the game's, kept in shape (none: no piece)
        private static VisualElement Picture(Sprite sprite, Vector2 size)
        {
            if (sprite == null)
                return null;
            VisualElement picture = Box(sprite.name, size);
            picture.style.backgroundImage = new StyleBackground(sprite);
            return picture;
        }

        // A card, with the soft shadow the board gives its cards
        private static VisualElement CardPicture(Sprite sprite, Vector2 size)
        {
            if (sprite == null)
                return null;
            VisualElement card = Box(sprite.name, size);
            VisualElement shadow = Box("Shadow", size);
            shadow.style.left = -9;
            shadow.style.right = -9;
            shadow.style.top = -6;
            shadow.style.bottom = -12;
            shadow.style.backgroundImage = new StyleBackground(UITheme.Current.softShadow);
            shadow.style.unityBackgroundImageTintColor = new Color(0, 0, 0, 0.6f);
            shadow.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)); // stretched, not kept in shape
            VisualElement art = Box("Art", size);
            art.style.left = 0;
            art.style.right = 0;
            art.style.top = 0;
            art.style.bottom = 0;
            art.style.backgroundImage = new StyleBackground(sprite);
            card.Add(shadow);
            card.Add(art);
            return card;
        }

        private static Label Number(string text, float size, Color color)
        {
            Label number = new Label(text) { pickingMode = PickingMode.Ignore };
            number.AddToClassList("piece__number");
            number.style.fontSize = size;
            number.style.color = color;
            return number;
        }

        // What a key's cap says: the keys of the keyboard keep their names (Space, Enter and Esc read the same on the caps the
        // battle draws), the words are the current language's
        private static string KeyLabel(string id)
        {
            switch (id)
            {
                case "Hold to pass": return Loc.T(Loc.UI, "battle.hold_to_pass");
                case "Pinch": return Loc.T(Loc.Rules, "rules.key.pinch");
                case "Back": return Loc.T(Loc.Rules, "rules.key.back");
                default: return id;
            }
        }

        // A key of the keyboard: a dark key with a gold outline; < and > show as chevrons. id names the key (and tells the
        // chevrons), what it says is KeyLabel's
        private static VisualElement Key(string id, out Vector2 size)
        {
            UITheme theme = UITheme.Current;
            string label = KeyLabel(id);
            Sprite chevron = id == "<" ? theme.chevronLeft : id == ">" ? theme.chevronRight : null;
            size = new Vector2(chevron != null ? 62 : Mathf.Max(62, 34 + label.Length * 19), 60);
            VisualElement key = Box("Key " + id, size);
            key.AddToClassList("piece--key");
            if (chevron != null)
            {
                VisualElement glyph = new VisualElement { pickingMode = PickingMode.Ignore };
                glyph.style.width = 34;
                glyph.style.height = 34;
                glyph.style.backgroundImage = new StyleBackground(chevron);
                glyph.style.unityBackgroundImageTintColor = theme.goldBright;
                key.Add(glyph);
            }
            else
            {
                Label text = new Label(label) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("piece__key-label");
                text.style.fontSize = 30;
                key.Add(text);
            }
            return key;
        }

        //----------------------------------------Factions----------------------------------------//
        // A faction like any rule (its shield, its name, its ability), its leaders below the ability
        private static VisualElement AddFaction(VisualElement parent, FactionTopic faction, Columns columns)
        {
            VisualElement text;
            // The row is named by the English name; the name and the ability shown are the current language's
            VisualElement row = AddEntry(parent, faction.Faction.DisplayName(), new[] { "shield:" + faction.Faction.Code() }, faction.Faction.LocalName(), faction.Faction.LocalAbility(), columns, out text);
            VisualElement leaders = new VisualElement { name = "Leaders" };
            leaders.AddToClassList("rules__leaders");
            foreach (int leader in faction.Leaders)
                leaders.Add(LeaderPortrait(leader));
            text.Add(leaders);
            return row;
        }

        // A round portrait of the leader in a gold ring; its ability shows over it on hover (a held finger with touch)
        private static VisualElement LeaderPortrait(int leader_id)
        {
            VisualElement holder = new VisualElement { name = "Leader " + leader_id };
            holder.AddToClassList("rules__leader");
            VisualElement art = new VisualElement { name = "Art", pickingMode = PickingMode.Ignore };
            art.AddToClassList("rules__leader-art");
            CardArt.ShowPortrait(art, leader_id);
            holder.Add(art);
            // The name is the leader card's own, as its table entry says it (the deck builder and the battle show the same)
            string name = CardLibrary.Get().Get(leader_id).LocalName();
            string ability = CardDescription.LeaderAbility(leader_id);
            if (ability != null)
                ElementTooltip.Attach(holder, () => "<b>" + name + "</b>\n" + ability);
            return holder;
        }

        //----------------------------------------A little life----------------------------------------//
        // The rules come in one after another when a topic opens (with Settings > Graphics > Menu animations off, all at once)
        private static void FadeIn(VisualElement row, int order)
        {
            if (!GameSettings.Shows(GameSettings.Effect.MenuAnimations))
            {
                row.AddToClassList("rules__row--shown");
                return;
            }
            row.schedule.Execute(() => row.AddToClassList("rules__row--shown")).StartingIn((long)((0.04f + order * 0.05f) * 1000));
        }
    }
}
