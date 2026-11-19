namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public sealed class MsgTalk : MsgBase<Client>
    {
        public Color Color { get; set; }
        public TalkChannel Channel { get; set; }
        public TalkStyle Style { get; set; }
        public uint CharacterID { get; set; }
        public uint RecipientMesh { get; set; }
        public string RecipientName { get; set; }
        public uint SenderMesh { get; set; }
        public string SenderName { get; set; }
        public string Suffix { get; set; }
        public string Message { get; set; }

        public MsgTalk(uint characterID, TalkChannel channel, string text)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = Color.White;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = MsgTalk.SYSTEM;
            this.RecipientName = MsgTalk.ALLUSERS;
            this.Suffix = string.Empty;
            this.Message = text;
        }

        public MsgTalk(uint characterID, TalkChannel channel, Color color, string text)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = color;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = MsgTalk.SYSTEM;
            this.RecipientName = MsgTalk.ALLUSERS;
            this.Suffix = string.Empty;
            this.Message = text;
        }

        public MsgTalk(uint characterID, TalkChannel channel, Color color, 
            string recipient, string sender, string text)
        {
            this.Type = PacketType.MsgTalk;
            this.Color = color;
            this.Channel = channel;
            this.Style = TalkStyle.Normal;
            this.CharacterID = characterID;
            this.SenderName = sender;
            this.RecipientName = recipient;
            this.Suffix = string.Empty;
            this.Message = text;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Color = Color.FromArgb(reader.ReadInt32());
            this.Channel = (TalkChannel)reader.ReadUInt16();
            this.Style = (TalkStyle)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();
            this.RecipientMesh = reader.ReadUInt32();
            this.SenderMesh = reader.ReadUInt32();

            var strings = reader.ReadStrings();
            if (strings.Count > 3)
            {
                this.SenderName = strings[0];
                this.RecipientName = strings[1];
                this.Suffix = strings[2];
                this.Message = strings[3];
            }
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((int)Color.FromArgb(0, this.Color).ToArgb());
            writer.Write((ushort)this.Channel);
            writer.Write((ushort)this.Style);
            writer.Write(this.CharacterID);
            writer.Write(this.RecipientMesh);
            writer.Write(this.SenderMesh);
            writer.Write(new List<string> 
            {
                this.SenderName,
                this.RecipientName,
                this.Suffix,
                this.Message
            });
            return writer.ToArray();
        }

        public const string SYSTEM = "SYSTEM";
        public const string ALLUSERS = "ALLUSERS";
        public readonly static MsgTalk LoginOk = new MsgTalk(0, TalkChannel.Login, "ANSWER_OK");
        public readonly static MsgTalk LoginInvalid = new MsgTalk(0, TalkChannel.Login, "Invalid login");
        public readonly static MsgTalk LoginNewRole = new MsgTalk(0, TalkChannel.Login, "NEW_ROLE");
        public readonly static MsgTalk RegisterOk = new MsgTalk(0, TalkChannel.Register, "ANSWER_OK");
        public readonly static MsgTalk RegisterInvalid = new MsgTalk(0, TalkChannel.Register, "Invalid character");
        public readonly static MsgTalk RegisterInvalidName = new MsgTalk(0, TalkChannel.Register, "Invalid character name");
        public readonly static MsgTalk RegisterNameTaken = new MsgTalk(0, TalkChannel.Register, "Character name taken");
        public readonly static MsgTalk RegisterTryAgain = new MsgTalk(0, TalkChannel.Register, "Error, please try later");
        
        public enum TalkChannel : ushort
        {
            Talk = 2000,
            Whisper,
            Action,
            Team,
            Guild,
            Spouse = 2006,
            System,
            Yell,
            Friend,
            Center = 2011,
            TopLeft,
            Ghost,
            Service,
            Tip,
            World = 2021,
            Register = 2100,
            Login,
            Shop,
            Vendor = 2104,
            Website,
            GuildWarRight1 = 2108,
            GuildWarRight2,
            Offline,
            Announce,
            TradeBoard = 2201,
            FriendBoard,
            TeamBoard,
            GuildBoard,
            OthersBoard,
            Broadcast = 2500,
            Monster = 2600
        }

        [Flags]
        public enum TalkStyle : ushort 
        {
            Normal = 0,
            Scroll = 1 << 0,
            Flash  = 1 << 1,
            Blast  = 1 << 2
        }
    }
}
