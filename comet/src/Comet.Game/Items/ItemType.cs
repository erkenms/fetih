namespace Comet.Game.Items
{
    public sealed record ItemType
    {
        public uint ID { get; init; }
        public string Name { get; init; }
        public byte RequiredProfession { get; init; }
        public byte RequiredWeaponSkill { get; init; }
        public byte RequiredLevel { get; init; }
        public byte RequiredGender { get; init; }
        public ushort RequiredStrength { get; init; }
        public ushort RequiredAgility { get; init; }
        public ushort RequiredVitality { get; init; }
        public ushort RequiredSpirit { get; init; }
        public byte Monopoly { get; init; }
        public uint Price { get; init; }
        public uint ActionID { get; init; }
        public ushort MaxAttack { get; init; }
        public ushort MinAttack { get; init; }
        public ushort Defense { get; init; }
        public ushort Dexterity { get; init; }
        public ushort Dodge { get; init; }
        public ushort Life { get; init; }
        public ushort Mana { get; init; }
        public ushort Amount { get; init; }
        public ushort AmountLimit { get; init; }
        public byte Gem1 { get; init; }
        public byte Gem2 { get; init; }
        public ushort MagicAttack { get; init; }
        public ushort MagicDefense { get; init; }
        public ushort AttackRange { get; init; }
        public ushort AttackSpeed { get; init; }
        public uint EmoneyPrice { get; init; }
        public uint StackLimit { get; init; }

        // The tooltip's kind line and description (fields 53 and 54), with spaces for '~'
        public string Kind { get; init; } = "";
        public string Description { get; init; } = "";
    }
}
