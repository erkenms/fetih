namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 5517 layout (Albetros), 40 bytes: the other player's ID at 4, action byte at 8,
    // online byte at 9, two ranks at 12 and 16 (left 0), the name (16 bytes) at 20
    public sealed class MsgFriend : MsgBase<Client>
    {
        public const int Size = 40;

        public uint ID { get; set; }
        public FriendAction Action { get; set; }
        public bool Online { get; set; }
        public string Name { get; set; }

        public MsgFriend() => base.Type = PacketType.MsgFriend;

        public MsgFriend(FriendAction action, uint id, string name, bool online) : this()
        {
            this.Action = action;
            this.ID = id;
            this.Name = name;
            this.Online = online;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.ID = reader.ReadUInt32();
            this.Action = (FriendAction)reader.ReadByte();
            this.Online = reader.ReadByte() != 0;
            if (bytes.Length >= 36)
            {
                reader.ReadBytes(10);
                this.Name = reader.ReadString(16);
            }
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.ID);                          // 4
            writer.Write((byte)this.Action);                // 8
            writer.Write((byte)(this.Online ? 1 : 0));      // 9
            writer.Write(new byte[10]);                     // 10
            writer.Write(this.Name ?? string.Empty, 16);    // 20
            writer.Write(0u);                               // 36
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Relationships.HandleAsync(client, this);

        public enum FriendAction : byte
        {
            // From the client with the other player's ID; forwarded to them with the asker's
            Apply = 10,
            Accept = 11,
            Online = 12,
            Offline = 13,
            Break = 14,
            // A friend in the list, with whether they're online
            GetInfo = 15,
            EnemyOnline = 16,
            EnemyOffline = 17,
            EnemyDelete = 18,
            EnemyAdd = 19,
        }
    }
}
