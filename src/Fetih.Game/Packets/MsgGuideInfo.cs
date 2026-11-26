namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Mentor and apprentice details and shared battle power (5619 client's CMsgGuideInfo, 2066):
    // mode u32 at 4, identity u32 at 8, targetID u32 at 12, mesh u32 at 16, sharedBP u32 at 20,
    // betrayHour u32 at 24 (999999 when not betraying), enrolDate u32 at 28 (YYYYMMDD),
    // level u8 at 32, profession u8 at 33, pkPoints i16 at 34, syndicateID u32 at 36,
    // syndicateRank u32 at 40, syndicateTitle u16 at 44, sex i32 at 52, online u8 at 56,
    // experience u64 at 64, blessing u16 at 72, composition u16 at 74, packed strings at 76
    // (minimum 80 bytes when empty).
    public sealed class MsgGuideInfo : MsgBase<Client>
    {
        public const int MinSize = 80;
        public const uint NotBetraying = 999999;

        public GuideInfoMode Mode { get; set; }
        public uint Identity { get; set; }
        public uint TargetID { get; set; }
        public uint Mesh { get; set; }
        public uint SharedBattlePower { get; set; }
        public uint BetrayHour { get; set; } = NotBetraying;
        public uint EnrolDate { get; set; }
        public byte Level { get; set; }
        public byte Profession { get; set; }
        public short PkPoints { get; set; }
        public uint SyndicateID { get; set; }
        public uint SyndicateRank { get; set; }
        public ushort SyndicateTitle { get; set; }
        public int Sex { get; set; }
        public bool Online { get; set; }
        public ulong Experience { get; set; }
        public ushort Blessing { get; set; }
        public ushort Composition { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public MsgGuideInfo() => base.Type = PacketType.MsgGuideInfo;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Mode = (GuideInfoMode)reader.ReadUInt32();          // 4
            this.Identity = reader.ReadUInt32();                     // 8
            if (bytes.Length < 76)
                return;
            this.TargetID = reader.ReadUInt32();                     // 12
            this.Mesh = reader.ReadUInt32();                         // 16
            this.SharedBattlePower = reader.ReadUInt32();            // 20
            this.BetrayHour = reader.ReadUInt32();                   // 24
            this.EnrolDate = reader.ReadUInt32();                    // 28
            this.Level = reader.ReadByte();                          // 32
            this.Profession = reader.ReadByte();                     // 33
            this.PkPoints = reader.ReadInt16();                      // 34
            this.SyndicateID = reader.ReadUInt32();                  // 36
            this.SyndicateRank = reader.ReadUInt32();                // 40
            this.SyndicateTitle = reader.ReadUInt16();               // 44
            reader.ReadBytes(6);                                     // 46..51
            this.Sex = reader.ReadInt32();                           // 52
            this.Online = reader.ReadByte() != 0;                    // 56
            reader.ReadBytes(7);                                     // 57..63
            this.Experience = reader.ReadUInt64();                   // 64
            this.Blessing = reader.ReadUInt16();                     // 72
            this.Composition = reader.ReadUInt16();                  // 74
            if (bytes.Length > 76)
                this.Strings = reader.ReadStrings();                 // 76
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Mode);                           // 4
            writer.Write(this.Identity);                             // 8
            writer.Write(this.TargetID);                             // 12
            writer.Write(this.Mesh);                                 // 16
            writer.Write(this.SharedBattlePower);                    // 20
            writer.Write(this.BetrayHour);                           // 24
            writer.Write(this.EnrolDate);                            // 28
            writer.Write(this.Level);                                // 32
            writer.Write(this.Profession);                           // 33
            writer.Write(this.PkPoints);                             // 34
            writer.Write(this.SyndicateID);                          // 36
            writer.Write(this.SyndicateRank);                        // 40
            writer.Write(this.SyndicateTitle);                       // 44
            writer.Write(new byte[6]);                               // 46..51
            writer.Write(this.Sex);                                  // 52
            writer.Write((byte)(this.Online ? 1 : 0));               // 56
            writer.Write(new byte[7]);                               // 57..63
            writer.Write(this.Experience);                           // 64
            writer.Write(this.Blessing);                             // 72
            writer.Write(this.Composition);                          // 74
            if (this.Strings == null || this.Strings.Count == 0)
                writer.Write(0u);                                    // 76 (padding to 80 bytes)
            else
                writer.Write(this.Strings);                          // 76
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Mentors.HandleGuideInfoAsync(client, this);

        public enum GuideInfoMode : uint
        {
            Mentor = 1,
            Apprentice = 2,
            SecondGenApprentice = 4,
            BattlePower = 6,
        }
    }
}
