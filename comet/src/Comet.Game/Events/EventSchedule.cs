namespace Comet.Game.Events
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    // When sign-up opens and when it starts, in local time of day. End is when the event
    // closes, or null when it runs until it's decided (a tournament).
    public sealed record EventWindow(TimeSpan SignUp, TimeSpan Start, TimeSpan? End);

    // One weekly event of the client's Today's Activities window, for one level range
    public sealed record ScheduledEvent(int ID, string Name, IReadOnlySet<DayOfWeek> Days,
        int LevelMin, int LevelMax, IReadOnlyList<EventWindow> Windows)
    {
        public bool Fits(int level) => level >= this.LevelMin && level <= this.LevelMax;
    }

    public enum EventEdge { SignUpOpens, Starts, Ends }

    // The weekly events of the client's ini/Activity.ini. Each "[tN]" section has Name,
    // Level_min, Level_max, type (1 weekly; 2 a date range and 3 monthly aren't scheduled),
    // Date ("1|2|..." with 1 Monday and 7 Sunday), and per window i "Enteri=hh:mm~hh:mm" (the
    // sign-up) and "Lasti", either "hh:mm" (the start, when the sign-up closes) or "hh:mm~hh:mm"
    // (start and end).
    public sealed class EventSchedule
    {
        public IReadOnlyList<ScheduledEvent> All { get; }

        public EventSchedule(IReadOnlyList<ScheduledEvent> events) => this.All = events;

        public static EventSchedule Empty { get; } = new EventSchedule(Array.Empty<ScheduledEvent>());

        public IEnumerable<ScheduledEvent> Named(string name) =>
            this.All.Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

        public static EventSchedule Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "Activity.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/Activity.ini in {clientDirectory}", path);
            return Parse(File.ReadAllText(path, Encoding.Latin1));
        }

        public static EventSchedule Parse(string text)
        {
            var events = new List<ScheduledEvent>();
            foreach (var section in Sections(text))
            {
                if (!section.TryGetValue("type", out var type) || type != "1" ||
                    !section.TryGetValue("Name", out var name) ||
                    !int.TryParse(section.GetValueOrDefault("id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    continue;
                var days = new HashSet<DayOfWeek>();
                foreach (var day in section.GetValueOrDefault("Date", "").Split('|'))
                    if (int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d >= 1 && d <= 7)
                        days.Add((DayOfWeek)(d % 7));
                var windows = new List<EventWindow>();
                for (int i = 1; section.TryGetValue("Enter" + i, out var enter); i++)
                {
                    var (signUp, enterEnd) = Range(enter);
                    var last = section.TryGetValue("Last" + i, out var l) ? Range(l) : (null, null);
                    if (signUp == null)
                        continue;
                    // "Last=20:06" starts at 20:06; "Last=21:00~22:00" runs from 21:00 to 22:00
                    var start = last.Item1 ?? enterEnd ?? signUp.Value;
                    var end = last.Item2;
                    windows.Add(new EventWindow(signUp.Value, start, end));
                }
                if (days.Count == 0 || windows.Count == 0)
                    continue;
                events.Add(new ScheduledEvent(id, name.Trim(), days,
                    Int(section.GetValueOrDefault("Level_min"), 1), Int(section.GetValueOrDefault("Level_max"), 255), windows));
            }
            return new EventSchedule(events);
        }

        // The edges of one event between two local times, oldest first; from is exclusive
        public static IEnumerable<(EventWindow Window, EventEdge Edge, DateTime At)> Edges(
            ScheduledEvent ev, DateTime fromLocal, DateTime toLocal)
        {
            if (toLocal <= fromLocal)
                yield break;
            var found = new List<(EventWindow, EventEdge, DateTime)>();
            for (var day = fromLocal.Date; day <= toLocal.Date; day = day.AddDays(1))
            {
                if (!ev.Days.Contains(day.DayOfWeek))
                    continue;
                foreach (var window in ev.Windows)
                {
                    Add(found, window, EventEdge.SignUpOpens, day + window.SignUp, fromLocal, toLocal);
                    Add(found, window, EventEdge.Starts, day + window.Start, fromLocal, toLocal);
                    if (window.End is TimeSpan end)
                        Add(found, window, EventEdge.Ends, day + end, fromLocal, toLocal);
                }
            }
            foreach (var edge in found.OrderBy(f => f.Item3).ThenBy(f => f.Item2))
                yield return edge;
        }

        // The window whose sign-up is open at the given local time, if any
        public static EventWindow SignUpWindow(ScheduledEvent ev, DateTime local)
        {
            if (!ev.Days.Contains(local.DayOfWeek))
                return null;
            var time = local.TimeOfDay;
            return ev.Windows.FirstOrDefault(w => time >= w.SignUp && time < w.Start);
        }

        // The next time sign-up opens after the given local time, within a week
        public static DateTime? NextSignUp(ScheduledEvent ev, DateTime local) =>
            Edges(ev, local, local.AddDays(7)).Where(e => e.Edge == EventEdge.SignUpOpens)
                .Select(e => (DateTime?)e.At).FirstOrDefault();

        private static void Add(List<(EventWindow, EventEdge, DateTime)> found, EventWindow window, EventEdge edge,
            DateTime at, DateTime from, DateTime to)
        {
            if (at > from && at <= to)
                found.Add((window, edge, at));
        }

        private static (TimeSpan?, TimeSpan?) Range(string value)
        {
            var parts = value.Split('~');
            return (Time(parts[0]), parts.Length > 1 ? Time(parts[1]) : null);
        }

        private static TimeSpan? Time(string value) =>
            TimeSpan.TryParseExact(value.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var time) ? time : null;

        private static int Int(string value, int fallback) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        private static IEnumerable<Dictionary<string, string>> Sections(string text)
        {
            Dictionary<string, string> current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    if (current != null)
                        yield return current;
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                int equals = line.IndexOf('=');
                if (current != null && equals > 0)
                    current[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            if (current != null)
                yield return current;
        }
    }
}
