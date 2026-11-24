namespace Comet.Game.States
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // Praying stones add days to it. While it lasts, as the client's tooltip says: stamina up
    // to 150, reviving where the player died, online and offline
    // training, and 20% extra EXP from monsters to claim (Training.GodExp). Changed on the
    // player's own queue.
    public static class HeavenBlessing
    {
        public const byte BlessedStamina = 150;

        // Maps where the dead go back to their reborn point anyway
        private const MapFlags NoReviveHere = MapFlags.PrisonMap | MapFlags.PkField | MapFlags.PkGame | MapFlags.GuildMap;

        // PrayingStone(S), (M) and (L), and the other stones, jades and notes that give a
        // blessing, by their itemtype descriptions
        public static TimeSpan? StoneDuration(uint typeID) => typeID switch
        {
            1200000 => TimeSpan.FromDays(3),
            1200001 => TimeSpan.FromDays(7),
            1200002 => TimeSpan.FromDays(30),
            720098 => TimeSpan.FromHours(1),      // 1hourBlessingStone
            720099 => TimeSpan.FromHours(3),      // 3hoursBlessingStone
            722576 => TimeSpan.FromHours(4),      // WishNote
            722117 => TimeSpan.FromDays(1),       // WishJade
            720357 => TimeSpan.FromDays(3),       // 3DaysBlessing
            722118 => TimeSpan.FromDays(3),       // WishDiamond
            722385 => TimeSpan.FromDays(3),       // DignityStone
            722386 => TimeSpan.FromDays(7),       // MajestyStone
            723817 => TimeSpan.FromDays(7),       // 7Days`BlessingStone
            >= 729172 and <= 729179 => TimeSpan.FromDays(7), // the NationalBlessings
            _ => null,
        };

        public static bool IsBlessed(DbCharacter character, DateTime now) => character?.HeavenBlessing > now;

        public static DateTime Extend(DateTime? until, TimeSpan by, DateTime now) =>
            (until > now ? until.Value : now) + by;

        public static uint SecondsLeft(DbCharacter character, DateTime now) =>
            character.HeavenBlessing is DateTime until && until > now
                ? (uint)Math.Ceiling((until - now).TotalSeconds)
                : 0;

        public static byte MaxStamina(Client client) =>
            IsBlessed(client.Character, DateTime.UtcNow) ? BlessedStamina : Magic.Energy.MaxStamina;

        public static bool CanReviveHere(Client client) =>
            IsBlessed(client.Character, DateTime.UtcNow) &&
            (Kernel.MapTypes.Of(client.Character.MapID) & NoReviveHere) == 0;

        public static async Task AddAsync(Client client, TimeSpan by, string reason)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            bool started = !IsBlessed(character, now);
            character.HeavenBlessing = Extend(character.HeavenBlessing, by, now);
            await SendAsync(client);
            if (started)
                await Pk.Pking.SendStatusAsync(client);
            LogEvents.For(typeof(HeavenBlessing), LogEvents.CharacterBlessed).Information(
                "{Name} is blessed for {Days} more days, until {Until:u} ({Reason})",
                character.Name, by.TotalDays, character.HeavenBlessing, reason);
        }

        // The seconds left, which the client counts down in its status window
        public static Task SendAsync(Client client) => client.SendAsync(new MsgUserAttrib(
            client.ID, AttributeType.HeavenBlessing, SecondsLeft(client.Character, DateTime.UtcNow)));

        // On the player's recovery tick: a blessing that ran out ends, and stamina above the
        // normal maximum comes down to it
        public static async Task ExpireAsync(Client client, DateTime now)
        {
            var character = client.Character;
            if (character.HeavenBlessing is not DateTime until || until > now)
                return;
            character.HeavenBlessing = null;
            await SendAsync(client);
            await Pk.Pking.SendStatusAsync(client);
            if (client.Energy.Stamina > Magic.Energy.MaxStamina)
            {
                client.Energy.Stamina = Magic.Energy.MaxStamina;
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Stamina, client.Energy.Stamina));
            }
            LogEvents.For(typeof(HeavenBlessing), LogEvents.CharacterBlessingEnded).Information(
                "{Name}'s heaven blessing ended", character.Name);
        }
    }
}
