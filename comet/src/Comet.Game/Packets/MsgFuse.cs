namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The 5619 compose window (window 2): the main item at 4, a ushort at 8 (always 0), the
    // count of minor items as a ushort at 10, then the minors (uint each) from 12. The client
    // doesn't process it, so it isn't answered; the items' updates are.
    public sealed class MsgFuse : MsgBase<Client>
    {
        public const int MaxMinors = 40;

        public uint MainID { get; set; }
        public ushort Mode { get; set; }
        public uint[] MinorIDs { get; set; } = new uint[0];

        public MsgFuse() => base.Type = PacketType.MsgFuse;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.MainID = reader.ReadUInt32();
            this.Mode = reader.ReadUInt16();
            int count = reader.ReadUInt16();
            int available = Math.Max(0, (bytes.Length - 12) / 4);
            this.MinorIDs = new uint[Math.Min(Math.Min(count, available), MaxMinors)];
            for (int i = 0; i < this.MinorIDs.Length; i++)
                this.MinorIDs[i] = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.MainID);
            writer.Write(this.Mode);
            writer.Write((ushort)this.MinorIDs.Length);
            foreach (var id in this.MinorIDs)
                writer.Write(id);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            foreach (var minor in this.MinorIDs)
                await Composing.ComposeAsync(client, this.MainID, minor);
        }
    }
}
