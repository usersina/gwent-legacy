using System;
using System.Linq;
using Gwent.Battle;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.DeckBuilding
{
    // The deck's leader, chosen in the carousel of the battle's redraw and pickers: the faction's leaders under a title,
    // the deck's own in front, the front one's ability at the bottom right. A side leader (the arrow keys, A/D, the wheel)
    // comes to the front; a click on the front one (or Enter) makes it the deck's leader and saves the deck (a preset's
    // edit goes to your own copy, as any edit does). The ×, Escape or a right click leave the deck as it was.
    public static class LeaderChoice
    {
        private static GameObject current;

        public static bool IsOpen { get { return current != null; } }

        // The carousel of the leaders, while it is open (the tools reach its cards through it)
        public static CardPicker Picker { get; private set; }

        public static void Open(DeckBuilder builder)
        {
            int[] all;
            if (current != null || !LeaderIds.ByFaction.TryGetValue(builder.my_deck.Faction, out all))
                return;
            // In the story, the leaders owned
            int[] leaders = all.Where(leader => DeckContext.Supply.Copies(builder.Db.Get(leader)) > 0).ToArray();
            if (leaders.Length == 0)
                return;
            // Over the whole screen, the deck builder dimmed under it and taking no click
            RectTransform root = UIKit.Rect("LeaderChoice", UIOverlay.Root).Stretch();
            root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.82f);
            current = root.gameObject;
            int front = Mathf.Max(0, Array.IndexOf(leaders, builder.my_deck.Leader));
            Picker = CardPicker.Open(root, "Choose your leader", leaders, leader => Choose(builder, leader), Close, front);
            Picker.StandIn(root.gameObject);
            UIOverlay.PushModal(root.gameObject, Closed);
            GameAudio.PlaySfx("card_select");
        }

        private static void Choose(DeckBuilder builder, int leader)
        {
            if (leader != builder.my_deck.Leader)
            {
                builder.my_deck.Leader = leader;
                builder.deckCollection.OnDeckEdit(); // saved, shown in the middle column, the battle deck updated
            }
            GameAudio.PlaySfx("deck_select");
            Close();
        }

        private static void Close()
        {
            if (current != null)
                UIOverlay.CloseModal(current);
        }

        private static void Closed()
        {
            current = null;
            Picker = null;
        }
    }
}
