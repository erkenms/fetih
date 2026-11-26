namespace Fetih.Game.Magic
{
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

    // Weapon proficiency: the damage a weapon deals to monsters is its experience.
    // On the player's own queue.
    public static class Proficiency
    {
        public static ushort WeaponType(uint itemType) => (ushort)(itemType / 1000);

        public static MsgWeaponSkill Packet(ushort weaponType, SkillLevel proficiency) =>
            new MsgWeaponSkill(weaponType, proficiency.Level, proficiency.Experience,
                Kernel.Proficiencies.Needed(proficiency.Level));

        // Both weapons in hand learn; a shield or arrows don't
        public static async Task GainAsync(Client client, uint amount)
        {
            var right = client.Equipment?[ItemPosition.RightHand];
            var left = client.Equipment?[ItemPosition.LeftHand];
            if (right != null)
                await GainAsync(client, WeaponType(right.TypeID), amount, "hit");
            if (left != null && ItemSlots.IsOneHanded(left.TypeID) &&
                (right == null || WeaponType(left.TypeID) != WeaponType(right.TypeID)))
                await GainAsync(client, WeaponType(left.TypeID), amount, "hit");
        }

        public static async Task GainAsync(Client client, ushort weaponType, uint amount, string reason)
        {
            if (amount == 0 || client.Character == null)
                return;
            client.Skills.TryGetProficiency(weaponType, out var current);
            current ??= new SkillLevel(0, 0);
            var (level, experience) = Kernel.Proficiencies.Gain(current.Level, current.Experience, amount);
            if (level == current.Level && experience == current.Experience)
                return;
            await SetAsync(client, weaponType, new SkillLevel(level, experience), current.Level, reason);
        }

        public static async Task SetAsync(Client client, ushort weaponType, SkillLevel proficiency,
            ushort fromLevel, string reason)
        {
            client.Skills.SetProficiency(weaponType, proficiency);
            await client.SendAsync(Packet(weaponType, proficiency));
            if (proficiency.Level != fromLevel)
                LogEvents.For(typeof(Proficiency), LogEvents.ProficiencyLevel).Information(
                    "{Name} weapon proficiency {WeaponType} went from level {FromLevel} to {Level} ({Reason})",
                    client.Character.Name, weaponType, fromLevel, proficiency.Level, reason);
        }

        public static async Task SendAllAsync(Client client)
        {
            foreach (var (type, proficiency) in client.Skills.AllProficiencies())
                await client.SendAsync(Packet(type, proficiency));
        }
    }
}
