namespace Comet.Game.Packets
{
    using System;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 36 bytes: action at 4 (the client reads it as a byte), seven u32 values from 8.
    // Action 0 sets the clock behind the status line: year - 1900, month - 1, day of year
    // (ignored), day, hour, minute, second. The client runs them through mktime and shows
    // them back with localtime, so they appear as sent whatever the client's time zone.
    // Action 2 is the steed's vigor.
    public sealed class MsgData : MsgBase<Client>
    {
        public const uint ServerTimeAction = 0;
        public const uint VigorAction = 2;

        public uint Action { get; set; }
        public uint[] Values { get; set; } = new uint[7];

        public MsgData() => base.Type = PacketType.MsgData;

        public static MsgData ServerTime(DateTime time) => new MsgData
        {
            Action = ServerTimeAction,
            Values = new[]
            {
                (uint)(time.Year - 1900), (uint)(time.Month - 1), (uint)(time.DayOfYear - 1),
                (uint)time.Day, (uint)time.Hour, (uint)time.Minute, (uint)time.Second,
            },
        };

        public static MsgData Vigor(uint vigor) => new MsgData
        {
            Action = VigorAction,
            Values = new uint[] { vigor, 0, 0, 0, 0, 0, 0 },
        };

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Action);      // 4
            for (int i = 0; i < 7; i++)     // 8
                writer.Write(i < this.Values.Length ? this.Values[i] : 0u);
            return writer.ToArray();
        }
    }
}
