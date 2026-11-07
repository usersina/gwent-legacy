using System;
using System.Collections.Generic;
using System.Linq;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.Story
{
    // A place of the story's map (Assets/UI/Toolkit/StorySheet.uxml), as a card game shows an opponent or a shop: few
    // words, the cards themselves. Their portrait (their leader's, named in its tooltip), name and trade; chips for their
    // faction, how well they play (one to three pips) and the quest that leads there; the card a win gives (their own, or
    // a random one: a card's back); what they sell, the cards in a grid with a price tag under each (a click on the card
    // or its tag buys it, asked first; ×2 on a card you own two of); a tournament's rounds. The button carries the crowns
    // a win pays (Play · 30). The line under it only appears to say what stops it (too few crowns, a deck that can't play
    // there).
    public static class StorySheet
    {
        public static void Open(StoryProgress story, StoryPlace place, Action<StoryReward> on_change, Action on_close)
        {
            UIScreens.ShowDialog("StorySheet", UITheme.Current.storySheet, root => Bind(root, story, place, on_change), on_close);
        }

        // Their trade, unless their name says it already ("The blacksmith of Blackbough": no "blacksmith" under it)
        public static string Trade(StoryPlace place)
        {
            return place.Name.IndexOf(place.Trade, StringComparison.OrdinalIgnoreCase) >= 0 ? "" : place.Trade;
        }

        // A line on where the place stands (the marker's tooltip): what is left to win, or that it is all won
        public static string State(StoryProgress story, StoryPlace place)
        {
            CardDatabase db = CardLibrary.Get();
            switch (place.Type)
            {
                case StoryPlaceKind.Shop:
                    int left = place.Sells.Where((sale, i) => !story.Bought(place, i)).Count();
                    return left > 0 ? Loc.T(Loc.UI, "story.cards_for_sale", left) : Loc.T(Loc.UI, "story.sold_out");
                case StoryPlaceKind.Found:
                case StoryPlaceKind.Event:
                    return Loc.T(Loc.UI, story.IsDone(place) ? "story.taken" : "story.something_here");
                case StoryPlaceKind.Tournament:
                    return story.IsDone(place) ? Loc.T(Loc.UI, "story.won") : story.Entered(place) ? Loc.T(Loc.UI, "story.next_round_is", story.NextRound(place).Name)
                        : Loc.T(Loc.UI, "story.n_rounds", place.Rounds.Count);
                default:
                    string sells = place.Sells.Where((sale, i) => !story.Bought(place, i)).Any() ? " · " + Loc.T(Loc.UI, "story.sells_cards") : "";
                    if (story.Wins(place.Id) > 0)
                        return Loc.T(Loc.UI, "story.beaten") + sells;
                    List<int> prize = story.PrizeOf(place);
                    return (prize.Count == 0 ? Loc.T(Loc.UI, "story.crowns") : place.Random ? Loc.T(Loc.UI, "story.random_card") : db.Get(prize[0]).LocalName()) + sells;
            }
        }

        private static void Bind(VisualElement root, StoryProgress story, StoryPlace place, Action<StoryReward> on_change)
        {
            CardDatabase db = CardLibrary.Get();
            StoryRound round = place.Type == StoryPlaceKind.Tournament ? story.NextRound(place) : null;
            Deck theirs = place.Plays ? DeckGenerator.For(place, round, db) : null;

            root.Q<Label>("Title").text = place.Name;
            root.Q<Label>("Trade").text = Trade(place);
            root.Q<Label>("Trade").style.display = Trade(place).Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            VisualElement art = root.Q("Art"), icon = root.Q("Icon");
            if (theirs != null)
            {
                CardArt.ShowPortrait(art, theirs.Leader);
                icon.style.display = DisplayStyle.None;
                ElementTooltip.Attach(root.Q("Portrait"), () => Loc.T(Loc.UI, "story.leader_tip", db.Get(theirs.Leader).LocalName()));
            }
            else
            {
                art.style.display = DisplayStyle.None;
                icon.style.backgroundImage = new StyleBackground(StoryScreen.Icon(place));
            }
            ShowChips(root.Q("Chips"), story, place, theirs);
            ShowPrize(root, story, place, round, db);

            // The way on, and what stops it
            Button play = root.Q<Button>("Button Play");
            Label note = root.Q<Label>("Note");
            string why = null;
            string text = null;
            int pays = 0;
            Action act = null;
            string deck_problem = null;
            Deck mine = place.Plays ? BattleController.PlayerDeck(db, out deck_problem) : null;
            switch (place.Type)
            {
                case StoryPlaceKind.Shop:
                    break;
                case StoryPlaceKind.Found:
                case StoryPlaceKind.Event:
                    text = Loc.T(Loc.UI, story.IsDone(place) ? "story.taken" : place.Type == StoryPlaceKind.Found ? "story.act_search" : place.Gives.Count > 0 ? "story.act_take_deck" : "story.act_accept");
                    why = story.IsDone(place) ? "" : null;
                    act = () =>
                    {
                        StoryReward reward = story.Take(place);
                        UIOverlay.Toast(Got(reward, db), 3.5f);
                        UIScreens.Close(root);
                        on_change(reward);
                    };
                    break;
                case StoryPlaceKind.Tournament:
                    text = Loc.T(Loc.UI, story.Entered(place) ? "story.act_next_round" : "story.act_enter");
                    pays = story.Entered(place) ? story.CrownsFor(place, round) : -place.Fee;
                    why = story.CannotEnter(place) ?? story.CannotPlayWith(place, mine);
                    act = () => StoryBattle.Start(place);
                    break;
                default:
                    text = Loc.T(Loc.UI, story.Wins(place.Id) > 0 ? "story.act_play_again" : "story.act_play");
                    pays = story.CrownsFor(place);
                    why = story.CannotPlayWith(place, mine);
                    act = () => StoryBattle.Start(place);
                    break;
            }
            if (text == null)
                play.style.display = DisplayStyle.None;
            else
            {
                PlayText(play, text, pays);
                play.SetEnabled(why == null);
                play.clicked += () =>
                {
                    if (why == null)
                        act();
                };
            }
            note.text = !string.IsNullOrEmpty(why) ? why : deck_problem != null ? Loc.T(Loc.UI, "story.deck_cant_play", deck_problem) : "";
            note.style.display = note.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            ShowList(root, story, place, on_change, db);
            root.Q("Panel").EnableInClassList("story-sheet--narrow", root.Q("ListBox").style.display == DisplayStyle.None);
            root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);
            root.schedule.Execute(() => UIScreens.Focus(text != null && why == null ? (VisualElement)play : root.Q<Button>("Button Close")));
        }

        // The button's words, and the crowns it pays (a coin and the number) or costs (a tournament's fee)
        private static void PlayText(Button button, string text, int crowns)
        {
            button.text = "";
            button.Clear();
            Label words = new Label(text) { pickingMode = PickingMode.Ignore };
            words.AddToClassList("story-sheet__play-text");
            button.Add(words);
            if (crowns == 0)
                return;
            Label dot = new Label("·") { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("story-sheet__play-text");
            button.Add(dot);
            button.Add(Coin("story-sheet__play-coin"));
            Label number = new Label(Mathf.Abs(crowns).ToString()) { pickingMode = PickingMode.Ignore };
            number.AddToClassList("story-sheet__play-text");
            button.Add(number);
            ElementTooltip.Attach(button, () => crowns > 0 ? Loc.T(Loc.UI, "story.win_pays", crowns) : Loc.T(Loc.UI, "story.entry_fee", -crowns));
        }

        private static VisualElement Coin(string style)
        {
            VisualElement coin = new VisualElement { pickingMode = PickingMode.Ignore };
            coin.AddToClassList("story-coin");
            coin.AddToClassList(style);
            coin.style.backgroundImage = new StyleBackground(UITheme.Current.iconCoin);
            return coin;
        }

        // Faction, pips for how well they play, the quest that leads there, a tournament's rule
        private static void ShowChips(VisualElement chips, StoryProgress story, StoryPlace place, Deck theirs)
        {
            if (theirs != null)
            {
                Chip(chips, theirs.Faction.LocalName(), null, null);
                int level = place.Difficulty == AiDifficulty.Easy ? 1 : place.Difficulty == AiDifficulty.Normal ? 2 : 3;
                string pips = new string('●', level) + new string('○', 3 - level);
                string how = Loc.T(Loc.UI, "story.skill_" + level);
                Chip(chips, pips, null, how).AddToClassList("story-chip--pips");
            }
            StoryQuest quest = story.Content.QuestsOf(place).FirstOrDefault(q => !story.QuestDone(q));
            if (quest != null && !story.IsDone(place))
                Chip(chips, quest.Name, UITheme.Current.iconQuest, Loc.T(Loc.UI, "story.step_of", quest.Name)).AddToClassList("story-chip--quest");
            if (place.OnlyDecks != Faction.Neutral)
                Chip(chips, Loc.T(Loc.UI, "story.decks_only", place.OnlyDecks.LocalName()), null, null);
        }

        private static VisualElement Chip(VisualElement chips, string text, Sprite icon, string tooltip)
        {
            VisualElement chip = new VisualElement();
            chip.AddToClassList("story-chip");
            if (icon != null)
            {
                VisualElement mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("story-chip__icon");
                mark.style.backgroundImage = new StyleBackground(icon);
                chip.Add(mark);
            }
            Label label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("story-chip__text");
            chip.Add(label);
            if (tooltip != null)
                ElementTooltip.Attach(chip, () => tooltip);
            chips.Add(chip);
            return chip;
        }

        // The card a win gives (their own, a random one: their faction's card back, the next round's), or what was won
        private static void ShowPrize(VisualElement root, StoryProgress story, StoryPlace place, StoryRound round, CardDatabase db)
        {
            VisualElement box = root.Q("PrizeBox"), card = root.Q("Prize");
            Label title = root.Q<Label>("PrizeTitle");
            List<int> prize;
            bool done;
            switch (place.Type)
            {
                case StoryPlaceKind.Tournament:
                    prize = round != null ? round.Prize : new List<int>();
                    done = round != null && story.Wins(StoryProgress.RoundKey(place, round)) > 0;
                    break;
                case StoryPlaceKind.Found:
                case StoryPlaceKind.Event:
                    prize = place.Gives.Count > 0 ? place.Gives.Where(id => db.Get(id).IsLeader).Take(1).ToList() : place.Prize;
                    done = story.IsDone(place);
                    break;
                case StoryPlaceKind.Shop:
                    box.style.display = DisplayStyle.None;
                    return;
                default:
                    prize = story.Wins(place.Id) > 0 ? place.Prize : story.PrizeOf(place);
                    done = story.Wins(place.Id) > 0;
                    break;
            }
            if (place.Random && !done && prize.Count > 0)
            {
                title.text = Loc.T(Loc.UI, "story.prize_random");
                card.style.backgroundImage = new StyleBackground(SpriteCache.Load("Cards/Back/" + place.DeckFaction.Code()));
                ElementTooltip.Attach(card, () => Loc.T(Loc.UI, "story.prize_random_tip"));
            }
            else if (prize.Count > 0)
            {
                title.text = Loc.T(Loc.UI, done ? "story.prize_won" : place.Gives.Count > 0 ? "story.prize_skellige" : "story.prize");
                CardArt.Show(card, prize[0]);
                card.EnableInClassList("story-sheet__card--won", done);
                if (place.Gives.Count > 0)
                    ElementTooltip.Attach(card, () => Loc.T(Loc.UI, "story.gives_deck", place.Gives.Count));
            }
            else
                box.style.display = DisplayStyle.None; // nothing left but crowns: the button says them
        }

        // What they sell, a grid of cards with a price tag under each; or a tournament's rounds
        private static void ShowList(VisualElement root, StoryProgress story, StoryPlace place, Action<StoryReward> on_change, CardDatabase db)
        {
            VisualElement box = root.Q("ListBox");
            ScrollView list = root.Q<ScrollView>("List");
            Label title = root.Q<Label>("ListTitle");
            list.Clear();
            if (place.Type == StoryPlaceKind.Tournament)
            {
                title.text = Loc.T(Loc.UI, "story.rounds");
                list.AddToClassList("story-sheet__list--rounds");
                StoryRound next = story.NextRound(place);
                for (int i = 0; i < place.Rounds.Count; i++)
                {
                    StoryRound round = place.Rounds[i];
                    bool won = story.Wins(StoryProgress.RoundKey(place, round)) > 0;
                    VisualElement row = new VisualElement();
                    row.AddToClassList("story-round");
                    row.EnableInClassList("story-round--next", story.Entered(place) && round == next);
                    row.EnableInClassList("story-round--won", won);
                    Label name = new Label((won ? "✓  " : (i + 1) + "  ") + round.Name);
                    name.AddToClassList("story-round__name");
                    row.Add(name);
                    Label faction = new Label(round.DeckFaction.LocalName());
                    faction.AddToClassList("story-round__faction");
                    row.Add(faction);
                    if (round.Prize.Count > 0)
                        ElementTooltip.Attach(row, () => Loc.T(Loc.UI, "story.round_wins", string.Join(", ", round.Prize.Select(id => db.Get(id).LocalName()))));
                    list.Add(row);
                }
                return;
            }
            if (place.Sells.Count == 0)
            {
                box.style.display = DisplayStyle.None;
                return;
            }
            title.text = Loc.T(Loc.UI, "story.for_sale");
            // Rows made here, not a wrapping container: a scroll view measures wrapped cards one per row, a tall empty list
            int per_row = root.Q("PrizeBox").style.display == DisplayStyle.None ? SalesPerWideRow : SalesPerRow;
            VisualElement cards = null;
            for (int i = 0; i < place.Sells.Count; i++)
            {
                if (i % per_row == 0)
                {
                    cards = new VisualElement();
                    cards.AddToClassList("story-sale-row");
                    list.Add(cards);
                }
                cards.Add(Sale(root, story, place, i, on_change, db));
            }
        }

        // Cards for sale in a row: beside the prize, and across the sheet when there is none (a shop)
        private const int SalesPerRow = 5, SalesPerWideRow = 6;

        // A card for sale: its art (×n in its corner when you own some), its price tag; a click on either buys it
        private static VisualElement Sale(VisualElement root, StoryProgress story, StoryPlace place, int sale, Action<StoryReward> on_change, CardDatabase db)
        {
            StorySale item = place.Sells[sale];
            Card card = db.Get(item.Card);
            bool sold = story.Bought(place, sale);
            VisualElement cell = new VisualElement { name = "Sale " + sale };
            cell.AddToClassList("story-sale");
            cell.EnableInClassList("story-sale--sold", sold);
            Button art = new Button { name = "Card " + sale, text = "" };
            art.AddToClassList("story-sale__card");
            CardArt.Show(art, card._id);
            int owned = story.Owned(card._id);
            if (owned > 0)
            {
                Label badge = new Label("×" + owned) { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("story-sale__owned");
                art.Add(badge);
            }
            cell.Add(art);
            Button tag = new Button { name = "Buy " + sale, text = "" };
            tag.AddToClassList("story-sale__tag");
            if (sold)
            {
                Label label = new Label(Loc.T(Loc.UI, "story.sold")) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("story-sale__price");
                tag.Add(label);
                tag.SetEnabled(false);
                art.SetEnabled(false);
            }
            else
            {
                tag.Add(Coin("story-sale__coin"));
                Label price = new Label(item.Price.ToString()) { pickingMode = PickingMode.Ignore };
                price.AddToClassList("story-sale__price");
                tag.Add(price);
                tag.EnableInClassList("story-sale__tag--short", story.State.Crowns < item.Price);
            }
            cell.Add(tag);
            Action buy = () =>
            {
                string why = story.CannotBuy(place, sale);
                if (why != null)
                {
                    GameAudio.PlaySfx("invalid");
                    UIOverlay.Toast(why);
                    return;
                }
                UIOverlay.Confirm(Loc.T(Loc.UI, "story.buy_confirm", card.LocalName(), item.Price), Loc.T(Loc.UI, "story.buy"), Loc.T(Loc.UI, "story.not_now"), () =>
                {
                    StoryReward reward = story.Buy(place, sale);
                    GameAudio.PlaySfx("deck_add");
                    on_change(reward);
                    ShowList(root, story, place, on_change, db);
                });
            };
            if (!sold)
            {
                art.clicked += buy;
                tag.clicked += buy;
            }
            ElementTooltip.Attach(art, () => owned > 0 ? Loc.T(Loc.UI, "story.card_owned", card.LocalName(), owned) : card.LocalName());
            return cell;
        }

        // "Isengrim Faoiltiarna is yours"
        private static string Got(StoryReward reward, CardDatabase db)
        {
            if (reward.Cards.Count == 0)
                return Loc.T(Loc.UI, "story.got_none");
            if (reward.Cards.Count > 3)
                return Loc.T(Loc.UI, "story.got_many", reward.Cards.Count);
            return Loc.T(Loc.UI, reward.Cards.Count == 1 ? "story.got_one" : "story.got_few", string.Join(", ", reward.Cards.Select(id => db.Get(id).LocalName())));
        }
    }
}
