namespace Comet.Game.Tournaments
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Game.World;

    [Flags]
    public enum GameEvent
    {
        None = 0,
        Qualifier = 1,
        ElitePk = 2,
        TeamPk = 4,
        GuildPk = 8,
        CaptureTheFlag = 16,
    }

    public static class EventHelpers
    {
        public static bool IsIn(Client client, GameEvent ev) =>
            (ev.HasFlag(GameEvent.Qualifier) && Arena.Qualifier.IsSignedUp(client)) ||
            (ev.HasFlag(GameEvent.ElitePk) && ElitePk.IsTakingPart(client)) ||
            (ev.HasFlag(GameEvent.TeamPk) && TeamPk.IsTakingPart(client)) ||
            (ev.HasFlag(GameEvent.GuildPk) && GuildPk.IsTakingPart(client));

        // Call outside event locks to avoid lock-order inversion across events
        public static string CheckOtherEvents(Client client, params (GameEvent Event, string Reason)[] checks)
        {
            foreach (var (ev, reason) in checks)
                if (IsIn(client, ev))
                    return reason;
            return null;
        }

        public static async Task TeleportHomeAsync(Client client, MapPoint back)
        {
            if (back == null || !await Movement.TeleportAsync(client, back.MapID, back.X, back.Y))
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
        }
    }
}
