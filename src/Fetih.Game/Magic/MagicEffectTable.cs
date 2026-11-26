namespace Fetih.Game.Magic
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // How the client shows a spell, from its ini/MagicEffect.ini: one section per type * 10 +
    // level, a level's keys over its level 0's. The client starts a spell's show as the packet
    // comes and shows each target's hit when its throw or shot is done and the target delays
    // (DelayOfTarget3DEffect + WoundDelayOfTarget) have passed, after the caster's intone
    // (IntoneDuration). A kick that lifts its targets (Whirlwind Kick) keeps them in the air
    // for SkillLastTime.
    public sealed record MagicEffect(
        int Represent, int TargetDelay, int WoundDelay, bool Traces, int TraceDelay, int TraceSpeed,
        int TraceAcceleration, int HitGap, int Intone = 0, int LastTime = 0)
    {
        // ClientRepresent: a body thrown at a cell (the bombs), a shot at each target, one
        // effect for each hit of a combo (Triple Attack), a kick that lifts its targets
        public const int Thrown = 3, Shot = 2, ShotEx = 102, ShotChain = 109, Combo = 108, Lift = 107;
    }

    public sealed class MagicEffectTable
    {
        private readonly Dictionary<int, Dictionary<string, string>> Sections;

        public MagicEffectTable(Dictionary<int, Dictionary<string, string>> sections)
        {
            this.Sections = sections;
        }

        public int Count => this.Sections.Count;

        public MagicEffect Find(ushort type, ushort level)
        {
            this.Sections.TryGetValue(type * 10 + level, out var own);
            this.Sections.TryGetValue(type * 10, out var first);
            if (own == null && first == null)
                return null;
            // The client's defaults for keys a section leaves out
            int Int(string key, int otherwise) =>
                own != null && own.TryGetValue(key, out var s) || first != null && first.TryGetValue(key, out s)
                    ? int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0
                    : otherwise;
            bool Has(string key) =>
                own != null && own.TryGetValue(key, out var s) && s.Length > 0 ||
                first != null && first.TryGetValue(key, out s) && s.Length > 0;
            return new MagicEffect(Int("ClientRepresent", 0), Int("DelayOfTarget3DEffect", 0),
                Int("WoundDelayOfTarget", 0), Has("TraceEffect"), Int("TraceEffectDelay", 0),
                Int("TraceEffectSpeed", 100), Int("TraceEffectAcceleration", 300), Int("ExtraCmdTime", 0),
                Int("IntoneDuration", 0), Int("SkillLastTime", 0));
        }

        public static MagicEffectTable Load(string clientDirectory)
        {
            var path = Path.Combine(clientDirectory, "ini", "MagicEffect.ini");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No ini/MagicEffect.ini in {clientDirectory}", path);
            return Parse(File.ReadAllText(path, Encoding.Latin1));
        }

        // A key given twice in a section keeps its first value
        public static MagicEffectTable Parse(string text)
        {
            var sections = new Dictionary<int, Dictionary<string, string>>();
            Dictionary<string, string> section = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = int.TryParse(line.AsSpan(1, line.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? sections[id] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : null;
                    continue;
                }
                int equals = line.IndexOf('=');
                if (section != null && equals > 0)
                    section.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim());
            }
            return new MagicEffectTable(sections);
        }
    }
}
