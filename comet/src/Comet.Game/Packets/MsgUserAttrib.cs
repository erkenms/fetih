namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public sealed class MsgUserAttrib : MsgBase<Client>
    {
        public uint Identity { get; set; }
        public List<(AttributeType Type, ulong Value, ulong High)> Attributes { get; } =
            new List<(AttributeType, ulong, ulong)>();

        public MsgUserAttrib(uint identity)
        {
            base.Type = PacketType.MsgUserAttrib;
            this.Identity = identity;
        }

        public MsgUserAttrib(uint identity, AttributeType type, ulong value, ulong high = 0) : this(identity)
        {
            this.Append(type, value, high);
        }

        public MsgUserAttrib Append(AttributeType type, ulong value, ulong high = 0)
        {
            this.Attributes.Add((type, value, high));
            return this;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Identity);                    // 4
            writer.Write((uint)this.Attributes.Count);      // 8
            // 24 bytes each: the type, the value, then 12 more bytes: status bits 64 to 159
            // for attribute 25, three uints for 49
            foreach (var (type, value, high) in this.Attributes)  // 12
            {
                writer.Write((uint)type);
                writer.Write(value);
                writer.Write(high);
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
            Rebirths = 22,
            Status = 25,
            Hairstyle = 26,
            XpCircle = 27,
            Jewels = 29,
            VipLevel = 39,
            QuizPoints = 40,
            EnlightenPoints = 41,
        }
    }
}
