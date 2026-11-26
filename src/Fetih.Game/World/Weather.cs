namespace Fetih.Game.World
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

    // Weather a game master set on a map, kept until changed or the server restarts.
    // The client data has no map weather, so maps have none unless set.
    public static class Weather
    {
        public static readonly IReadOnlyDictionary<string, (uint Code, uint Looks)> Kinds =
            new Dictionary<string, (uint, uint)>
            {
                ["none"] = (0, 1), ["rain"] = (1, 1), ["snow"] = (2, 1), ["leaves"] = (3, 10),
                ["sand"] = (13, 1), ["flowers"] = (14, 5), ["fireflies"] = (19, 1), ["clouds"] = (20, 1),
            };

        private static readonly ConcurrentDictionary<uint, MsgWeather> ByMap =
            new ConcurrentDictionary<uint, MsgWeather>();

        public static MsgWeather Of(uint mapID) => ByMap.TryGetValue(mapID, out var weather) ? weather : null;

        public static async Task SetAsync(uint mapID, MsgWeather weather)
        {
            if (weather.Code == 0)
                ByMap.TryRemove(mapID, out _);
            else
                ByMap[mapID] = weather;
            if (Kernel.World != null && Kernel.World.TryGet(mapID, out var map))
                foreach (var player in map.All())
                    await player.SendAsync(weather);
        }

        public static async Task SendAsync(Client client, uint mapID)
        {
            var weather = Of(mapID);
            if (weather != null)
                await client.SendAsync(weather);
        }
    }
}
