namespace Comet.Game.Social
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgTradeBuddy;

    // Trade partners: up to 20 each, on a three-day trial first. Lists live on the client
    // from login and in the database; a partnership changes two players at once, so changes
    // run under one gate.
    public static class TradePartners
    {
        public const int MaxPartners = 20;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static async Task LoadAsync(Client client)
        {
            foreach (var (id, name, since) in await TradePartnersRepository.LoadAsync(client.ID))
                client.TradePartners[id] = new TradePartner(name, since);
        }

        // With the friends at login: the list, and telling the partners this player is online
        public static async Task SendListAsync(Client client)
        {
            var now = DateTime.UtcNow;
            foreach (var (id, partner) in client.TradePartners)
                await client.SendAsync(new MsgTradeBuddy(PartnerAction.Add, id, partner.Name, IsOnline(id), partner.TrialMinutes(now)));
            await AnnounceAsync(client, online: true);
        }

        public static Task LogoutAsync(Client client) => AnnounceAsync(client, online: false);

        // Whether the player may give locked items to the other
        public static bool IsFormal(Client client, uint otherID, DateTime now) =>
            client.TradePartners.TryGetValue(otherID, out var partner) && partner.IsFormal(now);

        public static async Task HandleAsync(Client client, MsgTradeBuddy msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case PartnerAction.Request: await RequestAsync(client, msg.ID); break;
                    case PartnerAction.Reject: await RejectRequestAsync(client, msg.ID); break;
                    case PartnerAction.Break: await BreakAsync(client, msg.ID); break;
                    default:
                        LogEvents.For(typeof(TradePartners), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                            msg.Type, msg.Action, msg.Length);
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        // MsgAction 152: a partner's details, by ID in Command; answered with the details
        // if they're online, then the echo
        public static async Task InfoAsync(Client client, MsgAction msg)
        {
            if (client.Character == null)
                return;
            if (client.TradePartners.ContainsKey(msg.Command) && Kernel.Players.TryGet(msg.Command, out var partner) &&
                partner.Character is { } character)
            {
                var info = new MsgTradeBuddyInfo
                {
                    ID = character.CharacterID,
                    Mesh = (uint)(character.Mesh + character.Avatar * 10000),
                    Level = character.Level,
                    Class = character.CurrentClass,
                    PkPoints = character.KillPoints,
                };
                if (Guilds.Of(partner.ID) is Guilds.Membership guild)
                {
                    info.GuildID = guild.Guild.ID;
                    info.GuildRank = (uint)guild.Member.Rank;
                }
                await client.SendAsync(info);
            }
            await client.SendAsync(msg);
        }

        private static async Task RequestAsync(Client client, uint targetID)
        {
            Kernel.Players.TryGet(targetID, out var target);
            string reason =
                target?.Character == null || target == client ? "offline" :
                client.TradePartners.ContainsKey(targetID) ? "already_partners" :
                client.TradePartners.Count >= MaxPartners ? "list_full" :
                target.TradePartners.Count >= MaxPartners ? "their_list_full" :
                null;
            if (reason != null)
            {
                Reject(client, "request", targetID, reason);
                string notice = reason switch
                {
                    "already_partners" => "Sorry, you are already trade partners.",
                    "list_full" => $"Sorry, you've had {MaxPartners} trade partners. You can't have more.",
                    "their_list_full" => $"{target.Character.Name} can't have more trade partners.",
                    _ => null,
                };
                if (notice != null)
                    await Talk.NoticeAsync(client, notice);
                return;
            }

            // Asking someone who asked first makes the partnership
            if (target.PartnerRequest == client.ID)
            {
                await PartnerAsync(target, client);
                return;
            }

            client.PartnerRequest = target.ID;
            await target.SendAsync(new MsgRelation(client.Character, target.ID));
            await target.SendAsync(new MsgTradeBuddy(PartnerAction.Request, client.ID, client.Character.Name, true));
            await Talk.NoticeAsync(client, "Trade partner request sent.");
            LogEvents.For(typeof(TradePartners), LogEvents.TradePartnerRequested).Information(
                "{Name} asked {Partner} to be trade partners", client.Character.Name, target.Character.Name);
        }

        private static async Task RejectRequestAsync(Client client, uint askerID)
        {
            if (!Kernel.Players.TryGet(askerID, out var asker) || asker.Character == null ||
                asker.PartnerRequest != client.ID)
            {
                Reject(client, "reject", askerID, "not_asked");
                return;
            }
            asker.PartnerRequest = 0;
            await asker.SendAsync(new MsgTradeBuddy(PartnerAction.Reject, client.ID, client.Character.Name, true));
            await Talk.NoticeAsync(asker, $"{client.Character.Name} has rejected to be your trade partner.");
            Reject(asker, "request", client.ID, "refused");
        }

        private static async Task PartnerAsync(Client asker, Client accepter)
        {
            asker.PartnerRequest = 0;
            if (accepter.PartnerRequest == asker.ID)
                accepter.PartnerRequest = 0;
            // Whole seconds, as the database keeps them
            var now = DateTime.UtcNow;
            var since = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            await TradePartnersRepository.AddAsync(asker.ID, accepter.ID, since);
            var forAsker = asker.TradePartners[accepter.ID] = new TradePartner(accepter.Character.Name, since);
            var forAccepter = accepter.TradePartners[asker.ID] = new TradePartner(asker.Character.Name, since);

            await asker.SendAsync(new MsgTradeBuddy(PartnerAction.Add, accepter.ID, forAsker.Name, true, forAsker.TrialMinutes(now)));
            await accepter.SendAsync(new MsgTradeBuddy(PartnerAction.Add, asker.ID, forAccepter.Name, true, forAccepter.TrialMinutes(now)));
            await Talk.NoticeAsync(asker, $"{accepter.Character.Name} has agreed to be your trade partner. You will be formal partners after 3 days' trial.");
            await Talk.NoticeAsync(accepter, $"{asker.Character.Name} has agreed to be your trade partner. You will be formal partners after 3 days' trial.");
            LogEvents.For(typeof(TradePartners), LogEvents.TradePartnerAdded).Information(
                "{Name} and {Partner} became trade partners (characters {CharacterId} and {PartnerId})",
                asker.Character.Name, accepter.Character.Name, asker.ID, accepter.ID);
        }

        private static async Task BreakAsync(Client client, uint partnerID)
        {
            if (!client.TradePartners.TryGetValue(partnerID, out var partner))
            {
                Reject(client, "break", partnerID, "not_partners");
                return;
            }

            await TradePartnersRepository.RemoveAsync(client.ID, partnerID);
            client.TradePartners.TryRemove(partnerID, out _);
            await client.SendAsync(new MsgTradeBuddy(PartnerAction.Break, partnerID, partner.Name, IsOnline(partnerID)));
            if (Kernel.Players.TryGet(partnerID, out var other) && other.Character != null)
            {
                other.TradePartners.TryRemove(client.ID, out _);
                await other.SendAsync(new MsgTradeBuddy(PartnerAction.Break, client.ID, client.Character.Name, true));
                await Talk.NoticeAsync(other, $"{client.Character.Name} broke the trade partnership with you.");
            }
            LogEvents.For(typeof(TradePartners), LogEvents.TradePartnerRemoved).Information(
                "{Name} broke the trade partnership with {Partner} (characters {CharacterId} and {PartnerId})",
                client.Character.Name, partner.Name, client.ID, partnerID);
        }

        private static async Task AnnounceAsync(Client client, bool online)
        {
            var character = client.Character;
            if (character == null)
                return;
            var action = online ? PartnerAction.Online : PartnerAction.Offline;
            foreach (var id in client.TradePartners.Keys)
                if (Kernel.Players.TryGet(id, out var partner) && partner != client && partner.Character != null)
                    await partner.SendAsync(new MsgTradeBuddy(action, client.ID, character.Name, online));
        }

        private static bool IsOnline(uint id) =>
            Kernel.Players.TryGet(id, out var player) && player.Character != null;

        private static void Reject(Client client, string kind, uint id, string reason) =>
            LogEvents.For(typeof(TradePartners), LogEvents.TradePartnerRejected).Warning(
                "{Name} can't {Kind} trade partner {Id}: {Reason}", client.Character?.Name, kind, id, reason);
    }
}
