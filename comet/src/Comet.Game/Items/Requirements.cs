namespace Comet.Game.Items
{
    using Comet.Game.Database.Models;

    public static class Requirements
    {
        // Why the character can't wear the type, or null
        public static string Unmet(DbCharacter character, ItemType type)
        {
            if (character.Level < type.RequiredLevel)
                return "level";
            if (type.RequiredGender != 0 && type.RequiredGender != Gender(character.Mesh))
                return "gender";
            if (type.RequiredProfession != 0 && !ProfessionFits(character.CurrentClass, type.RequiredProfession))
                return "profession";
            if (character.Strength < type.RequiredStrength || character.Agility < type.RequiredAgility ||
                character.Vitality < type.RequiredVitality || character.Spirit < type.RequiredSpirit)
                return "attributes";
            return null;
        }

        // 1 for male bodies (1003, 1004), 2 for female ones (2001, 2002)
        public static int Gender(uint mesh) => (int)(mesh % 10000 / 1000);

        // Classes are a line (tens) and a rank (ones); taoists are every class from 100 up.
        // The class must be of the required line and at least the required rank.
        public static bool ProfessionFits(byte currentClass, byte required)
        {
            int Line(int c) => c >= 100 ? 10 : c / 10;
            if (Line(currentClass) != Line(required))
                return false;
            return currentClass >= 100 || currentClass % 10 >= required % 10;
        }
    }
}
