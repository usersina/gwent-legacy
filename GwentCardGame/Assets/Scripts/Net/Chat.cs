using System.Text;
using Gwent.Core;

namespace Gwent.Net
{
    // What the players of a LAN battle say to each other: a quick line of The Witcher 3 (an emote, sent by its number) or a
    // message typed. A message keeps to what the game's fonts show (Latin-1: the Latin alphabets' letters, digits,
    // punctuation; no control characters), one space between words, up to 100 characters: kept so as it is typed
    // (Typed), and made so before it goes and when it comes (Clean). Shown as plain text (a "<b>" stays as typed).
    public static class Chat
    {
        public const int MaxLength = 100;

        // The quick lines, by number over the link (both devices run the same version): Line says one in the language of
        // whoever reads it, since only its number travels. Lines is the English they started from (the table "chat.quick_N"
        // holds them now): what counts is how many there are.
        public static readonly string[] Lines =
        {
            "Evening.", "Well played.", "Thanks.", "Hmm...", "Medallion's humming.", "Bollocks!", "Wind's howling.", "Damn, you're ugly.",
        };

        public static string Line(int number)
        {
            return Loc.T(Loc.UI, "chat.quick_" + number);
        }

        // Printable Latin-1 (not the soft hyphen: it shows nothing, a message of them would be an empty bubble)
        public static bool Allowed(char c)
        {
            return (c >= 0x20 && c <= 0x7E || c >= 0xA0 && c <= 0xFF) && c != '\u00AD';
        }

        // What is being typed, kept to the rule: other characters left out (a tab, a line break or a no-break space is a
        // space), no space first or twice, no more than 100 (a space last stays: the next word follows)
        public static string Typed(string text)
        {
            StringBuilder said = new StringBuilder(MaxLength);
            foreach (char typed in text ?? "")
            {
                if (said.Length == MaxLength)
                    break;
                char c = typed == '\t' || typed == '\n' || typed == '\r' || typed == '\u00A0' ? ' ' : typed;
                if (!Allowed(c) || c == ' ' && (said.Length == 0 || said[said.Length - 1] == ' '))
                    continue;
                said.Append(c);
            }
            return said.ToString();
        }

        // A message to send or show: kept to the rule and trimmed; null when nothing is left
        public static string Clean(string text)
        {
            string said = Typed(text).Trim();
            return said.Length > 0 ? said : null;
        }
    }
}
