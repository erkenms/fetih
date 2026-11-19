namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1006</remarks>
    /// <summary>
    /// Message defining character information, used to initialize the client interface
    /// and game state. Character information is loaded from the game database on login
    /// if a character exists.
    /// </summary>
    public sealed class MsgUserInfo : MsgBase<Client>
    {
        public uint CharacterID { get; set; }
        public uint Mesh { get; set; }
        public ushort Hairstyle { get; set; }
        public uint Silver { get; set; }
        public uint Jewels { get; set; }
        public ulong Experience { get; set; }
        public ushort Strength { get; set; }
        public ushort Agility { get; set; }
        public ushort Vitality { get; set; }
        public ushort Spirit { get; set; }
        public ushort AttributePoints { get; set; }
        public ushort HealthPoints { get; set; }
        public ushort ManaPoints { get; set; }
        public ushort KillPoints { get; set; }
        public byte Level { get; set; }
        public byte CurrentClass { get; set; }
        public byte PreviousClass { get; set; }
        public byte Rebirths { get; set; }
        public byte AncestorClass { get; set; }
        public uint QuizPoints { get; set; }
        public ushort EnlightenPoints { get; set; }
        public string CharacterName { get; set; }
        public string SpouseName { get; set; }

        /// <summary>
        /// Instantiates a new instance of <see cref="MsgUserInfo"/> using data fetched
        /// from the database and stored in <see cref="DbCharacter"/>.
        /// </summary>
        /// <param name="character">Character info from the database</param>
        public MsgUserInfo(DbCharacter character)
        {
            base.Type = PacketType.MsgUserInfo;
            this.CharacterID = character.CharacterID;
            this.Mesh = (uint)(character.Mesh + (character.Avatar * 10000));
            this.Hairstyle = character.Hairstyle;
            this.Silver = character.Silver;
            this.Jewels = character.Jewels;
            this.Experience = character.Experience;
            this.Strength = character.Strength;
            this.Agility = character.Agility;
            this.Vitality = character.Vitality;
            this.Spirit = character.Spirit;
            this.AttributePoints = character.AttributePoints;
            this.HealthPoints = character.HealthPoints;
            this.ManaPoints = character.ManaPoints;
            this.KillPoints = character.KillPoints;
            this.Level = character.Level;
            this.CurrentClass = character.CurrentClass;
            this.PreviousClass = character.PreviousClass;
            this.AncestorClass = character.AncestorClass;
            this.Rebirths = character.Rebirths;
            this.EnlightenPoints = character.EnlightenPoints;
            this.QuizPoints = character.QuizPoints;
            this.CharacterName = character.Name;
            this.SpouseName = "None";
        }

        /// <summary>
        /// Encodes the packet structure defined by this message class into a byte packet
        /// that can be sent to the client. Invoked automatically by the client's send 
        /// method. Encodes using byte ordering rules interoperable with the game client.
        /// </summary>
        /// <remarks>
        /// 5619: confirmed in VM, except the fields marked "guess" (every offset below is
        /// one the client reads, and the string list at 110; the reference gave what the
        /// fields mean). Guessed fields are written where an older or newer layout
        /// suggests; new characters have zero in all of them, so the VM test can't show
        /// whether they're right.
        /// </remarks>
        /// <returns>Returns a byte packet of the encoded packet.</returns>
        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.CharacterID);         // 4
            writer.Write((ushort)0);                // 8  appearance type (default)
            writer.Write(this.Mesh);                // 10
            writer.Write(this.Hairstyle);           // 14
            writer.Write(this.Silver);              // 16
            writer.Write(this.Jewels);              // 20
            writer.Write(this.Experience);          // 24
            writer.Write(new byte[20]);             // 32 deed, medals, virtue, mete level
            writer.Write(this.Strength);            // 52
            writer.Write(this.Agility);             // 54
            writer.Write(this.Vitality);            // 56
            writer.Write(this.Spirit);              // 58
            writer.Write(this.AttributePoints);     // 60
            writer.Write(this.HealthPoints);        // 62
            writer.Write(this.ManaPoints);          // 64
            writer.Write(this.KillPoints);          // 66
            writer.Write(this.Level);               // 68
            writer.Write(this.CurrentClass);        // 69
            writer.Write(this.PreviousClass);       // 70 guess
            writer.Write(this.AncestorClass);       // 71 guess
            writer.Write((byte)0);                  // 72 nobility rank
            writer.Write(this.Rebirths);            // 73
            writer.Write((byte)0);                  // 74
            writer.Write(this.QuizPoints);          // 75
            writer.Write(this.EnlightenPoints);     // 79
            writer.Write(new byte[6]);              // 81
            writer.Write((uint)0);                  // 87
            writer.Write((ushort)0);                // 91 title
            writer.Write((uint)0);                  // 93 bound CPs
            writer.Write((byte)0);                  // 97
            writer.Write((uint)0);                  // 98 sub-professions
            writer.Write((uint)0);                  // 102
            writer.Write((uint)0);                  // 106
            writer.Write(new List<string>{          // 110
                this.CharacterName,
                string.Empty,
                this.SpouseName
            });
            return writer.ToArray();
        }
    }
}