namespace Fetih.Game.Events
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;

    // Runs the client's weekly events: once a second it finds the sign-up, start and end times
    // passed since the last tick and calls whoever subscribed to that event's name. Times
    // missed while the server was down are not caught up.
    public static class EventScheduler
    {
        public delegate Task Handler(ScheduledEvent ev, EventWindow window, EventEdge edge, DateTime utcNow);

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, List<Handler>> Handlers =
            new Dictionary<string, List<Handler>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime? LastLocal;

        public static EventSchedule Schedule { get; set; } = EventSchedule.Empty;

        public static void Subscribe(string name, Handler handler)
        {
            lock (Sync)
            {
                if (!Handlers.TryGetValue(name, out var list))
                    Handlers[name] = list = new List<Handler>();
                list.Add(handler);
            }
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(EventScheduler), LogEvents.EventLoopFailed).Error(e, "Event scheduler failed");
                }
            }
        }

        public static async Task TickAsync(DateTime utcNow)
        {
            var local = Clock.Local(utcNow);
            DateTime? from;
            lock (Sync)
            {
                from = LastLocal;
                LastLocal = local;
            }
            // Only the last few minutes: a clock jump forward doesn't replay a day of events
            if (from == null || local - from.Value > TimeSpan.FromMinutes(5))
                return;

            foreach (var ev in Schedule.All)
            {
                List<Handler> handlers;
                lock (Sync)
                {
                    if (!Handlers.TryGetValue(ev.Name, out var list))
                        continue;
                    handlers = list.ToList();
                }
                foreach (var (window, edge, at) in EventSchedule.Edges(ev, from.Value, local))
                {
                    LogEvents.For(typeof(EventScheduler), LogEvents.EventEdge).Information(
                        "Event {EventName} ({EventId}, levels {LevelMin}-{LevelMax}): {Edge} at {At:HH:mm}",
                        ev.Name, ev.ID, ev.LevelMin, ev.LevelMax, edge, at);
                    foreach (var handler in handlers)
                    {
                        try { await handler(ev, window, edge, utcNow); }
                        catch (Exception e)
                        {
                            LogEvents.For(typeof(EventScheduler), LogEvents.EventLoopFailed).Error(e,
                                "Event {EventName} ({EventId}) failed on {Edge}", ev.Name, ev.ID, edge);
                        }
                    }
                }
            }
        }

        // For tests: forget the last tick
        public static void Reset()
        {
            lock (Sync)
            {
                LastLocal = null;
                Handlers.Clear();
            }
        }

        public static string StatusText(DateTime utcNow)
        {
            var local = Clock.Local(utcNow);
            var lines = Schedule.All
                .Select(e => (Event: e, Next: EventSchedule.NextSignUp(e, local)))
                .Where(x => x.Next != null)
                .OrderBy(x => x.Next)
                .Select(x => $"{x.Event.Name} {x.Event.LevelMin}-{x.Event.LevelMax}: {x.Next:ddd HH:mm}");
            return $"{Schedule.All.Count} weekly events. Next sign-ups: " + string.Join("; ", lines.Take(8));
        }
    }
}
