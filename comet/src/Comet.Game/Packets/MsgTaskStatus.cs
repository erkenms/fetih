namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 8 + 12n bytes, at most 80 entries: action u16 at 4, entry count u16 at 6, then from 8
    // each entry is a task ID u32, a status u16 at +4 and the time left u32 at +8.
    // When the game starts the client sends action 3 listing every task in its mission data,
    // in as many packets as it takes, asking for their status. A server without tasks
    // leaves them as the client already has them: not accepted. Action 4 with status 5 says
    // a timed task ran out, which only happens to tasks the server started.
    public sealed class MsgTaskStatus : MsgBase<Client>
    {
        public const ushort QueryAction = 3;

        public ushort Action { get; set; }
        public uint[] TaskIds { get; set; } = new uint[0];

        public MsgTaskStatus() => base.Type = PacketType.MsgTaskStatus;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = reader.ReadUInt16();
            ushort count = reader.ReadUInt16();
            int fit = (bytes.Length - 8) / 12;
            this.TaskIds = new uint[count < fit ? count : fit];
            for (int i = 0; i < this.TaskIds.Length; i++)
            {
                this.TaskIds[i] = reader.ReadUInt32();
                reader.ReadBytes(8);
            }
        }

        public override Task ProcessAsync(Client client)
        {
            if (this.Action != QueryAction)
                LogEvents.For<MsgTaskStatus>(LogEvents.PacketUnhandled).Warning(
                    "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                    this.Type, this.Action, this.Length);
            return Task.CompletedTask;
        }
    }
}
