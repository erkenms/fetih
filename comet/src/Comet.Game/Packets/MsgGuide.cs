namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Mentor/apprentice actions and prompts (5619 client's CMsgGuide, 2065):
    // action u32 at 4, identity u32 at 8, param u32 at 12, value u32 at 16,
    // packed strings at 20 (minimum 24 bytes when empty).
    public sealed class MsgGuide : MsgBase<Client>
    {
        public const int MinSize = 24;

        public GuideAction Action { get; set; }
        public uint Identity { get; set; }
        public uint Param { get; set; }
        public uint Value { get; set; }
        public string Name { get; set; }

        public MsgGuide() => base.Type = PacketType.MsgGuide;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (GuideAction)reader.ReadUInt32();
            this.Identity = reader.ReadUInt32();
            this.Param = reader.ReadUInt32();
            this.Value = reader.ReadUInt32();
            if (bytes.Length > 20)
            {
                var strings = reader.ReadStrings();
                this.Name = strings.Count > 0 ? strings[0] : string.Empty;
            }
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);    // 4
            writer.Write(this.Identity);        // 8
            writer.Write(this.Param);           // 12
            writer.Write(this.Value);           // 16
            if (string.IsNullOrEmpty(this.Name))
                writer.Write(0u);               // 20 (empty string packer + padding to 24 bytes)
            else
                writer.Write(new List<string> { this.Name }); // 20
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Mentors.HandleGuideAsync(client, this);

        public enum GuideAction : uint
        {
            // Client -> Server
            InviteApprentice = 1,
            RequestMentor = 2,
            LeaveMentor = 3,
            ExpelApprentice = 4,
            AcceptReInvite = 5,
            AcceptReFollow = 6,

            // Server -> Client (7 triggers apprentice list query; 8 and 9 are also C->S replies)
            RefreshApprentices = 7,
            AcceptInviteApprentice = 8,
            AcceptRequestMentor = 9,
            TargetHasMentor = 10,
            AlreadyHasMentor = 11,
            ReInvitePrompt = 12,
            TargetBetrayCooldown = 13,
            SelfBetrayCooldown = 14,
            AlreadyApprentice = 15,
            Remove = 16,
            SetBetrayHours = 17,
            ClearBattlePower = 18,
            ClearMentor = 19,
            RemoveSecondGen = 20,
            ForceRemove = 21,
            MentorOffline = 22,
            ApprenticeOffline = 23,

            // Client -> Server: auto-refuse when PK-protected
            AutoRefuse = 24,
        }
    }
}
