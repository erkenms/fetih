namespace Comet.Game.Packets
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Character rename window (dialog 489): 26 bytes.
    // [4] ushort Action: 0=Change (C->S), 1=Success (S->C), 2=Duplicate (S->C),
    //                    3=Query / QueryReply (C->S & S->C), 5=Close (S->C)
    // [6] ushort UsedCount, [8] ushort RemainingCount, [10..26] Latin1 name (16 bytes)
    public sealed class MsgChangeName : MsgBase<Client>
    {
        public const int Size = 26;

        public NameAction Action { get; set; }
        public ushort UsedCount { get; set; }
        public ushort RemainingCount { get; set; }
        public string Name { get; set; } = string.Empty;

        public MsgChangeName()
        {
            this.Type = PacketType.MsgChangeName;
        }

        public static MsgChangeName QueryReply(ushort used, ushort remaining) => new MsgChangeName
        {
            Action = NameAction.Query,
            UsedCount = used,
            RemainingCount = remaining,
        };

        public static MsgChangeName Success() => new MsgChangeName { Action = NameAction.Success };

        public static MsgChangeName Duplicate() => new MsgChangeName { Action = NameAction.Duplicate };

        public static MsgChangeName Close() => new MsgChangeName { Action = NameAction.Close };

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (NameAction)reader.ReadUInt16();
            this.UsedCount = reader.ReadUInt16();
            this.RemainingCount = reader.ReadUInt16();
            var name = reader.ReadBytes(16);
            int end = Array.IndexOf(name, (byte)0);
            this.Name = Encoding.Latin1.GetString(name, 0, end < 0 ? name.Length : end);
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write((ushort)this.Action);
            writer.Write(this.UsedCount);
            writer.Write(this.RemainingCount);
            writer.Write(this.Name ?? string.Empty, 16);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => this.Action switch
        {
            NameAction.Query => NameChange.QueryAsync(client),
            NameAction.Change => NameChange.ChangeAsync(client, this.Name),
            _ => Task.CompletedTask,
        };

        public enum NameAction : ushort
        {
            Change = 0,
            Success = 1,
            Duplicate = 2,
            Query = 3,
            Close = 5,
        }
    }
}
