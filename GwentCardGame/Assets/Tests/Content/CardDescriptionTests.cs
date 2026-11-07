using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Gwent.Content.Tests
{
    // Every card has the description box The Witcher 3 gives it (CardDescription), with its icon
    public class CardDescriptionTests
    {
        // A special card, a leader, a unit with an ability, a hero, an agile unit or the Cow
        private static bool Described(Card card)
        {
            return card.faction == Faction.Special || card.ability != Ability.None || card.unique || card.row == Row.Agile
                || AbilityRules.AvengerOf(card._id) != -1;
        }

        [Test]
        public void EveryCardWithSomethingToSayHasADescription()
        {
            List<string> missing = CardLibrary.Get().All.Where(card => Described(card) && CardDescription.For(card) == null)
                .Select(card => card.name).ToList();
            Assert.IsEmpty(missing, "Cards with no description");
            foreach (Card card in CardLibrary.Get().All.Where(Described))
            {
                CardDescription description = CardDescription.For(card);
                Assert.IsFalse(string.IsNullOrWhiteSpace(description.TitleKey), card.name + " has no title");
                Assert.IsFalse(string.IsNullOrWhiteSpace(description.TextKey), card.name + " has no text");
            }
        }

        // The Cards table's keys (Assets/Localization/Source/Cards.csv): its rows are "key,en,es", and no key has a comma
        private static HashSet<string> TableKeys()
        {
            string path = Path.Combine(Application.dataPath, "Localization", "Source", "Cards.csv");
            return new HashSet<string>(File.ReadAllLines(path).Skip(1).Select(line => line.Substring(0, Mathf.Max(line.IndexOf(','), 0))));
        }

        // The texts are looked up by key when the box is drawn: a key the table lacks would show as the key itself
        [Test]
        public void EveryDescriptionTextIsInTheCardsTable()
        {
            HashSet<string> keys = TableKeys();
            List<string> missing = new List<string>();
            foreach (Card card in CardLibrary.Get().All)
            {
                CardDescription description = CardDescription.For(card);
                if (description == null)
                    continue;
                foreach (string key in new[] { description.TitleKey, description.TextKey })
                    if (!keys.Contains(key))
                        missing.Add(card.name + ": " + key);
            }
            Assert.IsEmpty(missing, "Description keys the Cards table lacks");
        }

        [Test]
        public void EveryCardIsNamedInTheCardsTable()
        {
            HashSet<string> keys = TableKeys();
            Assert.IsEmpty(CardLibrary.Get().All.Where(card => !keys.Contains(card._idstr)).Select(card => card._idstr).ToList(),
                "Cards whose name the Cards table lacks");
        }

        [Test]
        public void APlainUnitHasNone()
        {
            List<string> described = CardLibrary.Get().All.Where(card => !Described(card) && CardDescription.For(card) != null)
                .Select(card => card.name).ToList();
            Assert.IsEmpty(described, "Plain units with a description (The Witcher 3 shows none)");
        }

        [Test]
        public void EveryIconIsThere()
        {
            foreach (Card card in CardLibrary.Get().All)
            {
                CardDescription description = CardDescription.For(card);
                if (description != null && description.Icon != null)
                    Assert.IsNotNull(Resources.Load<Sprite>("Cards/DescriptionIcon/" + description.Icon), card.name + "'s icon " + description.Icon);
            }
        }
    }
}
