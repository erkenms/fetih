namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public sealed class MsgUserAttrib : MsgBase<Client>
    {
        public uint Identity { get; set; }
        public List<(AttributeType Type, UInt128 Value)> Attributes { get; } =
            new List<(AttributeType, UInt128)>();

        public MsgUserAttrib(uint identity)
        {
            base.Type = PacketType.MsgUserAttrib;
            this.Identity = identity;
        }

        public MsgUserAttrib(uint identity, AttributeType type, UInt128 value) : this(identity)
        {
            this.Append(type, value);
        }

        // Attribute 18 packs seconds in bits 32-63 and the EXP rate percent (e.g. 200 for 2x) in bits 96-127
        public static MsgUserAttrib DoubleExpOf(uint characterID, uint seconds, uint percent) =>
            new MsgUserAttrib(characterID, AttributeType.DoubleExp, ((UInt128)percent << 96) | ((UInt128)seconds << 32));

        public MsgUserAttrib Append(AttributeType type, UInt128 value)
        {
            this.Attributes.Add((type, value));
            return this;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Identity);                    // 4
            writer.Write((uint)this.Attributes.Count);      // 8
            // 24 bytes each: the type, the value, then three uints. Status (25) goes on with
            // its bits 64-127 in the first two; attribute 49 reads all three.
            foreach (var (type, value) in this.Attributes)  // 12
            {
                writer.Write((uint)type);
                writer.Write((ulong)value);
                writer.Write((ulong)(value >> 64));
                writer.Write(0u);
            }
            return writer.ToArray();
        }

        public enum AttributeType : uint
        {
            Health = 0,
            MaxHealth = 1,
            Mana = 2,
            MaxMana = 3,
            Silver = 4,
            Experience = 5,
            PkPoints = 6,
            Class = 7,
            Stamina = 8,
            AttributePoints = 10,
            Mesh = 11,
            Level = 12,
            Spirit = 13,
            Vitality = 14,
            Strength = 15,
            Agility = 16,
            HeavenBlessing = 17,    // seconds left
            DoubleExp = 18,         // seconds in bits 32-63, rate percent in bits 96-127
            Rebirths = 22,
            Status = 25,
            Hairstyle = 26,
            XpCircle = 27,
            LuckyTime = 28,         // milliseconds
            Jewels = 29,
            VipLevel = 39,
            QuizPoints = 40,
            EnlightenPoints = 41,
            GuildBattlePower = 44,
        }
    }
}
