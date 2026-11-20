namespace Comet.Network.Packets
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Comet.Network.Sockets;
    using Microsoft.Extensions.Hosting;

    public class PacketProcessor<TClient> : BackgroundService
        where TClient : TcpServerActor
    {
        protected readonly Task[] BackgroundTasks;
        protected readonly Channel<Message>[] Channels;
        protected readonly Partition[] Partitions;
        protected readonly Func<TClient, byte[], Task> Process;
        protected CancellationToken CancelReads;
        protected CancellationToken CancelWrites;

        public PacketProcessor(
            Func<TClient, byte[], Task> process,
            int count = 0)
        {
            count = count == 0 ? Environment.ProcessorCount : count;
            this.BackgroundTasks = new Task[count];
            this.CancelReads = new CancellationToken();
            this.CancelWrites = new CancellationToken();
            this.Channels = new Channel<Message>[count];
            this.Partitions = new Partition[count];
            this.Process = process;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (int i = 0; i < this.BackgroundTasks.Length; i++)
            {
                this.Partitions[i] = new Partition { ID = (uint)i };
                this.Channels[i] = Channel.CreateUnbounded<Message>();
                this.BackgroundTasks[i] = DequeueAsync(this.Channels[i]);
            }
            
            return Task.WhenAll(this.BackgroundTasks);
        }

        public void Queue(TClient actor, byte[] packet)
        {
            if (!this.CancelWrites.IsCancellationRequested)
                this.Channels[actor.Partition].Writer.TryWrite(new Message {
                    Actor = actor,
                    Packet = packet
                });
        }

        // Work for the actor that runs in order with its packets, so it never runs alongside
        // them. The caller handles the work's exceptions.
        public void Queue(TClient actor, Func<Task> work)
        {
            if (!this.CancelWrites.IsCancellationRequested)
                this.Channels[actor.Partition].Writer.TryWrite(new Message {
                    Actor = actor,
                    Work = work
                });
        }

        protected async Task DequeueAsync(Channel<Message> channel)
        {
            while (!this.CancelReads.IsCancellationRequested)
            {
                var msg = await channel.Reader.ReadAsync(this.CancelReads);
                if (msg?.Work != null)
                    await msg.Work().ConfigureAwait(false);
                else if (msg != null) 
                { 
                    await this.Process(msg.Actor, msg.Packet).ConfigureAwait(false); 
                }
            }
        }

        public new async Task StopAsync(CancellationToken cancellationToken)
        {
            this.CancelWrites = new CancellationToken(true);
            foreach (var channel in this.Channels)
                await channel.Reader.Completion;
            this.CancelReads = new CancellationToken(true);
            await base.StopAsync(cancellationToken);
        }

        public uint SelectPartition()
        {
            uint partition = this.Partitions.Aggregate((aggr, next) => 
                next.Weight.CompareTo(aggr.Weight) < 0 ? next : aggr).ID;
            Interlocked.Increment(ref this.Partitions[partition].Weight);
            return partition;
        }

        public void DeselectPartition(uint partition)
        {
            Interlocked.Decrement(ref this.Partitions[partition].Weight);
        }

        protected class Message
        {
            public TClient Actor;
            public byte[] Packet;
            public Func<Task> Work;
        }

        protected class Partition
        {
            public uint ID;
            public int Weight;
        }
    }
}