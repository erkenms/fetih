namespace Comet.Game.World
{
    /// <summary>
    /// Where new characters start, and where a character is moved to if their saved
    /// map is gone or their saved cell is blocked.
    /// </summary>
    public static class StartPosition
    {
        // The newbie village (5619: confirmed in VM)
        public const uint MapID = 1010;
        public const ushort X = 61;
        public const ushort Y = 109;
    }
}
