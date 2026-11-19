namespace Comet.Game.States
{
    using System;
    using System.Linq;

    // Plain ASCII so every client shows names the same whatever its Windows language.
    // '#' followed by digits is drawn as an emoji by the client.
    public static class CharacterRules
    {
        public const int MinNameLength = 4;       // the client asks for 4 to 15
        public const int MaxNameLength = 15;
        private static readonly string[] StaffTags = new[] { "[GM]", "[PM]" };

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
