namespace Comet.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;

    // In memory only: a login starts on foot. Riding is read by other players' queues
    // (MsgPlayer); the rest only on the player's own.
    public sealed class Mount
    {
        private volatile bool riding;

        public bool Riding
        {
            get => this.riding;
            set => this.riding = value;
        }

        public uint Vigor { get; set; }
        public DateTime MovedAt { get; set; }
    }

    // Steeds, as Albetros (5517) and the 5808 source have them. Casting Riding (7001) with a
    // steed worn mounts it or gets off; status bit 50 shows the rider on the steed MsgPlayer
    // carries. Attacking or casting gets the rider off, unless they wear a riding crop, which
    // also adds its dexterity to the vigor. Mounting fills the vigor. Ride steps cost 2,
    // walking steps 1, jumps 3 per 4 cells; a move the vigor can't pay for ends the ride. The
    // vigor comes back while the steed stands still: 2 + its plus every half second (Albetros).
    public static class Riding
    {
        public const ushort MagicType = 7001;
        public const uint RideStepCost = 2, WalkStepCost = 1;

        public static TimeSpan RestTime { get; set; } = TimeSpan.FromMilliseconds(1500);

        public static DbItem Steed(Client client) => client.Equipment?[ItemPosition.Steed];

        public static DbItem Crop(Client client) => client.Equipment?[ItemPosition.Crop];

        public static bool FightsMounted(Client client) => Crop(client) != null;

        // The steed type's dexterity plus what its plus adds (itemadd.ini), plus the crop's
        public static uint MaxVigor(DbItem steed, DbItem crop)
        {
            if (steed == null)
                return 0;
            uint vigor = Dexterity(steed) + (Kernel.ItemAdditions.Find(steed.TypeID, steed.Plus)?.Dexterity ?? 0u);
            return crop == null ? vigor : vigor + Dexterity(crop);
        }

        public static uint MaxVigor(Client client) => MaxVigor(Steed(client), Crop(client));

        private static uint Dexterity(DbItem item) =>
            Kernel.ItemTypes.TryGet(item.TypeID, out var type) ? type.Dexterity : 0u;

        public static Task DismountToFightAsync(Client client, string reason) =>
            FightsMounted(client) ? Task.CompletedTask : DismountAsync(client, reason);

        // After the crop is put on or taken off: vigor above the new maximum comes down to it
        public static async Task FollowCropAsync(Client client)
        {
            var mount = client.Mount;
            uint max = MaxVigor(client);
            if (!mount.Riding || mount.Vigor <= max)
                return;
            mount.Vigor = max;
            await client.SendAsync(MsgData.Vigor(mount.Vigor));
        }

        public static uint JumpCost(int distance) => (uint)(distance * 3 / 4);

        // Why the player can't ride here, or null
        public static string Problem(Client client)
        {
            var steed = Steed(client);
            return steed == null ? "no_steed" :
                steed.Plus < Kernel.MapTypes.RidePlusOf(client.Character.MapID) ? "lineage_level" :
                null;
        }

        public static async Task MountAsync(Client client)
        {
            var mount = client.Mount;
            mount.Vigor = MaxVigor(client);
            mount.Riding = true;
            await client.SendAsync(MsgData.Vigor(mount.Vigor));
            await Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Riding), LogEvents.MountStarted).Information(
                "{Name} mounted their steed with {Vigor} vigor on map {MapId}",
                client.Character.Name, mount.Vigor, client.Character.MapID);
        }

        public static async Task DismountAsync(Client client, string reason)
        {
            if (!client.Mount.Riding)
                return;
            client.Mount.Riding = false;
            await Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Riding), LogEvents.MountEnded).Information(
                "{Name} got off their steed ({Reason})", client.Character.Name, reason);
        }

        public static async Task SpendAsync(Client client, uint cost, DateTime now)
        {
            var mount = client.Mount;
            mount.MovedAt = now;
            if (!mount.Riding || cost == 0)
                return;
            if (cost > mount.Vigor)
            {
                await DismountAsync(client, "no_vigor");
                return;
            }
            mount.Vigor -= cost;
            await client.SendAsync(MsgData.Vigor(mount.Vigor));
        }

        // Each second, from Recovery
        public static async Task RecoverAsync(Client client, DateTime now)
        {
            var mount = client.Mount;
            var steed = Steed(client);
            if (!mount.Riding || steed == null || now - mount.MovedAt < RestTime)
                return;
            uint max = MaxVigor(steed, Crop(client));
            if (mount.Vigor >= max)
                return;
            mount.Vigor = Math.Min(max, mount.Vigor + 2 * (2u + steed.Plus));
            await client.SendAsync(MsgData.Vigor(mount.Vigor));
        }
    }
}
