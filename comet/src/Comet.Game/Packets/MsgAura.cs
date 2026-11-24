namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A monk's aura, as the client keeps it: 4 action (ushort), 8 the monk's ID, 12 the aura
    // (AuraType), 16 its level, 20 its range in cells, 24 its power. 28 bytes. The client
    // shows level and power in the aura icon's tooltip. Given an owner's aura (Add), the
    // client of the owner or a teammate within its range asks for it (Ask), unless it has it.
    public sealed class MsgAura : MsgBase<Client>
    {
        public AuraAction Action { get; set; }
        public uint OwnerID { get; set; }
        public AuraType Aura { get; set; }
        public uint Level { get; set; }
        public uint Range { get; set; }
        public uint Power { get; set; }

        public MsgAura() => base.Type = PacketType.MsgAura;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (AuraAction)reader.ReadUInt16();
            reader.ReadUInt16();
            this.OwnerID = reader.ReadUInt32();
            this.Aura = (AuraType)reader.ReadUInt32();
            this.Level = reader.ReadUInt32();
            this.Range = reader.ReadUInt32();
            this.Power = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);  // 4
            writer.Write((ushort)0);
            writer.Write(this.OwnerID);         // 8
            writer.Write((uint)this.Aura);      // 12
            writer.Write(this.Level);           // 16
            writer.Write(this.Range);           // 20
            writer.Write(this.Power);           // 24
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => this.Action switch
        {
            AuraAction.Ask => Magic.Casting.AskForAuraAsync(client, this.OwnerID, this.Aura),
            _ => Task.CompletedTask,
        };

        public MsgAura With(AuraAction action) => new MsgAura
        {
            Action = action,
            OwnerID = this.OwnerID,
            Aura = this.Aura,
            Level = this.Level,
            Range = this.Range,
            Power = this.Power,
        };

        public enum AuraAction : ushort
        {
            // The owner's aura, for the client to check its range
            Add = 0,
            Remove = 1,
            // From the client: in range of an owner's aura it doesn't have
            Ask = 2,
            // The player has the aura, or no longer
            Got = 3,
            Refused = 4,
            Lost = 5,
        }

        public enum AuraType : uint
        {
            None = 0,
            Tyrant = 1,
            Fend = 2,
            Metal = 3,
            Wood = 4,
            Water = 5,
            Fire = 6,
            Earth = 7,
        }
    }
}
