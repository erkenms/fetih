namespace Comet.Game.Magic
{
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    // OldLevel: a spell's level before a rebirth put it back to 0, which it regains quickly
    public sealed record SkillLevel(ushort Level, uint Experience, ushort OldLevel = 0);

    public sealed record SkillsSnapshot(List<(ushort Type, SkillLevel Level)> Spells,
        List<(ushort Type, SkillLevel Level)> Proficiencies, List<ushort> Forgotten);

    // A character's spells by magic type and weapon proficiencies by item type / 1000.
    // Changed on the player's queue and read by saves, so only under the lock.
    public sealed class Skills
    {
        private readonly object Sync = new object();
        private readonly Dictionary<ushort, SkillLevel> Spells = new Dictionary<ushort, SkillLevel>();
        private readonly Dictionary<ushort, SkillLevel> Proficiencies = new Dictionary<ushort, SkillLevel>();

        // Spells forgotten since loading, for saves to delete
        private readonly HashSet<ushort> Forgotten = new HashSet<ushort>();

        public Skills() { }

        public Skills(IEnumerable<DbMagic> spells, IEnumerable<DbProficiency> proficiencies)
        {
            foreach (var spell in spells)
                this.Spells[spell.Type] = new SkillLevel(spell.Level, spell.Experience, spell.OldLevel);
            foreach (var proficiency in proficiencies)
                this.Proficiencies[proficiency.Type] = new SkillLevel(proficiency.Level, proficiency.Experience);
        }

        public bool TryGetSpell(ushort type, out SkillLevel spell)
        {
            lock (this.Sync)
                return this.Spells.TryGetValue(type, out spell);
        }

        public void SetSpell(ushort type, SkillLevel spell)
        {
            lock (this.Sync)
            {
                this.Spells[type] = spell;
                this.Forgotten.Remove(type);
            }
        }

        public bool ForgetSpell(ushort type)
        {
            lock (this.Sync)
            {
                if (!this.Spells.Remove(type))
                    return false;
                this.Forgotten.Add(type);
                return true;
            }
        }

        public bool TryGetProficiency(ushort type, out SkillLevel proficiency)
        {
            lock (this.Sync)
                return this.Proficiencies.TryGetValue(type, out proficiency);
        }

        public void SetProficiency(ushort type, SkillLevel proficiency)
        {
            lock (this.Sync)
                this.Proficiencies[type] = proficiency;
        }

        public List<(ushort Type, SkillLevel Level)> AllSpells()
        {
            lock (this.Sync)
                return this.Spells.Select(x => (x.Key, x.Value)).OrderBy(x => x.Key).ToList();
        }

        public List<(ushort Type, SkillLevel Level)> AllProficiencies()
        {
            lock (this.Sync)
                return this.Proficiencies.Select(x => (x.Key, x.Value)).OrderBy(x => x.Key).ToList();
        }

        public SkillsSnapshot Snapshot()
        {
            lock (this.Sync)
                return new SkillsSnapshot(this.AllSpells(), this.AllProficiencies(), this.Forgotten.ToList());
        }
    }
}
