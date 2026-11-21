namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 36 bytes: action at 4 (2 = the steed's vigor), value at 8, zeros up to 36
    public sealed class MsgData : MsgBase<Client>
    {
        public const uint VigorAction = 2;

        public uint Action { get; set; }
        public uint Value { get; set; }

        public MsgData() => base.Type = PacketType.MsgData;

        public static MsgData Vigor(uint vigor) => new MsgData { Action = VigorAction, Value = vigor };

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Action);      // 4
            writer.Write(this.Value);       // 8
            writer.Write(new byte[24]);     // 12
            return writer.ToArray();
        }
    }
}
