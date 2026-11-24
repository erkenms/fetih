namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 20 bytes: weather code at 4, intensity at 8, direction at 12 (signed degrees, -180 to
    // 179), color at 16. The client draws map weather only from this packet, and a new map
    // starts with none. Codes: 0 none, 1 rain, 2 snow, 3-12 falling leaves (ten looks), 13
    // sand, 14-18 flying flowers, 19 fireflies, 20 clouds. The client clamps intensity and
    // direction per kind, e.g. rain to 512 and +-40 degrees.
    public sealed class MsgWeather : MsgBase<Client>
    {
        public uint Code { get; set; }
        public uint Intensity { get; set; }
        public int Direction { get; set; }
        public uint Color { get; set; }

        public MsgWeather() => base.Type = PacketType.MsgWeather;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Code);        // 4
            writer.Write(this.Intensity);   // 8
            writer.Write(this.Direction);   // 12
            writer.Write(this.Color);       // 16
            return writer.ToArray();
        }
    }
}
