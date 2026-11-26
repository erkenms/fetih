namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Guild arsenal overview (2201, 24 + count * 24 bytes): 0 (u16) at 4, total arsenal BP at 8,
    // player's arsenal donation at 12, shared BP at 16, pole count at 20, then 24-byte entries
    // from 24: type (+0), donation BP (+4), enhancement (+8), donation (+12), enhancement
    // expiration YYYYMMDD (+16), open flag (+20, 1 byte + 3 padding).
    public sealed class MsgTotemPoleInfo : MsgBase<Client>
    {
        public const int HeaderSize = 24;
        public const int EntrySize = 24;

        public readonly record struct PoleEntry(
            TotemPoleType Type,
            uint BattlePower,
            uint Enhancement,
            uint Donation,
            uint EnhancementExpiration,
            bool Open);

        public uint TotalBattlePower { get; set; }
        public uint PlayerDonation { get; set; }
        public uint SharedBattlePower { get; set; }
        public List<PoleEntry> Poles { get; } = new List<PoleEntry>();

        public MsgTotemPoleInfo() => base.Type = PacketType.MsgTotemPoleInfo;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)0);                    // 4
            writer.Write((ushort)0);                    // 6
            writer.Write(this.TotalBattlePower);        // 8
            writer.Write(this.PlayerDonation);          // 12
            writer.Write(this.SharedBattlePower);       // 16
            writer.Write((uint)this.Poles.Count);       // 20
            foreach (var pole in this.Poles)
            {
                writer.Write((uint)pole.Type);          // +0
                writer.Write(pole.BattlePower);         // +4
                writer.Write(pole.Enhancement);         // +8
                writer.Write(pole.Donation);            // +12
                writer.Write(pole.EnhancementExpiration); // +16
                writer.Write((byte)(pole.Open ? 1 : 0)); // +20
                writer.Write((byte)0);                  // +21
                writer.Write((ushort)0);                // +22
            }
            return writer.ToArray();
        }
    }
}
