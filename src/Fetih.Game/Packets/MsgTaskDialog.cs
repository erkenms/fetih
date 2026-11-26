namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Npcs;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // NPC dialogs: the server sends a dialog's lines, options and face, then Show; the
    // client answers with the chosen option's index (255 when the dialog is closed)
    public sealed class MsgTaskDialog : MsgBase<Client>
    {
        public const byte NoOption = 255;

        public uint TaskID { get; set; }
        public ushort Data { get; set; }
        public byte Option { get; set; }
        public DialogAction Action { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public MsgTaskDialog() => base.Type = PacketType.MsgTaskDialog;

        public MsgTaskDialog(DialogAction action, byte option, string text = null) : this()
        {
            this.Action = action;
            this.Option = option;
            if (text != null)
                this.Strings.Add(text);
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.TaskID = reader.ReadUInt32();
            this.Data = reader.ReadUInt16();
            this.Option = reader.ReadByte();
            this.Action = (DialogAction)reader.ReadByte();
            this.Strings = reader.ReadStrings();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.TaskID);              // 4
            writer.Write(this.Data);                // 8
            writer.Write(this.Option);              // 10
            writer.Write((byte)this.Action);        // 11
            writer.Write(this.Strings);             // 12
            writer.Write(new byte[3]);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client)
        {
            if (this.Action == DialogAction.Answer)
                return Dialogs.AnswerAsync(client, this.Option, this.Strings.Count > 0 ? this.Strings[0] : null);
            LogEvents.For<MsgTaskDialog>(LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes)", this.Type, this.Action, this.Length);
            return Task.CompletedTask;
        }

        public enum DialogAction : byte
        {
            Text = 1,
            Option = 2,
            Input = 3,
            Face = 4,
            Show = 100,
            Answer = 101,
        }
    }
}
