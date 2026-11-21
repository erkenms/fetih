namespace Comet.Game.Social
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgTeam;

    // Teams change from several players' packets at once: membership only changes under the
    // lock, and packets go out after it.
    public static class Teams
    {
        private static readonly object Lock = new object();

        public static Task HandleAsync(Client client, MsgTeam msg)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            switch (msg.Action)
            {
                case TeamAction.Create: return CreateAsync(client);
                case TeamAction.Invite: return InviteAsync(client, msg.Data);
                case TeamAction.AcceptInvite: return AcceptInviteAsync(client, msg.Data);
                case TeamAction.ApplyJoin: return ApplyJoinAsync(client, msg.Data);
                case TeamAction.AcceptJoin: return AcceptJoinAsync(client, msg.Data);
                case TeamAction.Leave: return LeaveAsync(client, "left");
                case TeamAction.Dismiss: return DismissAsync(client);
                case TeamAction.Kick: return KickAsync(client, msg.Data);
                default:
                    LogEvents.For(typeof(Teams), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                        msg.Type, msg.Action, msg.Length);
                    return Task.CompletedTask;
            }
        }

        private static async Task CreateAsync(Client client)
        {
            lock (Lock)
            {
                if (client.Team != null)
                {
                    Reject(client, "create", 0, "in_team");
                    return;
                }
                client.Team = new Team(client);
            }

            await client.SendAsync(new MsgTeam(TeamAction.Leader, client.ID));
            await client.SendAsync(new MsgTeam(TeamAction.Create, client.ID));
            LogEvents.For(typeof(Teams), LogEvents.TeamCreated).Information(
                "{Name} created a team", client.Character.Name);
        }

        private static async Task InviteAsync(Client client, uint targetID)
        {
            string reason;
            Client target = null;
            lock (Lock)
            {
                var team = client.Team;
                reason =
                    team?.Leader != client ? "not_leader" :
                    team.Full ? "full" :
                    !Kernel.Players.TryGet(targetID, out target) || !Near(client, target) ? "not_near" :
                    target.Team != null ? "in_team" :
                    null;
                if (reason == null)
                    team.Invited.Add(target.ID);
            }

            if (reason != null)
            {
                Reject(client, "invite", targetID, reason);
                if (reason == "in_team")
                    await Talk.NoticeAsync(client, $"{target.Character.Name} is already in a team.");
                return;
            }
            await target.SendAsync(new MsgRelation(client.Character, target.ID));
            await target.SendAsync(new MsgTeam(TeamAction.Invite, client.ID));
        }

        private static Task AcceptInviteAsync(Client client, uint leaderID)
        {
            Kernel.Players.TryGet(leaderID, out var leader);
            return JoinAsync(client, leader?.Team, "accept_invite", leaderID,
                team => team.Leader == leader && team.Invited.Contains(client.ID));
        }

        private static async Task ApplyJoinAsync(Client client, uint memberID)
        {
            string reason;
            Client leader = null;
            lock (Lock)
            {
                Client member = null;
                var team = Kernel.Players.TryGet(memberID, out member) ? member.Team : null;
                reason =
                    client.Team != null ? "in_team" :
                    team == null || !Near(client, member) ? "no_team" :
                    team.Full ? "full" :
                    null;
                if (reason == null)
                {
                    team.Applicants.Add(client.ID);
                    leader = team.Leader;
                }
            }

            if (reason != null)
            {
                Reject(client, "apply", memberID, reason);
                if (reason == "full")
                    await Talk.NoticeAsync(client, "That team is full.");
                return;
            }
            await leader.SendAsync(new MsgRelation(client.Character, leader.ID));
            await leader.SendAsync(new MsgTeam(TeamAction.ApplyJoin, client.ID));
        }

        private static Task AcceptJoinAsync(Client client, uint applicantID)
        {
            if (!Kernel.Players.TryGet(applicantID, out var applicant))
            {
                Reject(client, "accept_join", applicantID, "offline");
                return Task.CompletedTask;
            }
            var team = client.Team;
            return JoinAsync(applicant, team, "accept_join", applicantID,
                t => t.Leader == client && t.Applicants.Contains(applicant.ID));
        }

        private static async Task JoinAsync(Client joiner, Team team, string kind, uint id,
            System.Func<Team, bool> asked)
        {
            string reason;
            List<Client> members = null;
            lock (Lock)
            {
                reason =
                    team == null || !team.Members.Contains(team.Leader) || team.Leader.Team != team ? "no_team" :
                    !asked(team) ? "not_asked" :
                    joiner.Team != null ? "in_team" :
                    team.Full ? "full" :
                    !Near(joiner, team.Leader) ? "not_near" :
                    null;
                if (reason == null)
                {
                    team.Invited.Remove(joiner.ID);
                    team.Applicants.Remove(joiner.ID);
                    members = team.Members.ToList();
                    team.Members.Add(joiner);
                    joiner.Team = team;
                }
            }

            if (reason != null)
            {
                Reject(joiner, kind, id, reason);
                if (reason == "full")
                    await Talk.NoticeAsync(joiner, "That team is full.");
                return;
            }

            var joined = new MsgTeamMember(joiner.Character, Attributes.MaxHealth(joiner));
            foreach (var member in members)
            {
                await member.SendAsync(joined);
                await joiner.SendAsync(new MsgTeamMember(member.Character, Attributes.MaxHealth(member)));
            }
            await joiner.SendAsync(joined);
            LogEvents.For(typeof(Teams), LogEvents.TeamJoined).Information(
                "{Name} joined the team of {Leader} ({Members} members)",
                joiner.Character.Name, team.Leader.Character.Name, members.Count + 1);
        }

        // Also when a player logs out; a leader leaving ends the team
        public static async Task LeaveAsync(Client client, string reason)
        {
            List<Client> members;
            Team team;
            lock (Lock)
            {
                team = client.Team;
                if (team == null)
                    return;
                if (team.Leader == client)
                {
                    members = Dismiss(team);
                }
                else
                {
                    members = team.Members.ToList();
                    team.Members.Remove(client);
                    client.Team = null;
                }
            }

            if (team.Leader == client)
            {
                await DismissedAsync(client, members, reason);
                return;
            }
            var left = new MsgTeam(TeamAction.Leave, client.ID);
            foreach (var member in members)
                await member.SendAsync(left);
            LogEvents.For(typeof(Teams), LogEvents.TeamLeft).Information(
                "{Name} left the team of {Leader} ({Reason})",
                client.Character.Name, team.Leader.Character.Name, reason);
        }

        private static async Task DismissAsync(Client client)
        {
            List<Client> members;
            lock (Lock)
            {
                if (client.Team?.Leader != client)
                {
                    Reject(client, "dismiss", 0, "not_leader");
                    return;
                }
                members = Dismiss(client.Team);
            }
            await DismissedAsync(client, members, "dismissed");
        }

        private static async Task KickAsync(Client client, uint memberID)
        {
            List<Client> members = null;
            Client kicked = null;
            lock (Lock)
            {
                var team = client.Team;
                kicked = team?.Members.FirstOrDefault(m => m.ID == memberID);
                if (team?.Leader == client && kicked != null && kicked != client)
                {
                    members = team.Members.ToList();
                    team.Members.Remove(kicked);
                    kicked.Team = null;
                }
            }

            if (members == null)
            {
                Reject(client, "kick", memberID, "not_member");
                return;
            }
            var kick = new MsgTeam(TeamAction.Kick, memberID);
            foreach (var member in members)
                await member.SendAsync(kick);
            LogEvents.For(typeof(Teams), LogEvents.TeamLeft).Information(
                "{Name} left the team of {Leader} ({Reason})",
                kicked.Character.Name, client.Character.Name, "kicked");
        }

        // The other members, for team chat
        public static List<Client> OthersOf(Client client)
        {
            lock (Lock)
                return client.Team?.Members.Where(m => m != client).ToList() ?? new List<Client>();
        }

        private static List<Client> Dismiss(Team team)
        {
            var members = team.Members.ToList();
            foreach (var member in members)
                member.Team = null;
            team.Members.Clear();
            return members;
        }

        private static async Task DismissedAsync(Client leader, List<Client> members, string reason)
        {
            var dismiss = new MsgTeam(TeamAction.Dismiss, leader.ID);
            foreach (var member in members)
                await member.SendAsync(dismiss);
            LogEvents.For(typeof(Teams), LogEvents.TeamDismissed).Information(
                "{Name} ended their team of {Members} ({Reason})", leader.Character.Name, members.Count, reason);
        }

        private static bool Near(Client client, Client other)
        {
            var map = client.Map;
            return other != client && other.Character != null && map != null && map == other.Map &&
                map.TryGetPosition(client, out var x, out var y) &&
                map.TryGetPosition(other, out var otherX, out var otherY) &&
                GameMap<Client>.InView(x, y, otherX, otherY);
        }

        private static void Reject(Client client, string kind, uint id, string reason) =>
            LogEvents.For(typeof(Teams), LogEvents.TeamRejected).Warning(
                "{Name} can't {Kind} {Id}: {Reason}", client.Character?.Name, kind, id, reason);
    }
}
