namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Who asks, shown in the target's request popup ("Level: %d Battle Power: %d") for a
    // trade, team or friend request; sent just before the request. Guessed from memory of
    // later servers, unconfirmed: asker ID at 4, target ID at 8, the asker's level at 12
    // and battle power at 16, then relation flags (spouse, trade partner, ...) left 0.
    public sealed class MsgRelation : MsgBase<Client>
    {
        public const int Size = 36;

        public uint AskerID { get; set; }
        public uint TargetID { get; set; }
        public uint Level { get; set; }
        public uint BattlePower { get; set; }

        public MsgRelation() => base.Type = PacketType.MsgRelation;

        public MsgRelation(Character asker, uint targetID) : this()
        {
            this.AskerID = asker.CharacterID;
            this.TargetID = targetID;
            this.Level = asker.Level;
            this.BattlePower = Attributes.BattlePower(asker);
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.AskerID);             // 4
            writer.Write(this.TargetID);            // 8
            writer.Write(this.Level);               // 12
            writer.Write(this.BattlePower);         // 16
            writer.Write(new byte[Size - 20]);      // 20 relation flags
            return writer.ToArray();
        }
    }
}
