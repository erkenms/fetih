namespace Comet.Game
{
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using Comet.Network.Security;
    using Comet.Network.Sockets;
    using Serilog.Context;

    internal sealed class Server : TcpServerListener<Client>
    {
        private readonly PacketProcessor<Client> Processor;
        private readonly Task ProcessorTask;

        public Server(ServerConfiguration config) 
            : base(maxConn: config.GameNetwork.MaxConn, exchange: true, footerLength: 8,
                maxConnPerIP: config.GameNetwork.MaxConnPerIP,
                maxAttemptsPerMinute: config.GameNetwork.MaxAttemptsPerMinute,
                loginTimeoutSeconds: config.GameNetwork.LoginTimeoutSeconds)
        {
            this.Processor = new PacketProcessor<Client>(this.ProcessAsync);
            this.ProcessorTask = this.Processor.StartAsync(CancellationToken.None);
            Kernel.Post = this.Post;
        }

        private void Post(Client actor, Func<Task> work) =>
            this.Processor.Queue(actor, async () =>
            {
                if (!actor.Socket.Connected)
                    return;
                using var connScope = LogContext.PushProperty("ConnId", actor.ConnId);
                using var characterScope = actor.ID != 0
                    ? LogContext.PushProperty("CharacterId", actor.ID) : null;
                try { await work(); }
                catch (Exception e)
                {
                    LogEvents.For<Server>(LogEvents.PacketError).Error(e,
                        "Failed to run queued work for connection {ConnId}", actor.ConnId);
                }
            });

        protected override async Task<Client> AcceptedAsync(Socket socket, Memory<byte> buffer)
        {
            var partition = this.Processor.SelectPartition();
            var client = new Client(socket, buffer, partition);
            await client.DiffieHellman.ComputePublicKeyAsync();

            var handshakeRequest = new MsgHandshake(client.DiffieHellman);
            await handshakeRequest.RandomizeAsync();
            await client.SendAsync(handshakeRequest);
            return client;
        }

        protected override bool Exchanged(Client actor, ReadOnlySpan<byte> buffer)
        {
            try
            {
                MsgHandshake msg = new MsgHandshake();
                msg.Decode(buffer.ToArray());

                actor.DiffieHellman.ComputePrivateKey(msg.ClientKey);

                // Rekeying also resets both IVs to zero, as the client does
                actor.Cipher.GenerateKeys(new object[] {
                    actor.DiffieHellman.ComputeCipherKey() });

                actor.DiffieHellman = null;
                return true;
            }
            catch (Exception e)
            {
                Serilog.Log.ForContext<Server>().Error(e,
                    "Key exchange failed for connection {ConnId}", actor.ConnId);
            }
            return false;
        }

        protected override void Received(Client actor, ReadOnlySpan<byte> packet)
        {
            this.Processor.Queue(actor, packet.ToArray());
        }

        private async Task ProcessAsync(Client actor, byte[] packet) 
        {
            if (!actor.Socket.Connected)
                return;

            var length = BitConverter.ToUInt16(packet, 0);
            PacketType type = (PacketType)BitConverter.ToUInt16(packet, 2);

            using var connScope = LogContext.PushProperty("ConnId", actor.ConnId);
            using var accountScope = actor.AccountID != 0
                ? LogContext.PushProperty("AccountId", actor.AccountID) : null;
            using var characterScope = actor.ID != 0
                ? LogContext.PushProperty("CharacterId", actor.ID) : null;

            try
            {
                MsgBase<Client> msg = PacketHandlers.Create(type);
                if (msg == null)
                {
                    LogEvents.For<Server>(LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} ({Length} bytes): {Hex}",
                        type, length, Convert.ToHexString(packet));
                    await actor.SendAsync(new MsgTalk(actor.ID, MsgTalk.TalkChannel.Service,
                        String.Format("Missing packet {0}, Length {1}",
                        type, length)));
                    return;
                }

                msg.Decode(packet);
                await msg.ProcessAsync(actor);
            }
            catch (Exception e) 
            { 
                LogEvents.For<Server>(LogEvents.PacketError).Error(e,
                    "Failed to process packet {PacketType}", type);
            }
        }

        private static async Task LeaveOthersAsync(Client actor)
        {
            try
            {
                await Social.Trading.CancelAsync(actor, "logout");
                await Items.Vending.CloseAsync(actor, "logout");
                await Magic.Casting.DropAuraAsync(actor);
                Magic.Transforming.Revert(actor);
                await Social.Teams.LeaveAsync(actor, "logout");
                await Arena.Qualifier.LeaveAsync(actor, "logout");
                await Social.Relationships.LogoutAsync(actor);
                await Social.TradePartners.LogoutAsync(actor);
            }
            catch (Exception e)
            {
                LogEvents.For<Server>(LogEvents.SessionLogoutFailed).Error(e,
                    "Telling others that character {CharacterId} left failed", actor.ID);
            }
        }

        protected override void Disconnected(Client actor) 
        {
            if (actor == null) return;
            this.Processor.DeselectPartition(actor.Partition);

            // Registered before the client leaves Kernel.Clients, so a login for this account
            // finds either the client or its logout, and loads the character after the save
            var logout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (actor.AccountID != 0)
                Kernel.Logouts[actor.AccountID] = logout.Task;

            // Only removes the registry entry if it still belongs to this client; a
            // replacing login for the same account keeps its entry.
            Kernel.Clients.Remove(actor.AccountID, actor);

            if (actor.Creation != null)
                Kernel.Registration.TryRemove(actor.Creation.Token, out _);

            Task leaving = Task.CompletedTask;
            if (actor.Character != null && actor.LoggedInAt.HasValue)
            {
                var character = actor.Character;
                Visibility.Leave(actor);
                Kernel.Players.Remove(character.CharacterID, character.Name, actor);
                leaving = LeaveOthersAsync(actor);
                LogEvents.For<Server>(LogEvents.SessionLogout).Information(
                    "{Name} logged out: {Reason} after {DurationSec}s " +
                    "(connection {ConnId}, account {AccountId}, character {CharacterId}, " +
                    "from {IP}, map {MapId} at {X},{Y}; {Online} online)",
                    character.Name, actor.DisconnectReason ?? DisconnectReasons.Server,
                    Math.Round((DateTime.UtcNow - actor.LoggedInAt.Value).TotalSeconds, 3),
                    actor.ConnId, actor.AccountID, character.CharacterID, actor.IPAddress,
                    character.MapID, character.X, character.Y, Kernel.Players.Count);
            }

            _ = FinishLogoutAsync(actor, leaving, logout);
        }

        private static async Task FinishLogoutAsync(Client actor, Task leaving, TaskCompletionSource logout)
        {
            try { await Task.WhenAll(leaving, actor.LogoutAsync()); }
            catch (Exception e)
            {
                LogEvents.For<Server>(LogEvents.SessionLogoutFailed).Error(e,
                    "Logout save failed for character {CharacterId}", actor.ID);
            }
            finally
            {
                Kernel.Logouts.TryRemove(new KeyValuePair<uint, Task>(actor.AccountID, logout.Task));
                logout.SetResult();
            }
        }
    }
}
