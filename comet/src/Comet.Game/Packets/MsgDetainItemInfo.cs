namespace Comet.Game.Packets
{
    using System;
    using System.Text;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Detained equipment entry (112 bytes), sent to the owner (DetainPage) and the killer
    // (ClaimPage for the equipment, ReadyToClaim for the CP ransom after redemption).
    public sealed class MsgDetainItemInfo : MsgBase<Client>
    {
        public const int Size = 112;

        public uint DetainID { get; set; }
        public uint ItemID { get; set; }
        public uint TypeID { get; set; }
        public ushort Amount { get; set; }
        public ushort AmountLimit { get; set; }
        public DetainMode Mode { get; set; }
        public byte Position { get; set; }
        public uint SocketProgress { get; set; }
        public byte Gem1 { get; set; }
        public byte Gem2 { get; set; }
        public uint Effect { get; set; }
        public byte Plus { get; set; }
        public byte Bless { get; set; }
        public byte Bound { get; set; }
        public byte MonopolyPre { get; set; }
        public byte Enchant { get; set; }
        public uint PlusProgress { get; set; }
        public ushort Suspicious { get; set; }
        public ushort Locked { get; set; }
        public ushort Color { get; set; }
        public uint TargetID { get; set; }
        public string TargetName { get; set; }
        public uint HunterID { get; set; }
        public string HunterName { get; set; }
        public uint DetainDate { get; set; }
        public int Ransom { get; set; }
        public uint Cost { get; set; }
        public uint Days { get; set; }

        public MsgDetainItemInfo()
        {
            base.Type = PacketType.MsgDetainItemInfo;
        }

        public static MsgDetainItemInfo FromItem(
            DbDetainedItem entry, DbItem item, DetainMode mode, uint days, int ransom = 0)
        {
            var local = Clock.Local(entry.DetainedAt);
            return new MsgDetainItemInfo
            {
                DetainID = entry.DetainID,
                ItemID = item?.ItemID ?? entry.DetainID,
                TypeID = item?.TypeID ?? 0,
                Amount = item?.Amount ?? 0,
                AmountLimit = item?.AmountLimit ?? 0,
                Mode = mode,
                Position = item?.Position ?? 0,
                Gem1 = item?.Gem1 ?? 0,
                Gem2 = item?.Gem2 ?? 0,
                Plus = item?.Plus ?? 0,
                Bless = item?.Bless ?? 0,
                Enchant = item?.Enchant ?? 0,
                PlusProgress = item?.PlusProgress ?? 0,
                Locked = (ushort)(item != null && item.Locked ? 1 : 0),
                Color = item?.Color ?? 0,
                TargetID = entry.TargetID,
                TargetName = entry.TargetName ?? "",
                HunterID = entry.HunterID,
                HunterName = entry.HunterName ?? "",
                DetainDate = (uint)(local.Year * 10000 + local.Month * 100 + local.Day),
                Ransom = ransom,
                Cost = entry.Cost,
                Days = days,
            };
        }

        public override byte[] Encode()
        {
            var body = new byte[Size];
            var span = body.AsSpan();
            BitConverter.TryWriteBytes(span[4..], this.DetainID);
            BitConverter.TryWriteBytes(span[8..], this.ItemID);
            BitConverter.TryWriteBytes(span[12..], this.TypeID);
            BitConverter.TryWriteBytes(span[16..], this.Amount);
            BitConverter.TryWriteBytes(span[18..], this.AmountLimit);
            body[20] = (byte)this.Mode;
            body[21] = this.Position;
            BitConverter.TryWriteBytes(span[24..], this.SocketProgress);
            body[28] = this.Gem1;
            body[29] = this.Gem2;
            BitConverter.TryWriteBytes(span[32..], this.Effect);
            body[36] = this.Plus;
            body[37] = this.Bless;
            body[38] = this.Bound;
            body[39] = this.MonopolyPre;
            body[40] = this.Enchant;
            BitConverter.TryWriteBytes(span[44..], this.PlusProgress);
            BitConverter.TryWriteBytes(span[48..], this.Suspicious);
            BitConverter.TryWriteBytes(span[50..], this.Locked);
            BitConverter.TryWriteBytes(span[52..], this.Color);
            BitConverter.TryWriteBytes(span[56..], this.TargetID);
            WriteFixedString(span.Slice(60, 16), this.TargetName);
            BitConverter.TryWriteBytes(span[76..], this.HunterID);
            WriteFixedString(span.Slice(80, 16), this.HunterName);
            BitConverter.TryWriteBytes(span[96..], this.DetainDate);
            BitConverter.TryWriteBytes(span[100..], this.Ransom);
            BitConverter.TryWriteBytes(span[104..], this.Cost);
            BitConverter.TryWriteBytes(span[108..], this.Days);

            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(body, 4, Size - 4);
            return writer.ToArray();
        }

        private static void WriteFixedString(Span<byte> dest, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            var bytes = Encoding.ASCII.GetBytes(value);
            int len = Math.Min(bytes.Length, dest.Length - 1);
            bytes.AsSpan(0, len).CopyTo(dest);
        }

        public enum DetainMode : byte
        {
            DetainPage = 0,
            ClaimPage = 1,
            ReadyToClaim = 2,
        }
    }
}
