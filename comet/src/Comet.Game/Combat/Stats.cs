namespace Comet.Game.Combat
{
    using Comet.Game.Items;
    using Comet.Game.States;

    // A player's fighting numbers: strength adds to attack, worn items add their own. Arrows
    // are ammunition and add nothing. Plus levels and gems aren't counted yet.
    public sealed record Stats(int MinAttack, int MaxAttack, int Defense, int Dodge, int Range,
        int AttackSpeed, bool Bow, int MagicAttack = 0, int MagicDefense = 0)
    {
        public const int UnarmedSpeed = 1000;

        public static Stats Of(Client client)
        {
            var character = client.Character;
            int min = character.Strength, max = character.Strength, defense = 0, dodge = 0;
            int magicAttack = 0, magicDefense = 0;
            if (client.Equipment != null)
            {
                foreach (var item in client.Equipment.Items)
                {
                    if (ItemSlots.IsArrow(item.TypeID) || !Kernel.ItemTypes.TryGet(item.TypeID, out var type))
                        continue;
                    min += type.MinAttack;
                    max += type.MaxAttack;
                    defense += type.Defense;
                    dodge += type.Dodge;
                    magicAttack += type.MagicAttack;
                    magicDefense += type.MagicDefense;
                }
            }

            var weapon = client.Equipment?[ItemPosition.RightHand];
            ItemType weaponType = null;
            if (weapon != null)
                Kernel.ItemTypes.TryGet(weapon.TypeID, out weaponType);
            int range = weaponType == null ? 1 : System.Math.Max(1, (int)weaponType.AttackRange);
            int speed = weaponType == null || weaponType.AttackSpeed == 0 ? UnarmedSpeed : weaponType.AttackSpeed;
            bool bow = weapon != null && ItemSlots.IsBow(weapon.TypeID);
            return new Stats(min, max, defense, dodge, range, speed, bow, magicAttack, magicDefense);
        }
    }
}
