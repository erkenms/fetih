namespace Fetih.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;

    // Digging in a mine with a pick axe in the right hand, from level 15 (help250). The
    // client sends MsgAction 99 on a right click and plays the digging itself; others in view
    // get the action to play it too. Every few seconds a dig may turn up an ore (iron, copper,
    // silver or gold, grades 1 to 10, the high grades rarer) or, rarely, a gem. Digging stops
    // once the player moves, leaves, dies or puts the pick away, or the inventory is full.
    // Mining costs no health or mana. Runs on the player's own queue.
    public static class Mining
    {
        public const int MinLevel = 15;
        public const uint PickAxe = 562000;

        public static TimeSpan DigInterval { get; set; } = TimeSpan.FromSeconds(3);

        // Percent of digs that find anything, and of finds that are a gem
        public const int FindPercent = 30, GemPercent = 2;

        public const uint IronOre = 1072010, CopperOre = 1072020, SilverOre = 1072040, GoldOre = 1072050;

        private static readonly (uint Ore, int Weight)[] Ores =
            { (IronOre, 60), (CopperOre, 25), (SilverOre, 10), (GoldOre, 5) };

        public sealed record Dig(uint MapID, ushort X, ushort Y, DateTime NextAt);

        public static bool IsPick(uint typeID) => typeID / 1000 == PickAxe / 1000;

        public static async Task<bool> StartAsync(Client client, MsgAction msg)
        {
            var character = client.Character;
            if (character == null)
                return false;
            if (client.Life.Dead)
                return Reject(client, "dead");
            if (!Kernel.MapTypes.Of(character.MapID).HasFlag(MapFlags.MineField))
            {
                await Talk.NoticeAsync(client, "There is nothing to mine here.");
                return Reject(client, "not_a_mine");
            }
            if (!HasPick(client))
            {
                await Talk.NoticeAsync(client, "You need a pick axe in your right hand to mine.");
                return Reject(client, "no_pick");
            }
            if (character.Level < MinLevel)
            {
                await Talk.NoticeAsync(client, $"You can mine from level {MinLevel}.");
                return Reject(client, "level");
            }
            if (client.Transformation != null)
                return Reject(client, "transformed");

            client.Mining = new Dig(character.MapID, character.X, character.Y, DateTime.UtcNow + DigInterval);
            msg.CharacterID = client.ID;
            await Movement.SendToViewAsync(client, msg);
            LogEvents.For(typeof(Mining), LogEvents.MiningStarted).Information(
                "{Name} started mining on map {MapId} at {X},{Y}", character.Name, character.MapID, character.X, character.Y);
            return true;
        }

        public static async Task TickAsync(Client client, DateTime now, Random random)
        {
            var dig = client.Mining;
            var character = client.Character;
            if (dig == null || character == null)
                return;
            if (client.Life.Dead || !HasPick(client) || character.MapID != dig.MapID ||
                character.X != dig.X || character.Y != dig.Y)
            {
                client.Mining = null;
                return;
            }
            if (now < dig.NextAt)
                return;
            client.Mining = dig with { NextAt = now + DigInterval };

            if (client.Inventory == null || client.Inventory.FreeSlots == 0)
            {
                client.Mining = null;
                await Talk.NoticeAsync(client, "Your inventory is full. You stop mining.");
                return;
            }
            if (Find(random) is not uint found || !Kernel.ItemTypes.TryGet(found, out var type))
                return;
            if (await ItemActions.GiveAsync(client, type, "mining") == null)
                return;
            LogEvents.For(typeof(Mining), LogEvents.MiningFound).Information(
                "{Name} mined {ItemName} (type {TypeId}) on map {MapId}", character.Name, type.Name, type.ID, dig.MapID);
        }

        // The item type a dig turns up, or null
        public static uint? Find(Random random)
        {
            if (random.Next(100) >= FindPercent)
                return null;
            if (random.Next(100) < GemPercent)
                return 700001 + 10 * (uint)random.Next(Gems.Tortoise + 1);

            int pick = random.Next(100), total = 0;
            uint ore = IronOre;
            foreach (var (type, weight) in Ores)
                if (pick < (total += weight))
                {
                    ore = type;
                    break;
                }
            return ore + Grade(random) - 1;
        }

        // 1 to 10; grade g is drawn 11 - g times in 55
        public static uint Grade(Random random)
        {
            int pick = random.Next(55);
            for (uint grade = 1; ; grade++)
            {
                pick -= (int)(11 - grade);
                if (pick < 0)
                    return grade;
            }
        }

        private static bool HasPick(Client client) =>
            client.Equipment?[ItemPosition.RightHand] is { } weapon && IsPick(weapon.TypeID);

        private static bool Reject(Client client, string reason)
        {
            LogEvents.For(typeof(Mining), LogEvents.MiningRejected).Warning(
                "{Name} can't mine: {Reason}", client.Character?.Name, reason);
            return false;
        }
    }
}
