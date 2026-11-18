namespace Comet.Game.States
{
    using System;
    using System.Linq;

    /// <summary>
    /// Rules for new character names. Names are plain ASCII so every client shows them the
    /// same, whatever the player's Windows language. Players may use any printable symbol
    /// (including '#' followed by digits, which the client draws as an emoji), but not the
    /// staff tags.
    /// </summary>
    public static class CharacterRules
    {
        // Constants
        public const int MinNameLength = 4;       // the 5619 client asks for 4 to 15
        public const int MaxNameLength = 15;
        private static readonly string[] StaffTags = new[] { "[GM]", "[PM]" };

        /// <summary>
        /// Checks a character name. Returns null if it's allowed, or the reason it isn't
        /// (<c>invalid_name</c>).
        /// </summary>
        /// <param name="name">Name as sent by the client, one character per byte</param>
        public static string CheckName(string name)
        {
            if (name == null || name.Length < MinNameLength || name.Length > MaxNameLength)
                return "invalid_name";
            if (!name.All(c => c > ' ' && c < 0x7F))
                return "invalid_name";
            if (StaffTags.Any(tag => name.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                return "invalid_name";
            return null;
        }
    }
}
