namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    // Sent after each map change (Albetros 5517): map ID at 4, the map's file (doc) ID at 8,
    // the map type flags as a u64 at 12. Without it the 5619 client says path-finding
    // isn't available on the map.
    public sealed class MsgMapInfo : MsgBase<Client>
    {
        public uint MapID { get; set; }
        public uint DocID { get; set; }
        public MapFlags Flags { get; set; }

        public MsgMapInfo() => base.Type = PacketType.MsgMapInfo;

        // Maps are read from the client's GameMap.dat by ID, so a map is its own doc
        public static MsgMapInfo For(uint mapID) => new MsgMapInfo
        {
            MapID = mapID,
            DocID = mapID,
            Flags = Kernel.MapTypes.Of(mapID),
        };

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.MapID);           // 4
            writer.Write(this.DocID);           // 8
            writer.Write((ulong)this.Flags);    // 12
            return writer.ToArray();
        }
    }
}
