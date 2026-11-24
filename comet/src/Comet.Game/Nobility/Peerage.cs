namespace Comet.Game.Nobility
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgPeerage;

    // Nobility: players of level 70 and up donate silver (or CPs for it) from the nobility
    // button; the total ranks them. A donation can move others on the board, so everyone
    // whose standing changed hears of it, and those whose rank changed are shown again to
    // the players around them.
    public static class Peerage
    {
        public const byte MinLevel = 70;

        private static readonly object Sync = new object();
        private static readonly Dictionary<uint, Donor> Donors = new Dictionary<uint, Donor>();
        private static List<Donor> ordered = new List<Donor>();

        // Replaced whole on each change, so it's read without the lock
        private static volatile Dictionary<uint, Standing> standings = new Dictionary<uint, Standing>();

        public static int Count
        {
            get { lock (Sync) return Donors.Count; }
        }

        public static async Task LoadAsync()
        {
            var rows = await NobilityRepository.LoadAsync();
            lock (Sync)
            {
                foreach (var row in rows)
                    Donors[row.CharacterID] = new Donor(row.CharacterID, row.Name, row.Mesh, row.Donation);
                Reorder();
            }
        }

        public static Standing StandingOf(uint characterID) =>
            standings.TryGetValue(characterID, out var standing) ? standing : NobilityBoard.Commoner;

        public static NobilityRank RankOf(uint characterID) => StandingOf(characterID).Rank;

        public static Task HandleAsync(Client client, MsgPeerage msg)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            switch (msg.Action)
            {
                case PeerageAction.Donate:
                    return DonateAsync(client, msg.Data, msg.Currency);
                case PeerageAction.Board:
                    return SendBoardAsync(client, (int)(uint)msg.Data);
                case PeerageAction.Minimum:
                    return SendMinimumAsync(client, (NobilityRank)(uint)msg.Data);
                default:
                    LogEvents.For(typeof(Peerage), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                        msg.Type, msg.Action, msg.Length, Convert.ToHexString(msg.Encode()));
                    return Task.CompletedTask;
            }
        }

        // After MsgUserInfo: the client shows the nobility button once it has the player's rank
        public static async Task LoginAsync(Client client)
        {
            var character = client.Character;
            ulong donation;
            lock (Sync)
            {
                donation = Donors.TryGetValue(character.CharacterID, out var donor) ? donor.Donation : 0;
                if (donor != null)
                    Donors[donor.ID] = donor with { Name = character.Name, Mesh = MeshOf(character) };
            }
            await client.SendAsync(MsgPeerage.Info(client.ID, donation, StandingOf(client.ID)));
        }

        public static void Rename(uint characterID, string name)
        {
            lock (Sync)
            {
                if (Donors.TryGetValue(characterID, out var donor))
                    Donors[characterID] = donor with { Name = name };
            }
        }

        private static async Task DonateAsync(Client client, ulong amount, ushort currency)
        {
            var character = client.Character;
            bool conquerPoints = currency != 0;
            if (character.Level < MinLevel)
            {
                await RejectAsync(client, amount, "level", "Only level 70+ can donate.");
                return;
            }
            if (amount < NobilityBoard.MinDonation || amount > uint.MaxValue)
            {
                await RejectAsync(client, amount, "amount", "You need to donate at least 3,000,000 silver.");
                return;
            }
            ulong price = conquerPoints ? NobilityBoard.ConquerPointsFor(amount) : amount;
            bool paid = conquerPoints
                ? await Progress.ChangeConquerPointsAsync(client, -(long)price, "nobility_donation")
                : await Progress.ChangeSilverAsync(client, -(long)price, "nobility_donation");
            if (!paid)
            {
                await RejectAsync(client, amount, conquerPoints ? "cps" : "silver",
                    conquerPoints ? "You don't have enough CPs." : "You don't have enough silver.");
                return;
            }

            Dictionary<uint, Standing> before, after;
            ulong total;
            lock (Sync)
            {
                Donors.TryGetValue(character.CharacterID, out var donor);
                total = (donor?.Donation ?? 0) + amount;
                Donors[character.CharacterID] = new Donor(character.CharacterID, character.Name, MeshOf(character), total);
                before = standings;
                Reorder();
                after = standings;
            }
            await NobilityRepository.SaveAsync(character.CharacterID, total);

            var standing = after[character.CharacterID];
            await client.SendAsync(MsgPeerage.Donated(client.ID, amount, total, standing.Position));
            LogEvents.For(typeof(Peerage), LogEvents.NobilityDonated).Information(
                "{Name} donated {Amount} silver for nobility in {Currency} ({Price}); total {Total}, " +
                "rank {Rank}, position {Position}",
                character.Name, amount, conquerPoints ? "cps" : "silver", price, total,
                standing.Rank.ToString(), standing.Position);
            await AnnounceChangesAsync(before, after);
        }

        private static async Task AnnounceChangesAsync(Dictionary<uint, Standing> before, Dictionary<uint, Standing> after)
        {
            foreach (var (id, now) in after)
            {
                var was = before.TryGetValue(id, out var standing) ? standing : NobilityBoard.Commoner;
                if (was == now)
                    continue;
                if (was.Rank != now.Rank)
                    LogEvents.For(typeof(Peerage), LogEvents.NobilityRank).Information(
                        "Character {CharacterId} nobility rank {FromRank} to {Rank} (position {Position})",
                        id, was.Rank.ToString(), now.Rank.ToString(), now.Position);
                if (!Kernel.Players.TryGet(id, out var player) || player.Character == null)
                    continue;
                ulong donation;
                lock (Sync)
                    donation = Donors.TryGetValue(id, out var donor) ? donor.Donation : 0;
                await player.SendAsync(MsgPeerage.Info(id, donation, now));
                if (was.Rank != now.Rank)
                    await Movement.SendToViewAsync(player, new MsgPlayer(player));
            }
        }

        // Pages count from 0
        private static Task SendBoardAsync(Client client, int page)
        {
            var reply = new MsgPeerage { Action = PeerageAction.Board, Data = (ulong)Math.Max(0, page) };
            List<Donor> shown;
            Dictionary<uint, Standing> ranked;
            lock (Sync)
            {
                reply.Pages = (ushort)NobilityBoard.Pages(ordered.Count);
                shown = ordered.Take(NobilityBoard.Size)
                    .Skip((int)reply.Data * NobilityBoard.PageSize).Take(NobilityBoard.PageSize).ToList();
                ranked = standings;
            }
            reply.Entries = shown.Select(d =>
            {
                bool online = Kernel.Players.TryGet(d.ID, out var player) && player.Character != null;
                var donor = online ? d with { Mesh = player.Life.Mesh(player.Character) } : d;
                return (donor, online, ranked[d.ID]);
            }).ToList();
            return client.SendAsync(reply);
        }

        private static Task SendMinimumAsync(Client client, NobilityRank rank)
        {
            var reply = new MsgPeerage { Action = PeerageAction.Minimum };
            lock (Sync)
            {
                ulong donation = Donors.TryGetValue(client.ID, out var donor) ? donor.Donation : 0;
                ulong total = NobilityBoard.TotalFor(rank, ordered, client.ID);
                reply.Data = NobilityBoard.Remaining(total, donation);
                reply.Position = NobilityBoard.PositionWith(Math.Max(total, donation), ordered, client.ID);
                reply.CurrentPosition = StandingOf(client.ID).Position;
            }
            return client.SendAsync(reply);
        }

        private static Task RejectAsync(Client client, ulong amount, string reason, string notice)
        {
            LogEvents.For(typeof(Peerage), LogEvents.NobilityRejected).Information(
                "{Name} can't donate {Amount} silver for nobility ({Reason})",
                client.Character.Name, amount, reason);
            return Talk.NoticeAsync(client, notice);
        }

        private static void Reorder()
        {
            ordered = NobilityBoard.Order(Donors.Values);
            standings = NobilityBoard.Rank(ordered);
        }

        private static uint MeshOf(Character character) => (uint)(character.Mesh + character.Avatar * 10000);
    }
}
