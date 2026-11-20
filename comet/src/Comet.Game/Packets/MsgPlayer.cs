namespace Comet.Game.Packets
{
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
        public uint Helmet, Garment, Armor, RightHand, LeftHand;
        public ushort ArmorColor, ShieldColor, HelmetColor;
        public ulong Status { get; set; }
        public uint MonsterHealth { get; set; }
        public ushort MonsterLevel { get; set; }

        public MsgPlayer(Client client) : this(client.Character, client.Equipment)
        {
            this.Mesh = client.Life.Mesh(client.Character);
            this.Status = client.Status;
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
            this.Name = character.Name;
        }

        // Fields other than look, position, level and name stay zero until their features exist.
        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Mesh);                // 4
            writer.Write(this.CharacterID);         // 8
            writer.Write(0u);                       // 12  guild ID
            writer.Write(0u);                       // 16  guild rank
            writer.Write((ushort)0);                // 20
            writer.Write(this.Status);              // 22  status flags
            writer.Write(new byte[12]);             // 30
            writer.Write((ushort)0);                // 42  appearance type
            writer.Write(this.Helmet);              // 44  item types worn
            writer.Write(this.Garment);             // 48
            writer.Write(this.Armor);               // 52
            writer.Write(this.RightHand);           // 56
            writer.Write(this.LeftHand);            // 60
            writer.Write(new byte[16]);             // 64  accessories, mount and mount armor
            writer.Write((ushort)0);                // 80
            writer.Write((ushort)0);                // 82
            writer.Write(this.MonsterHealth);       // 84  monster health
            writer.Write((ushort)0);                // 88
            writer.Write(this.MonsterLevel);        // 90  monster level
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
            writer.Write((byte)0);                  // 110 away
            writer.Write(new byte[20]);             // 111 flowers, nobility, ...
            writer.Write(this.ArmorColor);          // 131
            writer.Write(this.ShieldColor);         // 133
            writer.Write(this.HelmetColor);         // 135
            writer.Write(0u);                       // 137 quiz points
            writer.Write((ushort)0);                // 141 mount plus
            writer.Write(0u);                       // 143
            writer.Write(0u);                       // 147
            writer.Write((ushort)0);                // 151 enlighten points
            writer.Write((ushort)0);                // 153 merit points
            writer.Write((ushort)0);                // 155
            writer.Write((ushort)0);                // 157
            writer.Write(0u);                       // 159
            writer.Write(0u);                       // 163 clan ID
            writer.Write(0u);                       // 167 clan rank
            writer.Write(0u);                       // 171
            writer.Write((ushort)0);                // 175 title
            writer.Write(0u);                       // 177
            writer.Write((byte)0);                  // 181
            writer.Write(0u);                       // 182
            writer.Write(new byte[3]);              // 186
            writer.Write((byte)0);                  // 189 boss
            writer.Write(new byte[16]);             // 190 helmet, armor, weapon art
            writer.Write((byte)0);                  // 206 sub-profession
            writer.Write(0u);                       // 207 sub-profession levels
            writer.Write(0u);                       // 211
            writer.Write((ushort)0);                // 215 first class
            writer.Write((ushort)0);                // 217 second class
            writer.Write(this.CurrentClass);        // 219 class
            writer.Write(0u);                       // 221
            writer.Write((byte)0);                  // 225 must be 0 for the client to
                                                    //     place the player at X, Y
            writer.Write(new List<string> {         // 226
                this.Name,
                string.Empty,
                string.Empty                        // clan name
            });
            return writer.ToArray();
        }
    }
}
