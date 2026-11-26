namespace Fetih.Network.Sockets
{
    using System;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;

    public abstract class TcpServerListener<TActor> : TcpServerEvents<TActor>
        where TActor : TcpServerActor
    {
        // TCP keepalive: after 60 s without traffic the OS probes the client every 10 s,
        // and closes the connection after 5 unanswered probes (about 110 s in all). This
        // only drops clients whose machine or network is gone, never idle players.
        private const int KeepAliveIdleSeconds = 60;
        private const int KeepAliveIntervalSeconds = 10;
        private const int KeepAliveProbes = 5;

        private readonly TimeSpan LoginTimeout;
        private readonly Semaphore AcceptanceSemaphore;
        private readonly ConcurrentStack<Memory<byte>> BufferPool;
        private readonly bool EnableKeyExchange;
        private readonly int FooterLength;
        private readonly TaskFactory ReceiveTasks;
        private readonly CancellationTokenSource ShutdownToken;
        private readonly Socket Socket;
        private TcpServerRegistry Registry;

        public TcpServerListener(
            int maxConn = 1000,
            int bufferSize = 4096,
            bool delay = false,
            bool exchange = false,
            int footerLength = 0,
            int maxConnPerIP = 10,
            int maxAttemptsPerMinute = 15,
            int loginTimeoutSeconds = 60)
        {
            this.LoginTimeout = TimeSpan.FromSeconds(loginTimeoutSeconds);
            this.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.Socket.LingerState = new LingerOption(false, 0);
            this.Socket.NoDelay = !delay;
            this.ShutdownToken = new CancellationTokenSource();

            this.AcceptanceSemaphore = new Semaphore(maxConn, maxConn);
            this.BufferPool = new ConcurrentStack<Memory<byte>>();
            this.EnableKeyExchange = exchange;
            this.FooterLength = footerLength;
            this.ReceiveTasks = new TaskFactory(this.ShutdownToken.Token);
            this.Registry = new TcpServerRegistry(
                maxActiveConn: maxConnPerIP, maxAttemptsPerMinute: maxAttemptsPerMinute);

            for (int i = 0; i < maxConn; i++)
                this.BufferPool.Push(new Memory<byte>(new byte[bufferSize]));
        }

        public async Task StartAsync(int port, string address = "0.0.0.0", int backlog = 100)
        {
            this.Socket.Bind(new IPEndPoint(IPAddress.Parse(address), port));
            this.Socket.Listen(backlog);
            
            await this.Registry.StartAsync(this.ShutdownToken.Token);
            try { await this.AcceptingAsync(); }
            catch (Exception e) when (this.ShutdownToken.IsCancellationRequested &&
                (e is ObjectDisposedException || e is SocketException))
            {
            }
        }

        // Stops accepting and ends every connection with reason shutdown, each through
        // Disconnecting as usual.
        public void Stop()
        {
            this.ShutdownToken.Cancel();
            this.Socket.Close();
        }

        private async Task AcceptingAsync()
        {
            while (this.Socket.IsBound && !this.ShutdownToken.IsCancellationRequested)
            {
                // At the connection limit, new attempts wait in the socket's backlog until a
                // client disconnects; the timeout only rechecks for shutdown.
                if (this.AcceptanceSemaphore.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    var socket = await this.Socket.AcceptAsync();
                    var ip = (socket.RemoteEndPoint as IPEndPoint).Address.MapToIPv4().ToString();
                    if (!this.Registry.AddActiveClient(ip))
                    {
                        LogEvents.For(this.GetType(), LogEvents.NetRejected).Warning(
                            "Rejected connection from {IP}: too many connections from this address",
                            ip);
                        socket.Disconnect(false);
                        this.AcceptanceSemaphore.Release();
                        continue;
                    }
                    
                    EnableKeepAlive(socket);
                    this.BufferPool.TryPop(out var buffer);
                    var actor = await this.AcceptedAsync(socket, buffer);
                    actor.LoginDeadline.CancelAfter(this.LoginTimeout);
                    LogEvents.For(this.GetType(), LogEvents.NetConnect).Information(
                        "Connection {ConnId} opened from {IP}", actor.ConnId, actor.IPAddress);

                    if (this.EnableKeyExchange)
                    {
                        var task = this.ReceiveTasks
                            .StartNew(this.ExchangingAsync, actor, this.ShutdownToken.Token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        var task = this.ReceiveTasks
                            .StartNew(this.ReceivingAsync, actor, this.ShutdownToken.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private static void EnableKeepAlive(Socket socket)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveTime, KeepAliveIdleSeconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveInterval, KeepAliveIntervalSeconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveRetryCount, KeepAliveProbes);
            }
            catch (SocketException e)
            {
                Serilog.Log.ForContext<TcpServerListener<TActor>>().Warning(e,
                    "Couldn't set TCP keepalive on an accepted connection");
            }
        }

        private async Task ExchangingAsync(object state)
        {
            var actor = state as TActor;
            int consumed = 0, examined = 0, remaining = 0;
            bool firstPacketPending = true;

            if (actor.Socket.Connected && !this.ShutdownToken.IsCancellationRequested)
            {
                try
                {
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        actor.LoginDeadline.Token, this.ShutdownToken.Token);
                    examined = await actor.Socket.ReceiveAsync(
                        actor.Buffer[..],
                        SocketFlags.None,
                        cancellation.Token);
                    if (examined == 0)
                    {
                        actor.Disconnect(DisconnectReasons.ClientClosed);
                        this.Disconnecting(actor);
                        return; 
                    }
                    if (examined < 9)
                    {
                        actor.Disconnect(DisconnectReasons.HandshakeFailed);
                        this.Disconnecting(actor);
                        return;
                    }
                }
                catch (Exception e)
                {
                    actor.Disconnect(this.ReasonFor(e));
                    this.Disconnecting(actor);
                    return;
                }

                // The key exchange: 7 bytes of padding, then its length counted from 7. Only the
                // exchange is decrypted with the initial key; what follows it in the buffer is
                // under the exchanged key.
                actor.Cipher.Decrypt(
                    actor.Buffer[..9].Span,
                    actor.Buffer[..9].Span);
                consumed = BitConverter.ToUInt16(actor.Buffer.Span.Slice(7, 2)) + 7;
                if (consumed > examined)
                {
                    actor.Disconnect(DisconnectReasons.HandshakeFailed);
                    this.Disconnecting(actor);
                    return;
                }

                actor.Cipher.Decrypt(
                    actor.Buffer[9..(consumed)].Span,
                    actor.Buffer[9..(consumed)].Span);

                PacketTrace.In(actor, actor.Buffer[..consumed].Span);
                if (!this.Exchanged(actor, actor.Buffer[..consumed].Span))
                {
                    actor.Disconnect(DisconnectReasons.HandshakeFailed);
                    this.Disconnecting(actor);
                    return;
                }

                // Exchanged has rekeyed the cipher, so the rest is decrypted only now
                if (consumed < examined)
                {
                    actor.Cipher.Decrypt(
                        actor.Buffer[consumed..examined].Span,
                        actor.Buffer[consumed..examined].Span);

                    int exchangeEnd = consumed;
                    if (!this.Splitting(actor, examined, ref consumed))
                    {
                        this.FirstPacketUnreadable(actor);
                        this.Disconnecting(actor);
                        return;
                    }
                    
                    firstPacketPending = consumed == exchangeEnd;
                    remaining = examined - consumed;
                    actor.Buffer[consumed..examined].CopyTo(actor.Buffer);
                }
            }

            await this.ReceivingAsync(state, remaining, firstPacketPending);
        }

        // Usually the client and server derived different keys, so this counts as a failed
        // handshake rather than a protocol error.
        private void FirstPacketUnreadable(TActor actor)
        {
            Serilog.Log.ForContext(this.GetType()).Warning(
                "Connection {ConnId} sent unreadable data right after the key exchange; " +
                "the cipher keys probably don't match", actor.ConnId);
            actor.Disconnect(DisconnectReasons.HandshakeFailed);
        }

        private Task ReceivingAsync(object state)
        {
            return this.ReceivingAsync(state, 0, false);
        }

        private async Task ReceivingAsync(object state, int remaining, bool firstPacketPending)
        {
            var actor = state as TActor;
            int examined = 0, consumed = 0;

            while (actor.Socket.Connected && !this.ShutdownToken.IsCancellationRequested)
            {
                try
                {
                    // The login deadline can cancel the wait until the client logs in
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        actor.LoginDeadline.Token, this.ShutdownToken.Token);
                    examined = await actor.Socket.ReceiveAsync(
                        actor.Buffer[remaining..],
                        SocketFlags.None,
                        cancellation.Token);
                    if (examined == 0)
                    {
                        actor.SetDisconnectReason(DisconnectReasons.ClientClosed);
                        break;
                    }
                }
                catch (Exception e)
                {
                    actor.SetDisconnectReason(this.ReasonFor(e));
                    break;
                }

                actor.Cipher.Decrypt(
                    actor.Buffer.Slice(remaining, examined).Span,
                    actor.Buffer.Slice(remaining, examined).Span);

                consumed = 0;
                if (!this.Splitting(actor, examined + remaining, ref consumed))
                {
                    if (firstPacketPending) this.FirstPacketUnreadable(actor);
                    else actor.Disconnect(DisconnectReasons.ProtocolError);
                    break;
                }
                
                firstPacketPending &= consumed == 0;
                remaining = examined + remaining - consumed;
                actor.Buffer.Slice(consumed, remaining).CopyTo(actor.Buffer);
            }

            if (this.ShutdownToken.IsCancellationRequested)
                actor.SetDisconnectReason(DisconnectReasons.Shutdown);
            this.Disconnecting(actor);
        }

        private string ReasonFor(Exception e)
        {
            switch (e)
            {
                case OperationCanceledException:
                    return this.ShutdownToken.IsCancellationRequested
                        ? DisconnectReasons.Shutdown
                        : DisconnectReasons.Timeout;

                case SocketException socketEx:
                    switch (socketEx.SocketErrorCode)
                    {
                        case SocketError.ConnectionReset:
                        case SocketError.ConnectionAborted:
                            return DisconnectReasons.Reset;
                        case SocketError.TimedOut:
                            return DisconnectReasons.Timeout;
                        case SocketError.Shutdown:
                        case SocketError.NotConnected:
                        case SocketError.OperationAborted:
                            // Socket closed locally, e.g. by a server-side Disconnect
                            return DisconnectReasons.Server;
                        default:
                            LogEvents.For(this.GetType(), LogEvents.NetDisconnect).Warning(socketEx,
                                "Unexpected socket error {SocketError}", socketEx.SocketErrorCode);
                            return DisconnectReasons.SocketError;
                    }

                case ObjectDisposedException:
                    return DisconnectReasons.Server;

                default:
                    Serilog.Log.ForContext(this.GetType()).Error(e, "Unexpected receive failure");
                    return DisconnectReasons.SocketError;
            }
        }

        protected virtual bool Splitting(TActor actor, int examined, ref int consumed)
        {
            var buffer = actor.Buffer.Span;
            while (consumed + 2 < examined)
            {
                var length = BitConverter.ToUInt16(buffer[consumed..]);
                if (length == 0) return false;
                var expected = consumed + length + this.FooterLength;
                if (length > buffer.Length) return false;
                if (expected > examined) break;

                PacketTrace.In(actor, buffer.Slice(consumed, length));
                this.Received(actor, buffer.Slice(consumed, length + this.FooterLength));
                consumed += length + this.FooterLength;
            }

            return true;
        }

        private void Disconnecting(TActor actor)
        {
            try { actor.Socket.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            actor.Socket.Close();

            actor.Buffer.Span.Clear();
            this.BufferPool.Push(actor.Buffer);
            this.AcceptanceSemaphore.Release();
            this.Registry.RemoveActiveClient(actor.IPAddress);

            var duration = DateTime.UtcNow - actor.ConnectedAt;
            LogEvents.For(this.GetType(), LogEvents.NetDisconnect).Information(
                "Connection {ConnId} from {IP} closed: {Reason} after {DurationSec}s",
                actor.ConnId, actor.IPAddress, actor.DisconnectReason ?? DisconnectReasons.Server,
                Math.Round(duration.TotalSeconds, 3));

            this.Disconnected(actor);
        }
    }
}
