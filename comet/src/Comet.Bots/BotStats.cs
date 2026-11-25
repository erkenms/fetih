namespace Comet.Bots
{
    using System;
    using System.Threading;

    // Counters every bot adds to; Report takes what happened since the last report
    public sealed class BotStats
    {
        private int online, logins, failures, kills, deaths, steps, jumps, attacks, echoes, pickups, potions;
        private long echoTicks, slowestEchoTicks;

        public int Online => Volatile.Read(ref this.online);

        public void Entered() { Interlocked.Increment(ref this.online); Interlocked.Increment(ref this.logins); }
        public void Left() => Interlocked.Decrement(ref this.online);
        public void Failed() => Interlocked.Increment(ref this.failures);
        public void Killed() => Interlocked.Increment(ref this.kills);
        public void Died() => Interlocked.Increment(ref this.deaths);
        public void Stepped() => Interlocked.Increment(ref this.steps);
        public void Jumped() => Interlocked.Increment(ref this.jumps);
        public void Attacked() => Interlocked.Increment(ref this.attacks);
        public void PickedUp() => Interlocked.Increment(ref this.pickups);
        public void Drank() => Interlocked.Increment(ref this.potions);

        // How long the server took to answer a jump
        public void Echo(TimeSpan took)
        {
            Interlocked.Increment(ref this.echoes);
            Interlocked.Add(ref this.echoTicks, took.Ticks);
            long slowest;
            while (took.Ticks > (slowest = Interlocked.Read(ref this.slowestEchoTicks)) &&
                   Interlocked.CompareExchange(ref this.slowestEchoTicks, took.Ticks, slowest) != slowest) { }
        }

        public string Report(int total, TimeSpan since)
        {
            double seconds = Math.Max(1, since.TotalSeconds);
            int echoes = Interlocked.Exchange(ref this.echoes, 0);
            long echoTicks = Interlocked.Exchange(ref this.echoTicks, 0);
            long slowest = Interlocked.Exchange(ref this.slowestEchoTicks, 0);
            string echo = echoes == 0 ? "-" :
                $"{TimeSpan.FromTicks(echoTicks / echoes).TotalMilliseconds:0} ms avg, {TimeSpan.FromTicks(slowest).TotalMilliseconds:0} ms max";
            return $"online {this.Online}/{total} | logins {Interlocked.Exchange(ref this.logins, 0)}, " +
                $"failures {Interlocked.Exchange(ref this.failures, 0)} | " +
                $"steps {Interlocked.Exchange(ref this.steps, 0) / seconds:0.0}/s, jumps {Interlocked.Exchange(ref this.jumps, 0) / seconds:0.0}/s, " +
                $"attacks {Interlocked.Exchange(ref this.attacks, 0) / seconds:0.0}/s | " +
                $"kills {Interlocked.Exchange(ref this.kills, 0)}, deaths {Interlocked.Exchange(ref this.deaths, 0)}, " +
                $"pickups {Interlocked.Exchange(ref this.pickups, 0)}, potions {Interlocked.Exchange(ref this.potions, 0)} | jump answer {echo}";
        }
    }
}
