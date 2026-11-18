namespace Comet.Network.Sockets
{
    using System;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;

    /// <summary>
    /// TcpServerListener implements an asynchronous TCP streaming socket server for high
    /// performance server logic. Socket operations are processed in value tasks using
    /// Socket Task Extensions. Inherits from a base class for providing socket operation
    /// event handling to the non-abstract derived class of TcpServerListener.
    /// </summary>
    /// <typeparam name="TActor">Type of actor passed by the parent project</typeparam>
    public abstract class TcpServerListener<TActor> : TcpServerEvents<TActor>
        where TActor : TcpServerActor
    {
        // TCP keepalive: after 60 s without traffic the OS probes the client every 10 s,
        // and closes the connection after 5 unanswered probes (about 110 s in all). This
        // only drops clients whose machine or network is gone, never idle players.
        private const int KeepAliveIdleSeconds = 60;
        private const int KeepAliveIntervalSeconds = 10;
        private const int KeepAliveProbes = 5;

        // Fields and properties
        private readonly TimeSpan HandshakeTimeout;
        private readonly Semaphore AcceptanceSemaphore;
        private readonly ConcurrentStack<Memory<byte>> BufferPool;
        private readonly bool EnableKeyExchange;
        private readonly int FooterLength;
        private readonly TaskFactory ReceiveTasks;
        private readonly CancellationTokenSource ShutdownToken;
        private readonly Socket Socket;
        private TcpServerRegistry Registry;

        /// <summary>
        /// Instantiates a new instance of <see cref="TcpServerListener"/> with a new server
        /// socket for accepting remote or local client connections. Creates preallocated
        /// buffers for receiving data from clients without expensive allocations per receive
        /// operation.
        /// </summary>
        /// <param name="maxConn">Maximum number of clients connected</param>
        /// <param name="bufferSize">Preallocated buffer size in bytes</param>
        /// <param name="delay">Use Nagel's algorithm to delay sending smaller packets</param>
        /// <param name="exchange">Use a key exchange before receiving packets</param>
        /// <param name="footerLength">Length of the packet footer</param>
        /// <param name="handshakeTimeoutSeconds">
        /// Time a new connection has to send its key exchange (or, without one, its first
        /// bytes). After that there is no time limit: idle clients stay connected.
        /// </param>
        public TcpServerListener(
            int maxConn = 1000,
            int bufferSize = 4096,
            bool delay = false,
            bool exchange = false,
            int footerLength = 0,
            int maxConnPerIP = 10,
            int maxAttemptsPerMinute = 15,
            int handshakeTimeoutSeconds = 30)
        {
            this.HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSeconds);
            // Initialize and configure server socket
            this.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.Socket.LingerState = new LingerOption(false, 0);
            this.Socket.NoDelay = !delay;
            this.ShutdownToken = new CancellationTokenSource();

            // Initialize management mechanisms
            this.AcceptanceSemaphore = new Semaphore(maxConn, maxConn);
            this.BufferPool = new ConcurrentStack<Memory<byte>>();
            this.EnableKeyExchange = exchange;
            this.FooterLength = footerLength;
            this.ReceiveTasks = new TaskFactory(this.ShutdownToken.Token);
            this.Registry = new TcpServerRegistry(
                maxActiveConn: maxConnPerIP, maxAttemptsPerMinute: maxAttemptsPerMinute);

            // Initialize preallocated buffer pool
            for (int i = 0; i < maxConn; i++)
                this.BufferPool.Push(new Memory<byte>(new byte[bufferSize]));
        }

        /// <summary>
        /// Binds the server listener to a port and network interface. Specify "0.0.0.0" 
        /// as the endpoint address to bind to all interfaces on the host machine. Starts
        /// the server listener and accepts new connections in a new task.
        /// </summary>
        /// <param name="port">Port number the server will bind to</param>
        /// <param name="address">Interface IPv4 address the server will bind to</param>
        /// <param name="backlog">Maximum connections backlogged for acceptance</param>
        /// <returns>Returns a new task for accepting new connections.</returns>
        public async Task StartAsync(int port, string address = "0.0.0.0", int backlog = 100)
        {
            this.Socket.Bind(new IPEndPoint(IPAddress.Parse(address), port));
            this.Socket.Listen(backlog);
            
            // Start the background registry cleaner and accepting clients
            await this.Registry.StartAsync(this.ShutdownToken.Token);
            await this.AcceptingAsync();
        }

        /// <summary>
        /// Accepting accepts client connections asynchronously as a new task. As a client
        /// connection is accepted, it will be associated with a preallocated buffer and
        /// a receive task. The accepted socket event will be called after accept.
        /// </summary>
        /// <returns>Returns task details for fault tolerance processing.</returns>
        private async Task AcceptingAsync()
        {
            while (this.Socket.IsBound && !this.ShutdownToken.IsCancellationRequested)
            {
                // Block if the maximum connections has been reached. Holds all connection
                // attempts in the backlog of the server socket until a client disconnects
                // and a new client can be accepted. Check shutdown every 5 seconds.
                if (this.AcceptanceSemaphore.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    // Pop a preallocated buffer and check the connection
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
                    
                    // Construct the client before receiving data
                    EnableKeepAlive(socket);
                    this.BufferPool.TryPop(out var buffer);
                    var actor = await this.AcceptedAsync(socket, buffer);
                    LogEvents.For(this.GetType(), LogEvents.NetConnect).Information(
                        "Connection {ConnId} opened from {IP}", actor.ConnId, actor.IPAddress);

                    // Start receiving data from the client connection
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

        /// <summary>
        /// Turns on TCP keepalive for an accepted client, so a client whose machine or
        /// network disappeared is noticed even if it never sends anything again.
        /// </summary>
        /// <param name="socket">Accepted client socket</param>
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

        /// <summary>
        /// Exchanging receives bytes from the accepted client socket when bytes become
        /// available as a raw buffer of bytes. This method is called once and then invokes
        /// <see cref="ReceivingAsync"/>.
        /// </summary>
        /// <param name="state">Created actor around the accepted client socket</param>
        /// <returns>Returns task details for fault tolerance processing.</returns>
        private async Task ExchangingAsync(object state)
        {
            // Initialize multiple receive variables
            var actor = state as TActor;
            var timeout = new CancellationTokenSource();
            int consumed = 0, examined = 0, remaining = 0;
            bool firstPacketPending = true;

            if (actor.Socket.Connected && !this.ShutdownToken.IsCancellationRequested)
            {
                try
                {
                    // Receive data from the client socket
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        timeout.Token, this.ShutdownToken.Token);
                    var receiveOperation = actor.Socket.ReceiveAsync(
                        actor.Buffer[..],
                        SocketFlags.None,
                        cancellation.Token);
                        
                    timeout.CancelAfter(this.HandshakeTimeout);
                    examined = await receiveOperation;
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

                // Decrypt traffic by first discarding the first 7 bytes, as per TQ Digital's
                // exchange protocol, then decrypting only what is necessary for the exchange.
                // This is to prevent the next packet from being decrypted with the wrong key.
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

                // Process the exchange now that bytes are decrypted
                if (!this.Exchanged(actor, actor.Buffer[..consumed].Span))
                {
                    actor.Disconnect(DisconnectReasons.HandshakeFailed);
                    this.Disconnecting(actor);
                    return;
                }

                // Now that the key has changed, decrypt the rest of the bytes in the buffer
                // and prepare to start receiving packets on a standard receive loop.
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

            // Start receiving packets
            await this.ReceivingAsync(state, remaining, firstPacketPending);
        }

        /// <summary>
        /// Called when the first packet after the key exchange can't be read. The usual
        /// cause is that the client and server derived different cipher keys, so this is
        /// recorded as a failed handshake rather than a protocol error.
        /// </summary>
        /// <param name="actor">Actor whose first packet couldn't be split</param>
        private void FirstPacketUnreadable(TActor actor)
        {
            Serilog.Log.ForContext(this.GetType()).Warning(
                "Connection {ConnId} sent unreadable data right after the key exchange; " +
                "the cipher keys probably don't match", actor.ConnId);
            actor.Disconnect(DisconnectReasons.HandshakeFailed);
        }

        /// <summary>
        /// Receiving receives bytes from the accepted client socket when bytes become
        /// available. While the client is connected and the server hasn't issued the 
        /// shutdown signal, bytes will be received in a loop.
        /// </summary>
        /// <param name="state">Created actor around the accepted client socket</param>
        /// <returns>Returns task details for fault tolerance processing.</returns>
        private Task ReceivingAsync(object state)
        {
            return this.ReceivingAsync(state, 0, false, firstReceiveDeadline: true);
        }

        /// <summary>
        /// Receiving receives bytes from the accepted client socket when bytes become
        /// available. While the client is connected and the server hasn't issued the 
        /// shutdown signal, bytes will be received in a loop.
        /// </summary>
        /// <param name="state">Created actor around the accepted client socket</param>
        /// <param name="remaining">Starting offset to receive bytes to</param>
        /// <param name="firstPacketPending">
        /// True if a key exchange just finished and no packet has been read since
        /// </param>
        /// <param name="firstReceiveDeadline">
        /// True if the first bytes must arrive within the handshake timeout (servers
        /// without a key exchange). Later receives have no time limit.
        /// </param>
        /// <returns>Returns task details for fault tolerance processing.</returns>
        private async Task ReceivingAsync(
            object state, int remaining, bool firstPacketPending, bool firstReceiveDeadline = false)
        {
            // Initialize multiple receive variables
            var actor = state as TActor;
            using var timeout = new CancellationTokenSource();
            int examined = 0, consumed = 0;
            if (firstReceiveDeadline)
                timeout.CancelAfter(this.HandshakeTimeout);

            while (actor.Socket.Connected && !this.ShutdownToken.IsCancellationRequested)
            {
                try
                {
                    // Receive data from the client socket
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        timeout.Token, this.ShutdownToken.Token);
                    examined = await actor.Socket.ReceiveAsync(
                        actor.Buffer[remaining..],
                        SocketFlags.None,
                        cancellation.Token);

                    // Only the first receive has a deadline
                    timeout.CancelAfter(Timeout.InfiniteTimeSpan);
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

                // Decrypt traffic
                actor.Cipher.Decrypt(
                    actor.Buffer.Slice(remaining, examined).Span,
                    actor.Buffer.Slice(remaining, examined).Span);

                // Handle splitting and processing of data
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

            // Disconnect the client
            if (this.ShutdownToken.IsCancellationRequested)
                actor.SetDisconnectReason(DisconnectReasons.Shutdown);
            this.Disconnecting(actor);
        }

        /// <summary>
        /// Maps a receive exception to a disconnect reason. Unexpected exceptions are
        /// logged as errors; expected network failures are only recorded as the reason.
        /// </summary>
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

        /// <summary>
        /// Splitting splits the actor's receive buffer into multiple packets that can
        /// then be processed by Received individually. The default behavior of this method
        /// unless otherwise overridden is to split packets from the buffer using an unsigned
        /// short packet header for the length of each packet.
        /// </summary>
        /// <param name="buffer">Actor for consuming bytes from the buffer</param>
        /// <param name="examined">Number of examined bytes from the receive</param>
        /// <param name="consumed">Number of consumed bytes by the split reader</param>
        /// <returns>Returns true if the client should remain connected.</returns>
        protected virtual bool Splitting(TActor actor, int examined, ref int consumed)
        {
            // Consume packets from the socket buffer
            var buffer = actor.Buffer.Span;
            while (consumed + 2 < examined)
            {
                var length = BitConverter.ToUInt16(buffer[consumed..]);
                if (length == 0) return false;
                var expected = consumed + length + this.FooterLength;
                if (length > buffer.Length) return false;
                if (expected > examined) break;

                this.Received(actor, buffer.Slice(consumed, length + this.FooterLength));
                consumed += length + this.FooterLength;
            }

            return true;
        }

        /// <summary>
        /// Disconnecting is called when the client is disconnecting from the server. Allows
        /// the server to handle client events post-disconnect, and reclaim resources first
        /// leased to the client on accept.
        /// </summary>
        /// <param name="actor">Actor being disconnected</param>
        private void Disconnecting(TActor actor)
        {
            // Close the socket so the client sees the disconnect. Without this, a
            // connection that timed out or broke the protocol stayed open forever
            // while the server ignored it.
            try { actor.Socket.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            actor.Socket.Close();

            // Reclaim resources and release back to server pools
            actor.Buffer.Span.Clear();
            this.BufferPool.Push(actor.Buffer);
            this.AcceptanceSemaphore.Release();
            this.Registry.RemoveActiveClient(actor.IPAddress);

            var duration = DateTime.UtcNow - actor.ConnectedAt;
            LogEvents.For(this.GetType(), LogEvents.NetDisconnect).Information(
                "Connection {ConnId} from {IP} closed: {Reason} after {DurationSec}s",
                actor.ConnId, actor.IPAddress, actor.DisconnectReason ?? DisconnectReasons.Server,
                Math.Round(duration.TotalSeconds, 3));

            // Complete processing for disconnect
            this.Disconnected(actor);
        }
    }
}
