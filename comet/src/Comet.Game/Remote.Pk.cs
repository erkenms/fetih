namespace Comet.Game
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Pk;
    using Comet.Game.World;
    using Comet.Shared.Models.Admin;

    // Live PK state and jail release for the web's PK and jail overview
    public partial class Remote : IGamePk
    {
        public Task<PkLiveSnapshot> PkSnapshot()
        {
            var now = DateTime.UtcNow;
            var snapshot = new PkLiveSnapshot { TakenAt = now };

            foreach (var player in Kernel.Players.Values)
            {
                var character = player.Character;
                if (character == null)
                    continue;

                uint mapId = player.Map?.Id ?? character.MapID;
                ushort x = character.X, y = character.Y;
                if (player.Map != null && player.Map.TryGetPosition(player, out var liveX, out var liveY))
                    (x, y) = (liveX, liveY);

                bool flashing = player.Pk.IsFlashing(now);
                ushort pkPoints = character.KillPoints;
                string color = NameColor(player, now);

                if (flashing || pkPoints >= Pking.RedNamePoints)
                {
                    snapshot.Active.Add(new ActivePkPlayer
                    {
                        CharacterID = character.CharacterID,
                        AccountID = character.AccountID,
                        Name = character.Name,
                        Level = character.Level,
                        Class = character.CurrentClass,
                        Rebirths = character.Rebirths,
                        MapID = mapId,
                        X = x,
                        Y = y,
                        PkPoints = pkPoints,
                        Flashing = flashing,
                        NameColor = color,
                        PkMode = player.Pk.Mode.ToString(),
                        InJail = mapId == Pk.Jail.MapID,
                        Dead = player.Life.Dead,
                    });
                }

                if (mapId == Pk.Jail.MapID)
                {
                    snapshot.Jailed.Add(new JailedOnlinePlayer
                    {
                        CharacterID = character.CharacterID,
                        AccountID = character.AccountID,
                        Name = character.Name,
                        Level = character.Level,
                        Class = character.CurrentClass,
                        X = x,
                        Y = y,
                        PkPoints = pkPoints,
                        Flashing = flashing,
                        NameColor = color,
                        LoggedInAt = player.LoggedInAt,
                    });
                }
            }

            snapshot.Active = snapshot.Active
                .OrderByDescending(p => p.PkPoints)
                .ThenByDescending(p => p.Flashing)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            snapshot.Jailed = snapshot.Jailed
                .OrderByDescending(p => p.PkPoints)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult(snapshot);
        }

        public async Task<AdminResult> ReleaseFromJail(CharacterArgs args)
        {
            var result = await OnCharacterAsync(args.CharacterID,
                async client =>
                {
                    uint mapId = client.Map?.Id ?? client.Character.MapID;
                    if (mapId != Pk.Jail.MapID)
                        return AdminErrors.Invalid;
                    if (!await Pk.Jail.ReleaseAsync(client, Reason))
                        return AdminErrors.BadDestination;
                    await Talk.NoticeAsync(client, "You have been released from jail.");
                    return null;
                },
                async saved =>
                {
                    if (saved.MapID != Pk.Jail.MapID)
                        return AdminErrors.Invalid;
                    if (!Kernel.Maps.Exists(StartPosition.MapID))
                        return AdminErrors.BadDestination;
                    (saved.MapID, saved.X, saved.Y) = (StartPosition.MapID, StartPosition.X, StartPosition.Y);
                    await CharactersRepository.SaveAsync(saved);
                    return null;
                });
            return Logged(LogEvents.GmRelease, args, args.CharacterID, "", result);
        }
    }
}
