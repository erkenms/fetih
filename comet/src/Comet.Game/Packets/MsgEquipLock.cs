namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // WCO (5808), 16 bytes: item ID at 4, mode byte at 8, a byte at 9 (3 on the unlock
    // date), a value at 12: the unlock date as yyyymmdd
    public sealed class MsgEquipLock : MsgBase<Client>
    {
        public uint ItemID { get; set; }
        public LockMode Mode { get; set; }
        public byte Flag { get; set; }
        public uint Value { get; set; }

        public MsgEquipLock() => base.Type = PacketType.MsgEquipLock;

        public MsgEquipLock(uint itemID, LockMode mode) : this()
        {
            this.ItemID = itemID;
            this.Mode = mode;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.ItemID = reader.ReadUInt32();
            this.Mode = (LockMode)reader.ReadByte();
            this.Flag = reader.ReadByte();
            reader.ReadUInt16();
            this.Value = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.ItemID);              // 4
            writer.Write((byte)this.Mode);          // 8
            writer.Write(this.Flag);                // 9
            writer.Write((ushort)0);                // 10
            writer.Write(this.Value);               // 12
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Locking.HandleAsync(client, this);

        public enum LockMode : byte
        {
            // From the client; the lock is echoed
            Lock = 0,
            // From the client, and to it with the unlock date
            Unlock = 1,
            Unlocked = 3,
        }
    }
}
