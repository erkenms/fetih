namespace Comet.Client
{
    using System.Linq;
    using System.Text;

    public static class MagicFiles
    {
        // One level of a spell as the client's magictype.dat describes it
        public sealed record Spell(ushort Type, ushort Level, ushort Sort, string Name)
        {
            public int Power { get; init; }
            public ushort Mana { get; init; }
            public uint Range { get; init; }
            public uint Distance { get; init; }
            public uint StepSeconds { get; init; }
            public uint NeedExperience { get; init; }
            public bool XpSkill { get; init; }
            public byte Stamina { get; init; }
            public uint WeaponSubtype { get; init; }
        }

        // An encrypted magictype.dat as the 5619 client has it: per spell level one line of 50
        // fields, each ending in "@@" (field numbers as in TQ's cq_magictype, with one more at 19)
        public static byte[] MagicTypeDat(params Spell[] spells)
        {
            var text = new StringBuilder();
            int id = 1;
            foreach (var spell in spells)
            {
                var fields = Enumerable.Repeat("0", 50).ToArray();
                fields[0] = (id++).ToString();
                fields[1] = spell.Type.ToString();
                fields[2] = spell.Sort.ToString();
                fields[3] = spell.Name.Replace(' ', '~');
                fields[8] = spell.Level.ToString();
                fields[9] = spell.Mana.ToString();
                fields[10] = spell.Power.ToString();
                fields[12] = "100";
                fields[13] = spell.StepSeconds.ToString();
                fields[14] = spell.Range.ToString();
                fields[15] = spell.Distance.ToString();
                fields[16] = "-1";
                fields[18] = spell.NeedExperience.ToString();
                fields[21] = spell.XpSkill ? "1" : spell.Stamina > 0 ? "2" : "0";
                fields[22] = spell.WeaponSubtype.ToString();
                fields[29] = spell.Stamina.ToString();
                text.Append(string.Join("@@", fields)).Append("@@\r\n");
            }
            return ItemFiles.Encrypt(Encoding.Latin1.GetBytes(text.ToString()));
        }

        // WeaponSkillLevelExp.ini: per line a level, the experience to reach it, and a number
        // the server doesn't use
        public static byte[] WeaponSkillLevelExpIni(params uint[] toReach) =>
            Encoding.ASCII.GetBytes(string.Concat(toReach.Select((exp, i) => $"{i + 1},{exp},0\r\n")));
    }
}
