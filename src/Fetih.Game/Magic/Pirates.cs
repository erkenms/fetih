namespace Fetih.Game.Magic
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;

    // Black Spot marks players and monsters for a while. Eagle Eye on a marked target, or
    // taking damage at Adrenaline Rush's percent, ends Eagle Eye's cooldown: the client does
    // that on Adrenaline Rush's effect from its own character.
    public static class Pirates
    {
        public const ushort EagleEye = 11030, BlackSpot = 11120, AdrenalineRush = 11130, Windstorm = 11140;

        private static readonly ConcurrentDictionary<uint, DateTime> Spotted = new ConcurrentDictionary<uint, DateTime>();

        public static bool IsSpotted(uint id, DateTime now)
        {
            if (!Spotted.TryGetValue(id, out var until))
                return false;
            if (until > now)
                return true;
            Spotted.TryRemove(new System.Collections.Generic.KeyValuePair<uint, DateTime>(id, until));
            return false;
        }

        public static DateTime Spot(uint id, DateTime until) =>
            Spotted.AddOrUpdate(id, until, (_, was) => was > until ? was : until);

        // What the caster's level of a learned spell does
        public static bool TryGetLearned(Client client, ushort magicType, out MagicType spell)
        {
            spell = null;
            return client.Skills.TryGetSpell(magicType, out var learned) &&
                Kernel.Magics.TryGet(magicType, learned.Level, out spell);
        }

        // After the caster's damage to a target: Black Spot at its percent, with its weapon in hand
        public static async Task AfterDamageAsync(Client client, uint targetID, ushort x, ushort y, DateTime now)
        {
            if (!TryGetLearned(client, BlackSpot, out var spell) ||
                spell.WeaponSubtype != 0 && !Casting.Holds(client, spell.WeaponSubtype) ||
                Random.Shared.Next(100) >= spell.Percent)
                return;
            await SpotAsync(client, spell, targetID, x, y, now);
        }

        // The mark shows over the target (a spell effect would show a hit of 0), and goes when
        // it ends unless spotted again meanwhile; those around the caster see both
        public static async Task SpotAsync(Client client, MagicType spell, uint targetID, ushort x, ushort y, DateTime now)
        {
            var until = Spot(targetID, now + TimeSpan.FromSeconds(spell.StepSeconds));
            await ShowMarkAsync(client, new MsgDeadMark(false, targetID));
            _ = Task.Delay(until - now).ContinueWith(_ => Kernel.Post(client, () =>
                client.Character == null || IsSpotted(targetID, DateTime.UtcNow)
                    ? Task.CompletedTask
                    : ShowMarkAsync(client, new MsgDeadMark(true, targetID))), TaskScheduler.Default);
            LogEvents.For(typeof(Pirates), LogEvents.StatusStarted).Information(
                "{Caster} put a black spot on {TargetId} for {Seconds}s", client.Character?.Name, targetID, spell.StepSeconds);
        }

        private static async Task ShowMarkAsync(Client client, MsgDeadMark mark)
        {
            await client.SendAsync(mark);
            await Movement.SendToViewAsync(client, mark);
        }

        // On the player's queue, after they took damage
        public static async Task OnDamagedAsync(Client client)
        {
            if (TryGetLearned(client, AdrenalineRush, out var spell) && Random.Shared.Next(100) < spell.Percent)
                await RushAsync(client, "damaged");
        }

        // Ends the caster's Eagle Eye cooldown, if they learned Adrenaline Rush
        public static async Task RushAsync(Client client, string reason)
        {
            var character = client.Character;
            if (character == null || !TryGetLearned(client, AdrenalineRush, out var spell))
                return;
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, AdrenalineRush) { MagicLevel = spell.Level };
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            LogEvents.For(typeof(Pirates), LogEvents.MagicCast).Debug(
                "{Name}'s Adrenaline Rush ended the Eagle Eye cooldown ({Reason})", character.Name, reason);
        }
    }
}
