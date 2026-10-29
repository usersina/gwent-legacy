namespace Gwent.Rules
{
    // How many copies of a card a deck may hold: every copy The Witcher 3 has (Play, Multiplayer, the free deck builder),
    // or, in the story, the copies the player owns (StoryProgress)
    public interface ICardSupply
    {
        int Copies(Card card);
    }

    public sealed class AllCards : ICardSupply
    {
        public static readonly AllCards Instance = new AllCards();

        private AllCards()
        {
        }

        public int Copies(Card card)
        {
            return DeckRules.MaxCopies(card);
        }
    }
}
