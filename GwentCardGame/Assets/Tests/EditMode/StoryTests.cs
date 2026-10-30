using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Gwent.Rules.Tests
{
    // The story's content (Resources/Story/story.json) and its rules (StoryProgress): every card has a source and the
    // whole story can be played through to Collect 'Em All; the opponents' decks are valid; a prize is won once, a
    // rematch pays a quarter, the random cards keep their order across a reload; shops, tournaments, quests and the
    // regions behave as The Witcher 3's do.
    public class StoryTests
    {
        private const string StoryPath = "Assets/Resources/Story/story.json";

        private static StoryContent content;

        private static CardDatabase Db { get { return TestBoard.Db; } }

        private static StoryContent Content
        {
            get
            {
                if (content == null)
                    content = new StoryContent(JsonUtility.FromJson<StoryFile>(File.ReadAllText(StoryPath)));
                return content;
            }
        }

        private static StoryProgress NewStory(int seed = 7)
        {
            return new StoryProgress(Content, StoryProgress.NewState(Content, seed), Db);
        }

        private static StoryPlace Place(string id)
        {
            StoryPlace place = Content.Place(id);
            Assert.That(place, Is.Not.Null, id);
            return place;
        }

        // A story saved and read back (as StorySave does, through JsonUtility)
        private static StoryProgress Reloaded(StoryProgress story)
        {
            StoryState state = JsonUtility.FromJson<StoryState>(JsonUtility.ToJson(story.State));
            return new StoryProgress(Content, state, Db);
        }

        // Everything there is to do, done: every place beaten, found or given, every tournament won through, every card
        // bought; rematches for crowns when a shop or a fee waits for them
        private static void PlayThrough(StoryProgress story)
        {
            for (int guard = 0; guard < 2000; guard++)
            {
                bool changed = false;
                foreach (StoryPlace place in Content.Places.Where(story.IsVisible).ToList())
                {
                    if (place.Taken && !story.IsDone(place))
                    {
                        story.Take(place);
                        changed = true;
                    }
                    else if (place.Type == StoryPlaceKind.Player && story.Wins(place.Id) == 0)
                    {
                        story.Win(place);
                        changed = true;
                    }
                    else if (place.Type == StoryPlaceKind.Tournament && !story.IsDone(place) && story.CannotEnter(place) == null)
                    {
                        story.Enter(place);
                        while (story.Entered(place))
                            story.WinRound(place);
                        changed = true;
                    }
                    for (int sale = 0; sale < place.Sells.Count; sale++)
                        if (story.CannotBuy(place, sale) == null)
                        {
                            story.Buy(place, sale);
                            changed = true;
                        }
                }
                if (changed)
                    continue;
                bool waiting = Content.Places.Where(story.IsVisible).Any(place => story.HasMore(place) || (place.Type == StoryPlaceKind.Tournament && !story.IsDone(place)));
                if (!waiting)
                    return;
                story.Win(Content.Places.First(place => place.Type == StoryPlaceKind.Player && story.IsVisible(place))); // a rematch
            }
            Assert.Fail("The story could not be played through");
        }

        [Test]
        public void TheContentHasNoProblems()
        {
            Assert.IsEmpty(Content.Problems(Db));
        }

        [Test]
        public void EveryPlaceLiesOnItsMap()
        {
            foreach (StoryPlace place in Content.Places.Where(place => place.Placed))
            {
                StoryMap map = Content.Map(Content.Region(place.Region).Map);
                Assert.That(map.Rows, Is.GreaterThan(0), map.Id + " has no geometry (tools/story-maps.py)");
                (float x, float y) = map.Position(place.Lat, place.Lng);
                Assert.That(x, Is.InRange(0.01f, 0.99f), place.Id);
                Assert.That(y, Is.InRange(0.01f, 0.99f), place.Id);
            }
        }

        [Test]
        public void TheStoryStartsWithTheWitcher3sStarterDeck()
        {
            StoryProgress story = NewStory();
            List<int> leaders = Content.File.Starter.Where(id => Db.Get(id).IsLeader).ToList();
            Assert.That(leaders, Is.EquivalentTo(new[] { LeaderIds.FoltestKingOfTemeria, LeaderIds.EmhyrHisImperialMajesty, LeaderIds.EredinKingOfTheWildHunt, LeaderIds.FrancescaPurebloodElf }));
            Deck deck = new Deck { Faction = Faction.NorthernRealms, Leader = LeaderIds.FoltestKingOfTemeria, Cards = Content.File.Starter.Where(id => !Db.Get(id).IsLeader).ToList() };
            Assert.That(deck.Cards.Count(id => Db.Get(id).IsUnit), Is.EqualTo(22));
            Assert.IsNull(story.DeckProblem(deck));
            Assert.That(story.State.Crowns, Is.Zero);
            Assert.That(story.Current, Is.SameAs(Content.First));
            Assert.That(story.OpenFactions, Is.EquivalentTo(new[] { Faction.NorthernRealms, Faction.Nilfgaard, Faction.Scoiatael, Faction.Monsters }));
        }

        [Test]
        public void EveryOpponentHasAValidDeckAlwaysTheSame()
        {
            int decks = 0;
            foreach (StoryPlace place in Content.Places)
            {
                IEnumerable<StoryRound> rounds = place.Type == StoryPlaceKind.Tournament ? place.Rounds : place.Type == StoryPlaceKind.Player ? new StoryRound[] { null } : new StoryRound[0];
                foreach (StoryRound round in rounds)
                {
                    Deck deck = DeckGenerator.For(place, round, Db);
                    string who = place.Id + (round != null ? "/" + round.Id : "");
                    Assert.IsEmpty(DeckRules.Validate(deck, Db), who);
                    Assert.That(deck.Cards, Is.EqualTo(DeckGenerator.For(place, round, Db).Cards), who + " plays another deck the second time");
                    if (round != null && round.Leader != 0)
                        Assert.That(deck.Leader, Is.EqualTo(round.Leader), who);
                    else if (place.Leader != 0)
                        Assert.That(deck.Leader, Is.EqualTo(place.Leader), who);
                    decks++;
                }
            }
            Assert.That(decks, Is.GreaterThan(100));
        }

        [Test]
        public void TheFirstPlayersPlayNoHeroesAndTheLastPlayTheirPrize()
        {
            foreach (StoryPlace place in Content.Places.Where(place => place.Type == StoryPlaceKind.Player && place.Tier == 1))
            {
                Deck deck = DeckGenerator.For(place, null, Db);
                Assert.That(deck.Cards.Where(id => Db.Get(id).unique && !place.Prize.Contains(id)), Is.Empty, place.Id);
            }
            Assert.That(DeckGenerator.For(Place("stjepan"), null, Db).Leader, Is.EqualTo(LeaderIds.EmhyrTheWhiteFlame));
            Assert.That(DeckGenerator.For(Place("vimme_vivaldi"), null, Db).Cards, Has.Member(Place("vimme_vivaldi").Prize[0]));
        }

        [Test]
        public void TheWholeStoryCanBePlayedToCollectEmAll([Values(1, 2, 3)] int seed)
        {
            StoryProgress story = NewStory(seed);
            PlayThrough(story);
            Assert.That(Content.Regions.Where(region => !story.IsOpen(region)).Select(region => region.Id), Is.Empty);
            Assert.That(Content.Quests.Where(quest => !story.QuestDone(quest)).Select(quest => quest.Id), Is.Empty);
            Assert.That(Content.Places.Where(place => !place.Plays && !place.Taken ? false : !story.IsDone(place)).Select(place => place.Id), Is.Empty);
            Assert.That(story.PoolLeft, Is.Zero);
            Assert.That(Db.All.Where(card => story.Owned(card._id) != card.copies).Select(card => card.name), Is.Empty);
            Assert.IsTrue(story.CollectionComplete);
            Assert.That(story.OpenFactions.Count, Is.EqualTo(5));
            foreach (StoryRegion region in Content.Regions)
                Assert.That(story.StillIn(region), Is.Empty, region.Id);
        }

        [Test]
        public void APrizeIsWonOnceAndARematchPaysAQuarter()
        {
            StoryProgress story = NewStory();
            StoryPlace scholar = Place("wo_scholar");
            StoryReward first = story.Win(scholar);
            Assert.That(first.Cards, Is.EqualTo(scholar.Prize));
            Assert.That(first.Crowns, Is.EqualTo(scholar.Crowns));
            StoryReward again = story.Win(scholar);
            Assert.That(again.Cards, Is.Empty);
            Assert.That(again.Crowns, Is.EqualTo(scholar.Crowns / StoryProgress.RematchShare));
            Assert.That(story.Owned(scholar.Prize[0]), Is.EqualTo(1));
            Assert.That(story.Wins(scholar.Id), Is.EqualTo(2));
        }

        [Test]
        public void TheScholarOpensVizimaAndTheNoblemanVelen()
        {
            StoryProgress story = NewStory();
            StoryRegion vizima = Content.Region("vizima"), velen = Content.Region("velen");
            Assert.IsFalse(story.IsOpen(vizima));
            Assert.That(story.Win(Place("wo_scholar")).Regions, Is.EqualTo(new[] { vizima }));
            Assert.IsFalse(story.IsOpen(velen));
            Assert.That(story.Win(Place("vizima_nobleman")).Regions, Is.EqualTo(new[] { velen }));
            Assert.IsTrue(story.IsVisible(Place("bloody_baron")));
        }

        [Test]
        public void TheBloodyBaronsListBringsOutVelensPlayersAndTheirQuestAnEredin()
        {
            StoryProgress story = NewStory();
            story.Win(Place("wo_scholar"));
            story.Win(Place("vizima_nobleman"));
            Assert.IsFalse(story.IsVisible(Place("haddy")));
            StoryReward baron = story.Win(Place("bloody_baron"));
            Assert.That(baron.Revealed.Select(place => place.Id), Is.EquivalentTo(new[] { "haddy", "old_sage", "boatwright" }));
            story.Win(Place("haddy"));
            story.Win(Place("old_sage"));
            StoryReward last = story.Win(Place("boatwright"));
            Assert.That(last.Quests.Select(quest => quest.Id), Is.EqualTo(new[] { "velen_players" }));
            Assert.That(last.Cards, Has.Member(LeaderIds.EredinDestroyerOfWorlds));
            Assert.That(last.Regions.Select(region => region.Id), Is.EqualTo(new[] { "skellige" }));
        }

        [Test]
        public void AShopIsOpenFromTheStartAndSellsEachCardOnceForItsPrice()
        {
            StoryProgress story = NewStory();
            StoryPlace elsa = Place("wo_elsa");
            Assert.IsTrue(story.IsVisible(elsa));
            Assert.That(story.CannotBuy(elsa, 0), Does.StartWith("You need"));
            story.State.Crowns = 100;
            int card = elsa.Sells[0].Card, before = story.Owned(card);
            StoryReward bought = story.Buy(elsa, 0);
            Assert.That(bought.Cards, Is.EqualTo(new[] { card }));
            Assert.That(story.State.Crowns, Is.EqualTo(100 - elsa.Sells[0].Price));
            Assert.That(story.Owned(card), Is.EqualTo(before + 1));
            Assert.That(story.CannotBuy(elsa, 0), Is.EqualTo("Bought already"));
            Assert.IsTrue(Reloaded(story).Bought(elsa, 0));
            Assert.IsTrue(story.IsVisible(Place("marquise_serenity")) == story.IsOpen(Content.Region("velen")), "Serenity's shop is there with Novigrad");
        }

        [Test]
        public void HeartsOfStonesShopsWaitForSkelligeStyleAsItsPlayersDo()
        {
            StoryProgress story = NewStory();
            story.Win(Place("wo_scholar"));
            story.Win(Place("vizima_nobleman"));
            foreach (string id in new[] { "dulla", "circus_merchant", "shani" })
                Assert.IsFalse(story.IsVisible(Place(id)), id + " is there before Skellige Style is done");
            Assert.IsTrue(story.IsVisible(Place("marquise_serenity")), "Novigrad's shops are open from the start");
        }

        [Test]
        public void TheRandomCardsKeepTheirOrderAcrossAReload()
        {
            List<StoryPlace> generic = Content.Places.Where(place => place.Random && place.Region == "velen").Take(6).ToList();
            StoryProgress straight = NewStory(42);
            List<int> cards = generic.SelectMany(place => straight.Win(place).Cards).ToList();
            StoryProgress reloaded = NewStory(42);
            List<int> again = new List<int>();
            foreach (StoryPlace place in generic)
            {
                again.AddRange(reloaded.Win(place).Cards);
                reloaded = Reloaded(reloaded);
            }
            Assert.That(cards.Count, Is.EqualTo(6));
            Assert.That(again, Is.EqualTo(cards));
            Assert.That(reloaded.State.Drawn, Is.EqualTo(6));
            StoryProgress other = NewStory(43);
            Assert.That(generic.SelectMany(place => other.Win(place).Cards), Is.Not.EqualTo(cards), "another story draws another order");
        }

        [Test]
        public void ALostRoundEndsTheRunAndTheFeeIsPaidAgain()
        {
            StoryProgress story = NewStory();
            StoryPlace stakes = Place("high_stakes");
            story.State.Crowns = stakes.Fee * 2;
            Assert.IsNull(story.CannotEnter(stakes));
            story.Enter(stakes);
            Assert.That(story.State.Crowns, Is.EqualTo(stakes.Fee));
            StoryReward first = story.WinRound(stakes);
            Assert.That(first.Cards, Is.EqualTo(stakes.Rounds[0].Prize));
            Assert.That(story.NextRound(stakes), Is.SameAs(stakes.Rounds[1]));
            story.LoseRound(stakes);
            Assert.IsFalse(story.Entered(stakes));
            Assert.That(story.NextRound(stakes), Is.SameAs(stakes.Rounds[0]));
            story.Enter(stakes);
            Assert.That(story.State.Crowns, Is.EqualTo(stakes.Rounds[0].Crowns), "the fee paid twice, the first round's crowns kept");
            Assert.That(story.WinRound(stakes).Cards, Is.Empty, "a round's prize is won once");
            Assert.That(story.CannotEnter(Place("masquerade")), Does.StartWith("Finish"));
        }

        [Test]
        public void TheBeauclairTournamentTakesSkelligeDecksOnly()
        {
            StoryProgress story = NewStory();
            StoryPlace tournament = Place("beauclair_tournament");
            Deck starter = new Deck { Faction = Faction.NorthernRealms, Leader = LeaderIds.FoltestKingOfTemeria, Cards = Content.File.Starter.Where(id => !Db.Get(id).IsLeader).ToList() };
            Assert.That(story.CannotPlayWith(tournament, starter), Does.Contain("Skellige decks only"));
            Assert.IsNull(story.CannotPlayWith(Place("wo_scholar"), starter));
            Assert.IsFalse(story.OpenFactions.Contains(Faction.Skellige));
            story.Take(Place("monnier_deck"));
            Assert.IsTrue(story.OpenFactions.Contains(Faction.Skellige));
        }

        [Test]
        public void AStoryDeckHoldsOnlyTheCopiesOwned()
        {
            StoryProgress story = NewStory();
            const int catapult = 11;
            Deck deck = new Deck { Faction = Faction.NorthernRealms, Leader = LeaderIds.FoltestTheSteelForged, Cards = Content.File.Starter.Where(id => !Db.Get(id).IsLeader).Concat(new[] { catapult, catapult }).ToList() };
            Assert.That(story.DeckProblem(deck), Is.EqualTo("You don't own Foltest the Steel-Forged"));
            deck.Leader = LeaderIds.FoltestKingOfTemeria;
            Assert.That(story.DeckProblem(deck), Is.EqualTo("You don't own Catapult"));
            DeckRules.Sanitize(deck, Db, story);
            Assert.That(deck.Cards, Has.No.Member(catapult));
            Assert.IsNull(story.DeckProblem(deck));
            deck.Leader = LeaderIds.FoltestTheSteelForged;
            DeckRules.Sanitize(deck, Db, story);
            Assert.That(deck.Leader, Is.EqualTo(LeaderIds.FoltestKingOfTemeria), "a leader not owned gives way to one owned");
            Deck free = new Deck { Faction = Faction.NorthernRealms, Leader = LeaderIds.FoltestTheSteelForged, Cards = new List<int> { catapult, catapult } };
            DeckRules.Sanitize(free, Db);
            Assert.That(free.Cards.Count(id => id == catapult), Is.EqualTo(2), "the free deck builder keeps every copy");
            Assert.That(free.Leader, Is.EqualTo(LeaderIds.FoltestTheSteelForged));
        }

        [Test]
        public void TheCollectionGuideCountsWhatIsLeftInARegion()
        {
            StoryProgress story = NewStory();
            StoryRegion orchard = Content.Region("white_orchard");
            int left = story.StillIn(orchard).Count;
            Assert.That(left, Is.EqualTo(Place("wo_elsa").Sells.Count + Place("wo_scholar").Prize.Count));
            story.Win(Place("wo_scholar"));
            Assert.That(story.StillIn(orchard).Count, Is.EqualTo(left - 1));
        }
    }
}
