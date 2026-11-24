namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    public sealed class MsgPlayer : MsgBase<Client>
    {
        public uint Mesh { get; set; }
        public uint CharacterID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Hairstyle { get; set; }
        public byte Direction { get; set; }
        public uint Stance { get; set; }
        public byte Rebirths { get; set; }
        public ushort Level { get; set; }
        public ushort CurrentClass { get; set; }
        public string Name { get; set; }
        public bool Away { get; set; }
        public uint Helmet, Garment, Armor, RightHand, LeftHand;
        public ushort ArmorColor, ShieldColor, HelmetColor;
        public uint Steed;
        public byte SteedPlus;
        public UInt128 Status { get; set; }
        public uint MonsterHealth { get; set; }
        public ushort MonsterLevel { get; set; }
        public uint GuildID { get; set; }
        public ushort GuildRank { get; set; }
        public uint ClanID { get; set; }
        public ushort ClanRank { get; set; }
        public string ClanName { get; set; } = string.Empty;
        public uint FlowerCharm { get; set; }
        public Nobility.NobilityRank Nobility { get; set; }
        public uint QuizPoints { get; set; }
        public ushort EnlightenPoints { get; set; }
        public bool Boss { get; set; }
        public ushort FirstClass { get; set; }
        public ushort SecondClass { get; set; }

        public MsgPlayer(Client client) : this(client.Character, client.Equipment)
        {
            this.Mesh = client.Life.Mesh(client.Character);
            this.Status = client.Status;
            this.Away = client.Away;
            if (Social.Guilds.Of(client.ID) is Social.Guilds.Membership guild)
            {
                this.GuildID = guild.Guild.ID;
                this.GuildRank = (ushort)guild.Member.Rank;
            }
            if (Social.Clans.Of(client.ID) is Social.Clans.Membership clan)
            {
                this.ClanID = clan.Clan.ID;
                this.ClanRank = (ushort)clan.Member.Rank;
                this.ClanName = clan.Clan.Name;
            }
        }

        public MsgPlayer(Monster monster)
        {
            base.Type = PacketType.MsgPlayer;
            this.Mesh = monster.Type.Lookface;
            this.CharacterID = monster.Identity;
            this.X = monster.X;
            this.Y = monster.Y;
            this.Direction = monster.Direction;
            this.MonsterHealth = monster.Health;
            this.MonsterLevel = monster.Type.Level;
            this.Name = monster.Type.Name;
            this.Boss = monster.Type.Boss;
        }

        public MsgPlayer(Character character, Equipment equipment) : this(character)
        {
            if (equipment == null)
                return;
            this.Helmet = equipment.TypeAt(ItemPosition.Headwear);
            this.Garment = equipment.TypeAt(ItemPosition.Garment);
            this.Armor = equipment.TypeAt(ItemPosition.Armor);
            this.RightHand = equipment.TypeAt(ItemPosition.RightHand);
            this.LeftHand = equipment.TypeAt(ItemPosition.LeftHand);
            this.ArmorColor = equipment.ColorAt(ItemPosition.Armor);
            this.ShieldColor = equipment.ColorAt(ItemPosition.LeftHand);
            this.HelmetColor = equipment.ColorAt(ItemPosition.Headwear);
            this.Steed = equipment.TypeAt(ItemPosition.Steed);
            this.SteedPlus = equipment[ItemPosition.Steed]?.Plus ?? 0;
        }

        public MsgPlayer(Character character)
        {
            base.Type = PacketType.MsgPlayer;
            this.Mesh = (uint)(character.Mesh + (character.Avatar * 10000));
            this.CharacterID = character.CharacterID;
            this.X = character.X;
            this.Y = character.Y;
            this.Hairstyle = character.Hairstyle;
            this.Direction = character.Direction;
            this.Stance = character.Stance;
            this.Rebirths = character.Rebirths;
            this.Level = character.Level;
            this.CurrentClass = character.CurrentClass;
            this.FirstClass = character.AncestorClass;
            this.SecondClass = character.PreviousClass;
            this.QuizPoints = character.QuizPoints;
            this.EnlightenPoints = character.EnlightenPoints;
            this.Name = character.Name;
            this.FlowerCharm = Flowers.FlowerRanking.CharmOf(character.CharacterID);
            this.Nobility = Comet.Game.Nobility.Peerage.RankOf(character.CharacterID);
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Mesh);                // 4
            writer.Write(this.CharacterID);         // 8
            writer.Write(this.GuildID);             // 12
            writer.Write((uint)this.GuildRank);     // 16
            writer.Write((ushort)0);                // 20
            writer.Write((ulong)this.Status);       // 22  status bits 0-63
            writer.Write((ulong)(this.Status >> 64)); // 30  64-127
            writer.Write(0u);                       // 38  128-159
            writer.Write((ushort)0);                // 42  appearance type
            writer.Write(this.Helmet);              // 44  item types worn
            writer.Write(this.Garment);             // 48
            writer.Write(this.Armor);               // 52
            writer.Write(this.RightHand);           // 56
            writer.Write(this.LeftHand);            // 60
            writer.Write(new byte[8]);              // 64  accessories
            writer.Write(this.Steed);               // 72  steed type (+4 as helmet)
            writer.Write(0u);                       // 76
            writer.Write((ushort)0);                // 80
            writer.Write((ushort)0);                // 82
            writer.Write(this.MonsterHealth);       // 84
            writer.Write((ushort)0);                // 88
            writer.Write(this.MonsterLevel);        // 90
            writer.Write(this.X);                   // 92
            writer.Write(this.Y);                   // 94
            writer.Write(this.Hairstyle);           // 96
            writer.Write(this.Direction);           // 98
            writer.Write(this.Stance);              // 99  action (stand, sit, emotes)
            writer.Write((ushort)0);                // 103
            writer.Write((byte)0);                  // 105
            writer.Write(this.Rebirths);            // 106
            writer.Write(this.Level);               // 107
            writer.Write((byte)0);                  // 109 equipment viewable
            writer.Write((byte)(this.Away ? 1 : 0)); // 110
            writer.Write(new byte[12]);             // 111 tutor battle effect, team info
            writer.Write(this.FlowerCharm);         // 123 flower charm (Dynarank.ini ID)
            writer.Write((uint)this.Nobility);      // 127 nobility rank
            writer.Write(this.ArmorColor);          // 131
            writer.Write(this.ShieldColor);         // 133
            writer.Write(this.HelmetColor);         // 135
            writer.Write(this.QuizPoints);          // 137
            writer.Write(this.SteedPlus);           // 141
            writer.Write((byte)0);                  // 142
            writer.Write(0u);                       // 143
            writer.Write(0u);                       // 147 steed color
            writer.Write(this.EnlightenPoints);     // 151
            writer.Write((ushort)0);                // 153 merit points
            writer.Write((ushort)0);                // 155
            writer.Write((ushort)0);                // 157
            writer.Write(0u);                       // 159
            writer.Write(this.ClanID);              // 163
            writer.Write((uint)this.ClanRank);      // 167
            writer.Write(0u);                       // 171
            writer.Write((ushort)0);                // 175 title
            writer.Write(0u);                       // 177
            writer.Write((byte)0);                  // 181
            writer.Write(0u);                       // 182
            writer.Write(new byte[3]);              // 186
            writer.Write((byte)(this.Boss ? 1 : 0)); // 189
            writer.Write(new byte[16]);             // 190 helmet, armor, weapon art
            writer.Write((byte)0);                  // 206 sub-profession
            writer.Write(0u);                       // 207 sub-profession levels
            writer.Write(0u);                       // 211
            writer.Write(this.FirstClass);          // 215
            writer.Write(this.SecondClass);         // 217
            writer.Write(this.CurrentClass);        // 219
            writer.Write(0u);                       // 221
            writer.Write((byte)0);                  // 225 must be 0 for the client to
                                                    //     place the player at X, Y
            writer.Write(new List<string> {         // 226
                this.Name,
                string.Empty,
                this.ClanName
            });
            return writer.ToArray();
        }
    }
}
