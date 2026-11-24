namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 80 bytes: mode byte at 4, a u32 at 8 and a byte at 14, from the client's flower and
    // gift ranking windows. On login the client clears its flower rank and sends mode 2 with
    // both zero, asking for the player's own flower ranks. Until flowers exist there is
    // nothing to rank, and an unanswered query leaves the client showing none.
    public sealed class MsgRank : MsgBase<Client>
    {
        public const byte OwnFlowerRankMode = 2;

        public byte Mode { get; set; }
        public uint Data { get; set; }

        public MsgRank() => base.Type = PacketType.MsgRank;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Mode = reader.ReadByte();
            reader.ReadBytes(3);
            this.Data = reader.ReadUInt32();
        }

        public override Task ProcessAsync(Client client)
        {
            if (this.Mode != OwnFlowerRankMode)
                LogEvents.For<MsgRank>(LogEvents.PacketUnhandled).Warning(
                    "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                    this.Type, this.Mode, this.Length);
            return Task.CompletedTask;
        }
    }
}
