namespace Fetih.Game.Chat
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using static Fetih.Game.Packets.MsgPigeon;
    using static Fetih.Game.Packets.MsgTalk;

    public sealed class BroadcastQueue
    {
        public const int PageSize = 10;

        public sealed record Entry(uint ID, uint CharacterID, string SenderName, string Message, uint Addition);

        private readonly object Sync = new object();
        private readonly List<Entry> Entries = new List<Entry>();
        private uint NextID = 1;
        private DateTime LastSentAt = DateTime.MinValue;

        public int Count
        {
            get { lock (this.Sync) return this.Entries.Count; }
        }

        public (Entry Entry, int Rank) Enqueue(uint characterID, string senderName, string message)
        {
            lock (this.Sync)
            {
                var entry = new Entry(this.NextID++, characterID, senderName, message, 0);
                this.Entries.Add(entry);
                return (entry, this.Entries.Count);
            }
        }

        public bool TryFind(uint id, out Entry entry, out int index)
        {
            lock (this.Sync)
            {
                index = this.Entries.FindIndex(e => e.ID == id);
                if (index < 0)
                {
                    entry = null;
                    return false;
                }
                entry = this.Entries[index];
                return true;
            }
        }

        // Moves an owned entry up by advance places (int.MaxValue moves it to 1st) and records
        // the extra CPs spent. Returns false if the entry left the queue or is no longer owned.
        public bool TryPromote(uint id, uint characterID, int advance, uint cost, out int newRank)
        {
            lock (this.Sync)
            {
                int index = this.Entries.FindIndex(e => e.ID == id && e.CharacterID == characterID);
                if (index <= 0)
                {
                    newRank = index + 1;
                    return false;
                }

                int target = advance >= index ? 0 : index - advance;
                var updated = this.Entries[index] with { Addition = this.Entries[index].Addition + cost };
                this.Entries.RemoveAt(index);
                this.Entries.Insert(target, updated);
                newRank = target + 1;
                return true;
            }
        }

        public (int Page, List<(Entry Entry, int Position)> Items) Page(int requestedPage, uint? onlyCharacterID)
        {
            lock (this.Sync)
            {
                var matching = new List<(Entry Entry, int Position)>();
                for (int i = 0; i < this.Entries.Count; i++)
                {
                    if (!onlyCharacterID.HasValue || this.Entries[i].CharacterID == onlyCharacterID.Value)
                        matching.Add((this.Entries[i], i));
                }

                int maxPage = matching.Count == 0 ? 0 : (matching.Count - 1) / PageSize;
                int page = Math.Clamp(requestedPage, 0, maxPage);
                int start = page * PageSize;
                int take = Math.Min(PageSize, Math.Max(0, matching.Count - start));
                return (page, matching.GetRange(start, take));
            }
        }

        public bool TryDequeue(DateTime now, TimeSpan interval, out Entry entry)
        {
            lock (this.Sync)
            {
                if (this.Entries.Count == 0)
                {
                    entry = null;
                    return false;
                }
                if (this.LastSentAt != DateTime.MinValue && now - this.LastSentAt < interval)
                {
                    entry = null;
                    return false;
                }
                entry = this.Entries[0];
                this.Entries.RemoveAt(0);
                this.LastSentAt = now;
                return true;
            }
        }

        public List<Entry> Snapshot()
        {
            lock (this.Sync) return new List<Entry>(this.Entries);
        }

        // Replaces the queue with entries saved earlier, in order, and carries the ID counter on.
        public void Load(IEnumerable<Entry> entries)
        {
            lock (this.Sync)
            {
                this.Entries.Clear();
                this.Entries.AddRange(entries);
                this.NextID = this.Entries.Count == 0 ? 1 : this.Entries.Max(e => e.ID) + 1;
            }
        }

        public void Clear()
        {
            lock (this.Sync)
            {
                this.Entries.Clear();
                this.NextID = 1;
                this.LastSentAt = DateTime.MinValue;
            }
        }
    }

    // Paid server-wide broadcast queue. Players of level 50+ pay 5 CPs to queue a message;
    // messages release one at a time on TalkChannel.Broadcast. Players can pay 5 CPs to move
    // their queued message forward 5 places, or 15 CPs to move it to 1st place.
    public static class Broadcasts
    {
        public const byte MinimumLevel = 50;
        public const int SendCost = 5;
        public const int UrgentCost = 5;
        public const int UrgentTopCost = 15;
        public const int UrgentAdvance = 5;
        public const int MaxMessageBytes = 80;

        public static TimeSpan Interval = TimeSpan.FromSeconds(60);

        private static readonly BroadcastQueue Queue = new BroadcastQueue();
        private static readonly SemaphoreSlim SaveLock = new SemaphoreSlim(1, 1);

        public static int Count => Queue.Count;

        public static async Task LoadAsync()
        {
            var rows = await BroadcastRepository.LoadAsync();
            Queue.Load(rows.OrderBy(r => r.Position).Select(r =>
                new BroadcastQueue.Entry(r.ID, r.CharacterID, r.SenderName, r.Message, r.Addition)));
        }

        // The queue is small, so every change rewrites it whole. Taking the snapshot inside the
        // lock keeps concurrent saves from writing an older order over a newer one.
        private static async Task PersistAsync()
        {
            await SaveLock.WaitAsync();
            try
            {
                var rows = Queue.Snapshot().Select((e, i) => new DbBroadcast
                {
                    ID = e.ID,
                    Position = (uint)i,
                    CharacterID = e.CharacterID,
                    SenderName = e.SenderName,
                    Message = e.Message,
                    Addition = e.Addition
                }).ToList();
                await BroadcastRepository.ReplaceAsync(rows);
            }
            catch (Exception e)
            {
                LogEvents.For(typeof(Broadcasts), LogEvents.BroadcastLoopFailed).Error(
                    e, "Can't save the broadcast queue");
            }
            finally
            {
                SaveLock.Release();
            }
        }

        public static Task HandleAsync(Client client, MsgPigeon msg)
        {
            if (client.Character == null)
                return Task.CompletedTask;

            return msg.Action switch
            {
                PigeonAction.QueryAll => SendPageAsync(client, (int)msg.Param, onlyMine: false),
                PigeonAction.QueryMine => SendPageAsync(client, (int)msg.Param, onlyMine: true),
                PigeonAction.Send => SendAsync(client, msg),
                PigeonAction.Urgent => UrgentAsync(client, msg.Param, top: false),
                PigeonAction.UrgentTop => UrgentAsync(client, msg.Param, top: true),
                _ => LogUnhandledAsync(msg)
            };
        }

        private static async Task SendAsync(Client client, MsgPigeon msg)
        {
            var character = client.Character;
            if (character.Level < MinimumLevel)
            {
                await RejectAsync(client, "level",
                    $"You can use the Broadcast channel right after you reach level {MinimumLevel}.");
                return;
            }

            if (Talk.IsMuted(character, DateTime.UtcNow))
            {
                await RejectAsync(client, "muted",
                    $"You are muted until {character.MutedUntil:yyyy-MM-dd HH:mm} UTC.");
                return;
            }

            string text = Sanitize(msg.Strings.Count > 0 ? msg.Strings[0] : string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                await RejectAsync(client, "empty", "You didn't enter anything to broadcast.");
                return;
            }

            if (Encoding.Latin1.GetByteCount(text) > MaxMessageBytes)
            {
                await RejectAsync(client, "too_long", "The message is too long to broadcast.");
                return;
            }

            if (!await Progress.ChangeConquerPointsAsync(client, -SendCost, "broadcast"))
            {
                await RejectAsync(client, "cps", "You don't have enough CPs to broadcast this message.");
                return;
            }

            var (entry, rank) = Queue.Enqueue(character.CharacterID, character.Name, text);
            await PersistAsync();
            await client.SendAsync(new MsgPigeon(PigeonAction.Send, (uint)rank));
            LogEvents.For(typeof(Broadcasts), LogEvents.BroadcastQueued).Information(
                "{Name} queued broadcast {BroadcastId} at rank {Rank}: {Text}",
                character.Name, entry.ID, rank, text);
        }

        private static async Task UrgentAsync(Client client, uint id, bool top)
        {
            var character = client.Character;
            if (!Queue.TryFind(id, out var existing, out int index))
            {
                await RejectAsync(client, "missing", "That broadcast message is no longer in the queue.");
                return;
            }

            if (existing.CharacterID != character.CharacterID)
            {
                await RejectAsync(client, "owner", "You can only make your own broadcast message urgent.");
                return;
            }

            if (index == 0)
            {
                await RejectAsync(client, "already_first", "Your broadcast message is already 1st in the queue.");
                return;
            }

            int cost = top ? UrgentTopCost : UrgentCost;
            if (!await Progress.ChangeConquerPointsAsync(client, -cost, top ? "broadcast_urgent_top" : "broadcast_urgent"))
            {
                await RejectAsync(client, "cps",
                    "You don't have enough CPs to send an urgent message via the Broadcast channel.");
                return;
            }

            int advance = top ? int.MaxValue : UrgentAdvance;
            if (!Queue.TryPromote(id, character.CharacterID, advance, (uint)cost, out int newRank))
            {
                await Progress.ChangeConquerPointsAsync(client, cost, "broadcast_urgent_refund");
                await RejectAsync(client, "missing", "That broadcast message is no longer in the queue.");
                return;
            }

            await PersistAsync();
            var action = top ? PigeonAction.UrgentTop : PigeonAction.Urgent;
            await client.SendAsync(new MsgPigeon(action, (uint)newRank));
            LogEvents.For(typeof(Broadcasts), LogEvents.BroadcastUrgent).Information(
                "{Name} promoted broadcast {BroadcastId} to rank {Rank} for {Cost} CPs",
                character.Name, id, newRank, cost);
        }

        private static async Task SendPageAsync(Client client, int requestedPage, bool onlyMine)
        {
            var (page, items) = Queue.Page(requestedPage, onlyMine ? client.ID : null);
            if (items.Count == 0)
            {
                await client.SendAsync(new MsgPigeonQuery((uint)page, append: false, null));
                return;
            }

            // A 10-row page splits across two packets (8 + up to 2) to stay within the
            // client's 1024-byte packet buffer.
            for (int offset = 0; offset < items.Count; offset += MsgPigeonQuery.MaxEntries)
            {
                int chunk = Math.Min(MsgPigeonQuery.MaxEntries, items.Count - offset);
                var packet = new MsgPigeonQuery((uint)page, append: offset > 0, null);
                for (int i = 0; i < chunk; i++)
                {
                    var (entry, pos) = items[offset + i];
                    packet.Entries.Add(new MsgPigeonQuery.Entry(
                        entry.ID, (uint)pos, entry.CharacterID, entry.SenderName, entry.Addition, entry.Message));
                }
                await client.SendAsync(packet);
            }
        }

        public static async Task RunAsync(TimeSpan tick, CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(tick);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try { await StepAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(Broadcasts), LogEvents.BroadcastLoopFailed).Error(
                        e, "Broadcast tick failed");
                }
            }
        }

        public static async Task<bool> StepAsync(DateTime now)
        {
            if (!Queue.TryDequeue(now, Interval, out var entry))
                return false;

            await PersistAsync();
            var msg = new MsgTalk(0, TalkChannel.Broadcast, Color.White,
                MsgTalk.ALLUSERS, entry.SenderName, entry.Message);
            foreach (var player in Kernel.Players.Values)
            {
                if (player.Character != null)
                    await player.SendAsync(msg);
            }

            LogEvents.For(typeof(Broadcasts), LogEvents.BroadcastSent).Information(
                "Released broadcast {BroadcastId} from {Name}: {Text}",
                entry.ID, entry.SenderName, entry.Message);
            return true;
        }

        public static string Sanitize(string text) =>
            (text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        private static Task RejectAsync(Client client, string reason, string notice)
        {
            LogEvents.For(typeof(Broadcasts), LogEvents.BroadcastRejected).Information(
                "{Name} broadcast rejected ({Reason})", client.Character.Name, reason);
            return Talk.NoticeAsync(client, notice);
        }

        private static Task LogUnhandledAsync(MsgPigeon msg)
        {
            LogEvents.For(typeof(Broadcasts), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                msg.Type, msg.Action, msg.Length);
            return Task.CompletedTask;
        }
    }
}
