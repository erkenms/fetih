namespace Fetih.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Packet 2320 (0x0910): sub-class requests and updates.
    // Client -> Server (always 29 bytes):
    //   0: u16 length (29), 2: u16 type (2320), 4: u16 action, 6: u8 sub-class ID, 7..28: zero.
    //   Actions: 0 Switch/Activate (0 = recover/deactivate), 2 Upgrade (study points),
    //            6 RequestInfo (sent when opening the sub-class window), 9 Learn, 10 Promote.
    // Server -> Client:
    //   1 Switch:  4: u16=1, 6: u8 subClass (0 = none), padded to 29 bytes.
    //   3 Upgrade: 4: u16=3, 6: u8 subClass (client increments its level by 1), padded to 29 bytes.
    //   4 Learn:   4: u16=4, 6: u8 subClass, 7: u8 phase, padded to 29 bytes.
    //   5 Promote: 4: u16=5, 6: u8 subClass, 7: u8 phase, padded to 29 bytes.
    //   7 Show:    4: u16=7, 6: u64 studyPoints, 14: u64=0, 22: u32 count,
    //              26: count * (u8 subClass, u8 phase, u8 level).
    //   8 StudyPoints: 4: u16=8, 6: u64 basePoints, 14: u64 addedPoints (client sets
    //              basePoints + addedPoints and shows "+addedPoints" when addedPoints > 0),
    //              padded to 29 bytes.
    public sealed class MsgSubPro : MsgBase<Client>
    {
        public const int BaseSize = 29;

        public enum SubProAction : ushort
        {
            RequestSwitch = 0,
            Switch = 1,
            RequestUpgrade = 2,
            Upgrade = 3,
            Learn = 4,
            Promote = 5,
            RequestInfo = 6,
            Show = 7,
            StudyPoints = 8,
            RequestLearn = 9,
            RequestPromote = 10,
        }

        public SubProAction Action { get; set; }
        public SubClassId SubClass { get; set; }
        public byte Phase { get; set; }
        public ulong StudyPoints { get; set; }
        public ulong AddedStudyPoints { get; set; }
        public List<(SubClassId Id, byte Phase, byte Level)> Entries { get; } =
            new List<(SubClassId, byte, byte)>();

        public MsgSubPro() => base.Type = PacketType.MsgSubPro;

        public static MsgSubPro Switch(SubClassId id) => new MsgSubPro
        {
            Action = SubProAction.Switch,
            SubClass = id,
        };

        public static MsgSubPro Upgrade(SubClassId id) => new MsgSubPro
        {
            Action = SubProAction.Upgrade,
            SubClass = id,
        };

        public static MsgSubPro Learn(SubClassId id, byte phase) => new MsgSubPro
        {
            Action = SubProAction.Learn,
            SubClass = id,
            Phase = phase,
        };

        public static MsgSubPro Promote(SubClassId id, byte phase) => new MsgSubPro
        {
            Action = SubProAction.Promote,
            SubClass = id,
            Phase = phase,
        };

        public static MsgSubPro StudyPointsPacket(ulong basePoints, ulong added) => new MsgSubPro
        {
            Action = SubProAction.StudyPoints,
            StudyPoints = basePoints,
            AddedStudyPoints = added,
        };

        public static MsgSubPro Show(ulong studyPoints, IEnumerable<(SubClassId Id, byte Phase, byte Level)> entries)
        {
            var msg = new MsgSubPro
            {
                Action = SubProAction.Show,
                StudyPoints = studyPoints,
            };
            msg.Entries.AddRange(entries);
            return msg;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            if (bytes.Length < 7)
                return;
            this.Action = (SubProAction)reader.ReadUInt16();
            this.SubClass = (SubClassId)reader.ReadByte();
            if (bytes.Length >= 8)
                this.Phase = reader.ReadByte();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);              // 4
            switch (this.Action)
            {
                case SubProAction.Show:
                    writer.Write(this.StudyPoints);         // 6
                    writer.Write(0UL);                      // 14
                    writer.Write((uint)this.Entries.Count); // 22
                    foreach (var (id, phase, level) in this.Entries)
                    {
                        writer.Write((byte)id);
                        writer.Write(phase);
                        writer.Write(level);
                    }
                    if (this.Entries.Count == 0)
                        writer.Write(new byte[BaseSize - 26]);
                    break;
                case SubProAction.StudyPoints:
                    writer.Write(this.StudyPoints);         // 6
                    writer.Write(this.AddedStudyPoints);    // 14
                    writer.Write(new byte[BaseSize - 22]);  // 22..28
                    break;
                case SubProAction.Learn:
                case SubProAction.Promote:
                    writer.Write((byte)this.SubClass);      // 6
                    writer.Write(this.Phase);               // 7
                    writer.Write(new byte[BaseSize - 8]);   // 8..28
                    break;
                default:
                    writer.Write((byte)this.SubClass);      // 6
                    writer.Write(new byte[BaseSize - 7]);   // 7..28
                    break;
            }
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            return this.Action switch
            {
                SubProAction.RequestInfo => SubClasses.SendAllAsync(client),
                SubProAction.RequestSwitch => SubClasses.ActivateAsync(client, this.SubClass),
                SubProAction.RequestUpgrade => SubClasses.UpgradeAsync(client, this.SubClass),
                SubProAction.RequestLearn => SubClasses.LearnAsync(client, this.SubClass),
                SubProAction.RequestPromote => SubClasses.PromoteAsync(client, this.SubClass),
                _ => Task.CompletedTask,
            };
        }
    }
}
