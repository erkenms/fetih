namespace Comet.Game.Items
{
    // Where an item type can be worn, from its type ID's thousands (ID / 1000)
    public static class ItemSlots
    {
        private static uint Sort(uint type) => type / 1000;

        public static bool IsOneHanded(uint type) =>
            Sort(type) is >= 410 and < 500 or >= 601 and <= 612;

        public static bool IsTwoHanded(uint type) => Sort(type) is >= 500 and < 600;

        public static bool IsBow(uint type) => Sort(type) == 500;

        public static bool IsArrow(uint type) => Sort(type) == 1050;

        public static bool IsShield(uint type) => Sort(type) == 900;

        public static bool Fits(uint type, ItemPosition position) => position switch
        {
            ItemPosition.Headwear => Sort(type) is >= 111 and <= 118 or 123 or >= 141 and <= 145,
            ItemPosition.Necklace => Sort(type) is 120 or 121,
            ItemPosition.Armor => Sort(type) is >= 130 and <= 139,
            ItemPosition.RightHand => IsOneHanded(type) || IsTwoHanded(type),
            ItemPosition.LeftHand => IsOneHanded(type) || IsShield(type) || IsArrow(type),
            ItemPosition.Ring => Sort(type) is >= 150 and <= 152,
            ItemPosition.Boots => Sort(type) == 160,
            ItemPosition.Garment => Sort(type) is >= 181 and <= 194,
            _ => false,
        };
    }
}
