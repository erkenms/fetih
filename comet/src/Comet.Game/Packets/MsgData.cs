namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 36 bytes: action at 4 (the client reads it as a byte), seven u32 values from 8.
    // Action 0 sets the clock behind the status line: year - 1900, month - 1, day of year
    // (ignored), day, hour, minute, second. The client runs them through mktime and shows
    // them back with localtime, so they appear as sent whatever the client's time zone.
    // Action 2 is the steed's vigor.
    // Actions 3 and 4 are the client's anti-cheat check. Now and then it sends action 4 with
    // a packet type (MsgTalk, MsgInteract or MsgAction) at 8, asking how many of those the
    // server has received since it entered the game. An answer is action 3 with the type at 8
    // and the count at 12; when it differs from the client's own count, the client reports
    // cheating. Leaving it unanswered is what the client expects of a server that doesn't
    // track this, and costs nothing.
    public sealed class MsgData : MsgBase<Client>
    {
        public const uint ServerTimeAction = 0;
        public const uint VigorAction = 2;
        public const uint AskMessageCountAction = 4;

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

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = reader.ReadUInt32();
            for (int i = 0; i < 7; i++)
                this.Values[i] = reader.ReadUInt32();
        }

        public override Task ProcessAsync(Client client)
        {
            if (this.Action != AskMessageCountAction)
                LogEvents.For<MsgData>(LogEvents.PacketUnhandled).Warning(
                    "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                    this.Type, this.Action, this.Length);
            return Task.CompletedTask;
        }

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
