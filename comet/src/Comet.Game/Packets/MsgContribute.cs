namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Mentor reward totals, apprentice contribution list, and reward claims (5619 client's
    // CMsgContribute, 2067): action u32 at 4, generation u32 at 16 (0 first-gen, 1 second-gen),
    // experience u64 at 24 (level-up time, 600 per EXP ball), blessing u16 at 32 (hours),
    // composition u16 at 34 (points, 100 per +1 Stone), packed strings at 36 (minimum 40 bytes).
    public sealed class MsgContribute : MsgBase<Client>
    {
        public const int MinSize = 40;

        public ContributeAction Action { get; set; }
        public uint Generation { get; set; }
        public ulong Experience { get; set; }
        public ushort Blessing { get; set; }
        public ushort Composition { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public MsgContribute() => base.Type = PacketType.MsgContribute;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (ContributeAction)reader.ReadUInt32();     // 4
            if (bytes.Length < 36)
                return;
            reader.ReadBytes(8);                                     // 8..15
            this.Generation = reader.ReadUInt32();                   // 16
            reader.ReadUInt32();                                     // 20
            this.Experience = reader.ReadUInt64();                   // 24
            this.Blessing = reader.ReadUInt16();                     // 32
            this.Composition = reader.ReadUInt16();                  // 34
            if (bytes.Length > 36)
                this.Strings = reader.ReadStrings();                 // 36
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);                         // 4
            writer.Write(0UL);                                       // 8..15
            writer.Write(this.Generation);                           // 16
            writer.Write(0u);                                        // 20
            writer.Write(this.Experience);                           // 24
            writer.Write(this.Blessing);                             // 32
            writer.Write(this.Composition);                          // 34
            if (this.Strings == null || this.Strings.Count == 0)
                writer.Write(0u);                                    // 36 (padding to 40 bytes)
            else
                writer.Write(this.Strings);                          // 36
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Mentors.HandleContributeAsync(client, this);

        public enum ContributeAction : uint
        {
            // Client -> Server claims
            ClaimExperience = 1,
            ClaimExperiencePart = 2,
            ClaimBlessing = 3,
            ClaimComposition = 4,

            // Bidirectional: C->S query, S->C totals / entries
            Rewards = 5,
            Apprentices = 6,

            // Server -> Client: reset list before sending entries, then refresh UI
            ResetCurrent = 7,
            Refresh = 8,
        }
    }
}
