namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Black Spot's mark over a target, which the client draws until told to remove it:
    // 4 remove (bool), 8 target ID. 12 bytes. Not yet seen in the 5619 client, which has
    // the message.
    public sealed class MsgDeadMark : MsgBase<Client>
    {
        public bool Remove { get; }
        public uint TargetID { get; }

        public MsgDeadMark(bool remove, uint targetID)
        {
            base.Type = PacketType.MsgDeadMark;
            this.Remove = remove;
            this.TargetID = targetID;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Remove);          // 4
            writer.Write(new byte[3]);
            writer.Write(this.TargetID);        // 8
            return writer.ToArray();
        }
    }
}
