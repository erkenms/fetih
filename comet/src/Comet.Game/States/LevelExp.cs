namespace Comet.Game.States
{
    public sealed class LevelExp
    {
        public byte Level { get; init; }
        public ulong Experience { get; init; }

        // TQ's time to level up; an EXP ball is worth 600 of it
        public uint UpLevTime { get; init; }
    }
}
