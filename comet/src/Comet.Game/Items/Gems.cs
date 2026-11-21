namespace Comet.Game.Items
{
    // A socket holds the gem's item ID modulo 1000: the kind in the tens, the grade (1 normal,
    // 2 refined, 3 super) in the ones. 0 is no socket, 255 an empty one.
    public static class Gems
    {
        public const int Phoenix = 0, Dragon = 1, Fury = 2, Rainbow = 3, Kylin = 4, Violet = 5,
            Moon = 6, Tortoise = 7, Thunder = 10, Glory = 12;

        public const byte NoSocket = 0, EmptySocket = 255;

        public const int MaxTortoisePercent = 54;

        public static bool IsGemItem(uint typeID) => typeID / 10000 == 70;

        public static int Kind(byte socket) =>
            socket is NoSocket or EmptySocket || socket % 10 is < 1 or > 3 ? -1 : socket / 10;

        public static int Grade(byte socket) => Kind(socket) < 0 ? 0 : socket % 10;

        // What one gem adds, in percent: of attack, magic attack, hit rate, experience, durability,
        // weapon experience, magic experience, or off damage taken
        public static int Percent(byte socket) => (Kind(socket), Grade(socket)) switch
        {
            (_, 0) => 0,
            (Phoenix or Dragon or Fury, var g) => 5 * g,
            (Rainbow, var g) => new[] { 10, 15, 25 }[g - 1],
            (Kylin, var g) => new[] { 50, 100, 200 }[g - 1],
            (Violet, var g) => new[] { 30, 50, 100 }[g - 1],
            (Moon, var g) => new[] { 15, 30, 50 }[g - 1],
            (Tortoise, var g) => 2 * g,
            _ => 0,
        };
    }
}
