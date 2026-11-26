namespace Fetih.Game.States
{
    public sealed class LevelExp
    {
        public byte Level { get; init; }
        public ulong Experience { get; init; }

        // The time a level is meant to take; an EXP ball is worth 600 of it
        public uint UpLevTime { get; init; }
    }
}
