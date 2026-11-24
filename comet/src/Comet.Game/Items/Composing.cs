namespace Comet.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // The Wuxing Oven raises gear's plus with composition points: each minor item burnt gives
    // points by its own plus, and the main item's plus goes up a level each time its points
    // reach what that level needs. Minors are +1 to +8 gear of the main item's kind, or +n
    // Stones. Composing never fails and stops at +12. The points tables are the 5619 client's,
    // which draws the progress bar from them: past +9 a level needs fewer points than +8 did.
    public static class Composing
    {
        public const byte MaxPlus = 12, MaxMinorPlus = 8;

        // Points a +n minor gives, n from 0
        private static readonly uint[] MinorPoints = { 0, 10, 40, 120, 360, 1080, 3240, 9720, 29160 };

        // Points a +n item needs for +n+1
        private static readonly uint[] LevelPoints = { 20, 20, 80, 240, 720, 2160, 6480, 19440, 58320, 2700, 5500, 9000 };

        public static uint PointsOf(byte plus) => plus < MinorPoints.Length ? MinorPoints[plus] : 0;

        public static uint PointsFor(byte plus) => plus < LevelPoints.Length ? LevelPoints[plus] : 0;

        // +1 Stone (730001) to +8 Stone (730008); +9 Stones can't be minors
        public static bool IsStone(uint typeID) => typeID is >= 730001 and <= 730009;

        public static byte PlusOf(DbItem item) => IsStone(item.TypeID) ? (byte)(item.TypeID % 10) : item.Plus;

        // The slot an item composes with; one-handed weapons and two-handed ones and bows are
        // all weapons, apart from shields
        public static ItemPosition? Kind(uint typeID) =>
            ItemSlots.IsShield(typeID) ? ItemPosition.LeftHand :
            ItemSlots.IsArrow(typeID) ? null :
            ItemSlots.Fits(typeID, ItemPosition.RightHand) ? ItemPosition.RightHand :
            ItemSlots.Fits(typeID, ItemPosition.Headwear) ? ItemPosition.Headwear :
            ItemSlots.Fits(typeID, ItemPosition.Necklace) ? ItemPosition.Necklace :
            ItemSlots.Fits(typeID, ItemPosition.Armor) ? ItemPosition.Armor :
            ItemSlots.Fits(typeID, ItemPosition.Ring) ? ItemPosition.Ring :
            ItemSlots.Fits(typeID, ItemPosition.Boots) ? ItemPosition.Boots :
            null;

        // Why the minor can't go into the main item, or null if it can
        public static string Refusal(DbItem main, DbItem minor, DateTime now)
        {
            if (Kind(main.TypeID) == null)
                return "main_not_composable";
            if (main.Plus >= MaxPlus)
                return "main_at_max";
            byte plus = PlusOf(minor);
            if (plus is < 1 or > MaxMinorPlus)
                return "minor_plus";
            if (!IsStone(minor.TypeID))
            {
                if (Kind(minor.TypeID) != Kind(main.TypeID))
                    return "minor_kind";
                if (minor.Amount == 0)
                    return "minor_broken";
            }
            if (Locking.IsLocked(minor, now))
                return "minor_locked";
            if (Social.Guilds.IsInscribed(minor.ItemID))
                return "minor_inscribed";
            return null;
        }

        // Adds the points and raises the plus as far as they reach; at the cap the rest is lost
        public static (byte Plus, uint Progress) Add(byte plus, uint progress, uint points)
        {
            progress += points;
            while (plus < MaxPlus && progress >= PointsFor(plus))
                progress -= PointsFor(plus++);
            return (plus, plus >= MaxPlus ? 0 : progress);
        }

        public static async Task<bool> ComposeAsync(Client client, uint mainID, uint minorID)
        {
            var character = client.Character;
            var main = client.Inventory?.Find(mainID);
            var minor = client.Inventory?.Find(minorID);
            if (character == null || main == null || minor == null || main == minor)
                return Reject(client, mainID, "not_in_inventory");
            if (Npcs.Dialogs.Reach(client, Npcs.WuxingOven.OvenID, "compose") == null)
            {
                await Talk.NoticeAsync(client, "Failed to compose. You are standing too far away.");
                return Reject(client, mainID, "too_far");
            }
            if (Refusal(main, minor, DateTime.UtcNow) is { } reason)
                return Reject(client, mainID, reason);

            client.Inventory.Remove(minor);
            await ItemsRepository.DeleteAsync(minor.ItemID);
            await client.SendAsync(MsgItem.Removed(minor.ItemID));

            byte before = main.Plus;
            uint points = PointsOf(PlusOf(minor));
            (main.Plus, main.PlusProgress) = Add(main.Plus, main.PlusProgress, points);
            await ItemsRepository.SetPlusAsync(main.ItemID, main.Plus, main.PlusProgress);
            await Social.Guilds.RefreshInscribedItemAsync(main);
            await client.SendAsync(new MsgItemInfo(main, MsgItemInfo.ItemAction.Update));
            await Social.Mentors.RecordCompositionAsync(client, points);
            LogEvents.For(typeof(Composing), LogEvents.ItemComposed).Information(
                "{Name} composed item {MinorId} (type {MinorType}) into item {ItemId} (type {TypeId}): " +
                "{Points} points, +{Before} to +{Plus} with {Progress}",
                character.Name, minor.ItemID, minor.TypeID, main.ItemID, main.TypeID,
                points, before, main.Plus, main.PlusProgress);
            return true;
        }

        public static Task UnsupportedAsync(Client client, MsgDataArray msg)
        {
            LogEvents.For(typeof(Composing), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                msg.Type, msg.Action, msg.Length, Convert.ToHexString(msg.Encode()));
            return Task.CompletedTask;
        }

        private static bool Reject(Client client, uint itemID, string reason)
        {
            LogEvents.For(typeof(Composing), LogEvents.ItemComposeRejected).Warning(
                "{Name} can't compose into item {ItemId}: {Reason}", client.Character?.Name, itemID, reason);
            return false;
        }
    }
}
