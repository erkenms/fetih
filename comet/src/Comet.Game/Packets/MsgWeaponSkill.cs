namespace Comet.Game.Packets
{
    using Comet.Game.States;
    using Comet.Network.Packets;

    // A weapon proficiency, sent on login and when it changes. 20 bytes:
    // 4 weapon type (item type / 1000), 8 level, 12 experience, 16 experience the level needs.
    public sealed class MsgWeaponSkill : MsgBase<Client>
    {
        public uint WeaponType { get; set; }
        public uint Level { get; set; }
        public uint Experience { get; set; }
        public uint Needed { get; set; }

        public MsgWeaponSkill(uint weaponType, uint level, uint experience, uint needed)
        {
            base.Type = PacketType.MsgWeaponSkill;
            this.WeaponType = weaponType;
            this.Level = level;
            this.Experience = experience;
            this.Needed = needed;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.WeaponType);
            writer.Write(this.Level);
            writer.Write(this.Experience);
            writer.Write(this.Needed);
            return writer.ToArray();
        }
    }
}
