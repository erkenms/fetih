namespace Comet.Network.Sockets
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Hosting;

    public sealed class TcpServerRegistry : IHostedService, IDisposable
    {
        private Dictionary<string, int> Active;
        private Dictionary<string, int> Attempts;
        private ConcurrentDictionary<string, DateTime> Blocks;
        private Timer PurgeTimer;
        private object ActiveMutex;
        private object AttemptsMutex;
        private readonly int BanMinutes;
        private readonly int MaxActiveConnections;
        private readonly int MaxAttemptsPerMinute;

        public TcpServerRegistry(
            int banMinutes = 15,
            int maxActiveConn = 10, 
            int maxAttemptsPerMinute = 15)
        {
            this.Active = new Dictionary<string, int>();
            this.Attempts = new Dictionary<string, int>();
            this.Blocks = new ConcurrentDictionary<string, DateTime>();

            this.ActiveMutex = new object();
            this.AttemptsMutex = new object();
            this.BanMinutes = banMinutes;
            this.MaxActiveConnections = maxActiveConn;
            this.MaxAttemptsPerMinute = maxAttemptsPerMinute;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.PurgeTimer = new Timer(
                this.TimedPurgeJob, 
                null, 
                TimeSpan.Zero, 
                TimeSpan.FromSeconds(60));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.PurgeTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            this.PurgeTimer?.Dispose();
        }

        public bool AddActiveClient(string ip)
        {
            if (this.Blocks.ContainsKey(ip))
            {
                return false;
            }

            return IncrementCounter(ip, this.MaxAttemptsPerMinute, ref this.AttemptsMutex, ref this.Attempts)
                && IncrementCounter(ip, this.MaxActiveConnections, ref this.ActiveMutex, ref this.Active);
        }

        public bool IncrementCounter(
            string ip,
            int ceiling,
            ref object mutex,
            ref Dictionary<string, int> collection)
        {
            lock (mutex)
            if (collection.TryGetValue(ip, out int count))
            {
                count++;
                if (count > ceiling)
                {
                    this.Blocks.TryAdd(ip, DateTime.UtcNow.AddMinutes(this.BanMinutes));
                    return false;
                }

                collection[ip] = count;
            }
            else collection.TryAdd(ip, 1);
            return true;
        }

        public void RemoveActiveClient(string ip)
        {
            lock (this.ActiveMutex)
            if (this.Active.TryGetValue(ip, out int attempts))
            {
                attempts--;
                if (attempts == 0) 
                {
                    this.Active.Remove(ip);
                    return;
                }
                else this.Active[ip] = attempts;
            }
        }

        public void TimedPurgeJob(object state)
        {
            lock (AttemptsMutex) 
                this.Attempts.Clear();

            DateTime now = DateTime.UtcNow;
            foreach (var blockedConnection in this.Blocks)
            {
                if (blockedConnection.Value < now)
                    this.Blocks.TryRemove(blockedConnection.Key, out DateTime _);
            }
        }
    }
}
