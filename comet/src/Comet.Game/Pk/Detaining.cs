namespace Comet.Game.Pk
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgDetainItemInfo;

    // Worn equipment detained when a red- or black-name player is killed by another player.
    // The owner can redeem it for CPs within 7 days (the CPs become a claimable ransom for the
    // killer); once the 7 days pass, the killer can claim the equipment itself.
    public static class Detaining
    {
        public static TimeSpan DetainPeriod { get; set; } = TimeSpan.FromDays(7);

        // PrizeOfficer in the Market (1036) and in Jail (6000)
        public const uint PrizeOfficerMarketID = 4450;
        public const uint PrizeOfficerJailID = 4542;

        private const byte PrizeOfficerFace = 102;
        private const byte RedeemOption = 1;
        private const byte ClaimOption = 2;

        private static readonly uint[] PlusCosts =
            { 0, 10, 20, 50, 100, 200, 400, 800, 1500, 2500, 3500, 4500, 5500 };

        public static bool IsDetainable(DbItem item) =>
            item != null && Equipment.IsEquipped(item.Position) && !ItemSlots.IsArrow(item.TypeID) &&
            !Social.Guilds.IsInscribed(item.ItemID);

        // Days elapsed for the client's detain/reward dialogs: 0..7 while redeemable, >= 8 once
        // expired (the client treats >= 8 as expired and claimable by the hunter).
        public static uint DaysElapsed(DateTime detainedAt, DateTime now)
        {
            if (now >= detainedAt + DetainPeriod)
                return Math.Max(8u, (uint)(now - detainedAt).TotalDays + 1);
            return (uint)Math.Clamp((int)(now - detainedAt).TotalDays, 0, 7);
        }

        public static bool IsExpired(DbDetainedItem entry, DateTime now) =>
            now >= entry.DetainedAt + DetainPeriod;

        public static uint RedeemCost(DbItem item)
        {
            uint cost = ItemSlots.Quality(item.TypeID) switch
            {
                9 => 50u,
                8 => 30u,
                7 => 20u,
                _ => 10u,
            };
            if (item.Plus > 0)
                cost += PlusCosts[Math.Min((int)item.Plus, PlusCosts.Length - 1)];
            if (item.Gem1 != 0)
                cost += 50;
            if (item.Gem2 != 0)
                cost += 150;
            if (item.Bless > 0)
                cost += (uint)item.Bless * 10;
            return Math.Max(10u, cost);
        }

        public static async Task OnKillAsync(Client killer, Client victim)
        {
            var victimCharacter = victim.Character;
            var killerCharacter = killer.Character;
            if (victimCharacter == null || killerCharacter == null || victim.Equipment == null)
                return;
            if (victimCharacter.KillPoints < Pking.RedNamePoints || Pking.IsFree(killerCharacter.MapID))
                return;

            var candidates = victim.Equipment.Items.Where(IsDetainable).ToList();
            if (candidates.Count == 0)
                return;

            var item = candidates[Random.Shared.Next(candidates.Count)];
            var from = (ItemPosition)item.Position;
            var now = DateTime.UtcNow;

            victim.Equipment.Clear(from);
            item.Position = (byte)ItemPosition.Detained;
            await ItemsRepository.SetPositionsAsync(new[] { (item.ItemID, item.Position) });

            await victim.SendAsync(MsgItem.Unequipped(item.ItemID, from));
            await victim.SendAsync(MsgItem.Removed(item.ItemID));
            await Equipping.ShowLookAsync(victim);
            await Equipping.FollowMaximumsAsync(victim);

            var entry = new DbDetainedItem
            {
                ItemID = item.ItemID,
                TargetID = victimCharacter.CharacterID,
                TargetName = victimCharacter.Name,
                HunterID = killerCharacter.CharacterID,
                HunterName = killerCharacter.Name,
                Cost = RedeemCost(item),
                DetainedAt = now,
                Redeemed = false,
            };
            await DetainedItemsRepository.CreateAsync(entry);

            uint days = DaysElapsed(entry.DetainedAt, now);
            await victim.SendAsync(FromItem(entry, item, DetainMode.DetainPage, days));
            await killer.SendAsync(FromItem(entry, item, DetainMode.ClaimPage, days));

            string itemName = Kernel.ItemTypes.TryGet(item.TypeID, out var type) ? type.Name : "equipment";
            await Talk.NoticeAsync(victim,
                $"Your {itemName} has been detained by {killerCharacter.Name}! You can redeem it at the PrizeOfficer in Market for {entry.Cost} CPs within 7 days.");
            await Talk.NoticeAsync(killer,
                $"You detained {victimCharacter.Name}'s {itemName}! Claim your prize at the PrizeOfficer in Market.");

            LogEvents.For(typeof(Detaining), LogEvents.PkDetained).Information(
                "{Victim} lost {ItemName} (item {ItemId}, detain {DetainId}) to {Hunter} for {Cost} CPs",
                victimCharacter.Name, itemName, item.ItemID, entry.DetainID, killerCharacter.Name, entry.Cost);
        }

        public static async Task LoginAsync(Client client)
        {
            if (client.Character == null)
                return;
            var now = DateTime.UtcNow;

            var detained = await DetainedItemsRepository.LoadForTargetAsync(client.ID);
            foreach (var (entry, item) in detained)
            {
                if (item == null)
                    continue;
                await client.SendAsync(FromItem(entry, item, DetainMode.DetainPage, DaysElapsed(entry.DetainedAt, now)));
            }

            var rewards = await DetainedItemsRepository.LoadForHunterAsync(client.ID);
            foreach (var (entry, item) in rewards)
            {
                uint days = DaysElapsed(entry.DetainedAt, now);
                if (entry.Redeemed)
                    await client.SendAsync(FromItem(entry, null, DetainMode.ReadyToClaim, days, (int)entry.Cost));
                else if (item != null)
                    await client.SendAsync(FromItem(entry, item, DetainMode.ClaimPage, days, IsExpired(entry, now) ? -1 : 0));
            }
        }

        // Sent by CDlgPKDetain::Open (page 0) and CDlgPKReward::Open (page 1) via MsgAction 153
        public static async Task QueryAsync(Client client, uint page)
        {
            if (client.Character == null)
                return;
            var now = DateTime.UtcNow;
            var update = new MsgDetainItemUpdate(page);
            if (page == MsgDetainItemUpdate.DetainPage)
            {
                var detained = await DetainedItemsRepository.LoadForTargetAsync(client.ID);
                foreach (var (entry, _) in detained)
                    update.Entries.Add(new MsgDetainItemUpdate.Entry(entry.DetainID, DaysElapsed(entry.DetainedAt, now), Keep: true));
            }
            else if (page == MsgDetainItemUpdate.RewardPage)
            {
                var rewards = await DetainedItemsRepository.LoadForHunterAsync(client.ID);
                foreach (var (entry, _) in rewards)
                    update.Entries.Add(new MsgDetainItemUpdate.Entry(entry.DetainID, DaysElapsed(entry.DetainedAt, now), Keep: true));
            }
            await client.SendAsync(update);
        }

        // Owner redeems detained equipment for CPs (MsgItem 32)
        public static async Task RedeemAsync(Client client, uint detainID)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null)
                return;

            var (entry, item) = await DetainedItemsRepository.FindAsync(detainID);
            var now = DateTime.UtcNow;
            if (entry == null || entry.TargetID != client.ID || entry.Redeemed || item == null)
            {
                Reject(client, "redeem", detainID, "not_found");
                await Talk.NoticeAsync(client, "Failed to redeem.");
                return;
            }
            if (IsExpired(entry, now))
            {
                Reject(client, "redeem", detainID, "expired");
                await Talk.NoticeAsync(client, "Failed. It's expired.");
                return;
            }
            if (client.Inventory.FreeSlots == 0)
            {
                Reject(client, "redeem", detainID, "inventory_full");
                await Talk.NoticeAsync(client, "No space in your inventory.");
                return;
            }
            if (character.Jewels < entry.Cost)
            {
                Reject(client, "redeem", detainID, "not_enough_cps");
                await Talk.NoticeAsync(client, "Not enough CPs.");
                return;
            }

            if (!await DetainedItemsRepository.MarkRedeemedAsync(detainID))
            {
                Reject(client, "redeem", detainID, "already_redeemed");
                return;
            }

            if (!await Progress.ChangeConquerPointsAsync(client, -(long)entry.Cost, "redeem_detained"))
            {
                Reject(client, "redeem", detainID, "not_enough_cps");
                return;
            }

            item.Position = (byte)ItemPosition.Inventory;
            await ItemsRepository.SetPositionsAsync(new[] { (item.ItemID, item.Position) });
            client.Inventory.Add(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            await client.SendAsync(MsgDetainItemUpdate.Remove(MsgDetainItemUpdate.DetainPage, detainID));
            await Talk.NoticeAsync(client, "Equipment successfully redeemed!");

            if (Kernel.Players.TryGet(entry.HunterID, out var hunter))
            {
                entry.Redeemed = true;
                entry.ItemID = 0;
                uint days = DaysElapsed(entry.DetainedAt, now);
                await hunter.SendAsync(MsgDetainItemUpdate.Remove(MsgDetainItemUpdate.RewardPage, detainID));
                await hunter.SendAsync(FromItem(entry, null, DetainMode.ReadyToClaim, days, (int)entry.Cost));
                await Talk.NoticeAsync(hunter,
                    $"{character.Name} redeemed their detained equipment! Claim your {entry.Cost} CPs at the PrizeOfficer in Market.");
            }

            LogEvents.For(typeof(Detaining), LogEvents.PkRedeemed).Information(
                "{Name} redeemed item {ItemId} (detain {DetainId}) for {Cost} CPs",
                character.Name, item.ItemID, detainID, entry.Cost);
        }

        // Hunter claims either CP ransom (if redeemed) or expired equipment (MsgItem 33)
        public static async Task ClaimAsync(Client client, uint detainID)
        {
            var character = client.Character;
            if (character == null || client.Inventory == null)
                return;

            var (entry, item) = await DetainedItemsRepository.FindAsync(detainID);
            var now = DateTime.UtcNow;
            if (entry == null || entry.HunterID != client.ID)
            {
                Reject(client, "claim", detainID, "not_found");
                return;
            }

            if (entry.Redeemed)
            {
                if (!await DetainedItemsRepository.DeleteAsync(detainID))
                {
                    Reject(client, "claim", detainID, "already_claimed");
                    return;
                }
                await Progress.ChangeConquerPointsAsync(client, entry.Cost, "detain_ransom");
                await client.SendAsync(MsgDetainItemUpdate.Remove(MsgDetainItemUpdate.RewardPage, detainID));
                await Talk.NoticeAsync(client, "Ransom successfully claimed!");

                LogEvents.For(typeof(Detaining), LogEvents.PkClaimed).Information(
                    "{Name} claimed {Cost} CPs ransom (detain {DetainId}) from {Target}",
                    character.Name, entry.Cost, detainID, entry.TargetName);
                return;
            }

            if (!IsExpired(entry, now))
            {
                Reject(client, "claim", detainID, "not_expired");
                await Talk.NoticeAsync(client, "Failed. It's neither expired nor been redeemed.");
                return;
            }
            if (item == null)
            {
                await DetainedItemsRepository.DeleteAsync(detainID);
                await client.SendAsync(MsgDetainItemUpdate.Remove(MsgDetainItemUpdate.RewardPage, detainID));
                return;
            }
            if (client.Inventory.FreeSlots == 0)
            {
                Reject(client, "claim", detainID, "inventory_full");
                await Talk.NoticeAsync(client, "No space in your inventory.");
                return;
            }

            if (!await DetainedItemsRepository.DeleteAsync(detainID))
            {
                Reject(client, "claim", detainID, "already_claimed");
                return;
            }

            item.CharacterID = client.ID;
            item.Position = (byte)ItemPosition.Inventory;
            await ItemsRepository.SetOwnerAsync(item.ItemID, client.ID, item.Position);
            if (item.Locked || item.UnlocksAt != null)
            {
                item.Locked = false;
                item.UnlocksAt = null;
                await ItemsRepository.SetLockAsync(item.ItemID, false, null);
            }
            client.Inventory.Add(item);
            await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            await client.SendAsync(MsgDetainItemUpdate.Remove(MsgDetainItemUpdate.RewardPage, detainID));
            await Talk.NoticeAsync(client, "Equipment successfully claimed!");

            if (Kernel.Players.TryGet(entry.TargetID, out var target))
                await target.SendAsync(MsgDetainItemUpdate.Remove(MsgDetainItemUpdate.DetainPage, detainID));

            LogEvents.For(typeof(Detaining), LogEvents.PkClaimed).Information(
                "{Name} claimed expired item {ItemId} (detain {DetainId}) from {Target}",
                character.Name, item.ItemID, detainID, entry.TargetName);
        }

        // NPCs 4450 (Market) and 4542 (Jail) open the client's Redeem Equipment and Claim Prize windows
        public static async Task PrizeOfficerAsync(NpcTalk talk)
        {
            switch (talk.Option)
            {
                case RedeemOption:
                    await talk.OpenWindowAsync(MsgAction.Window.DetainRedeem);
                    return;
                case ClaimOption:
                    await talk.OpenWindowAsync(MsgAction.Window.DetainClaim);
                    return;
            }
            await talk.Text("The red/black name can redeem their detained equipment within 7 days. The CPs they pay " +
                    "for their guilt will be rewarded to the one who killed them. If they do not pay, the equipment " +
                    "detained will be given to the one who killed them as rewards.")
                .Choice("I want to redeem my equipment.", RedeemOption)
                .Choice("I want to claim my rewards.", ClaimOption)
                .Choice("What?", MsgTaskDialog.NoOption)
                .Face(PrizeOfficerFace).ShowAsync();
        }

        private static void Reject(Client client, string action, uint detainID, string reason) =>
            LogEvents.For(typeof(Detaining), LogEvents.PkDetainRejected).Warning(
                "{Name} can't {Action} detain {DetainId}: {Reason}",
                client.Character?.Name, action, detainID, reason);
    }
}
