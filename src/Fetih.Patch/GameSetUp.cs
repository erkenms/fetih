using System.Text;

namespace Fetih.Patch;

// ini/GameSetUp.ini: the launcher writes only the keys it names and leaves the rest as it was
public static class GameSetUp
{
    public const string RelativePath = "ini/GameSetUp.ini";

    // What play.exe's high quality button did: [GameMode] HighTimes + 1, GameModeRecord 1
    // (normal is 0, low 2; Conquer.exe reads only GameModeRecord)
    public static void SetHighQuality(string iniPath)
    {
        var ini = IniText.Load(iniPath);
        var times = int.TryParse(ini.Get("GameMode", "HighTimes"), out var n) ? n : 0;
        ini.Set("GameMode", "HighTimes", (times + 1).ToString());
        ini.Set("GameMode", "GameModeRecord", "1");
        ini.Save(iniPath);
    }

    public static ScreenMode ReadScreen(string iniPath)
    {
        var ini = IniText.Load(iniPath);
        int Int(string key, int fallback) => int.TryParse(ini.Get("ScreenMode", key), out var v) ? v : fallback;
        var fullType = Int("FullScrType", 1);
        return Int("ScreenModeRecord", 2) switch
        {
            0 => new(800, 600, true),
            1 => new(800, 600, false),
            2 => new(1024, 768, true),
            3 => new(1024, 768, false),
            _ => new(Int("ScrWidth", 1024), Int("ScrHeight", 768), fullType == ScreenMode.FullScreenType),
        };
    }

    // Record 4 is the client's own "custom": it takes ScrWidth, ScrHeight and FullScrType as written
    public static void WriteScreen(string iniPath, ScreenMode mode)
    {
        var ini = IniText.Load(iniPath);
        ini.Set("ScreenMode", "ScreenModeRecord", "4");
        ini.Set("ScreenMode", "ScrWidth", mode.Width.ToString());
        ini.Set("ScreenMode", "ScrHeight", mode.Height.ToString());
        ini.Set("ScreenMode", "FullScrType", (mode.FullScreen ? ScreenMode.FullScreenType : 1 - ScreenMode.FullScreenType).ToString());
        ini.Save(iniPath);
    }

    static readonly (int W, int H)[] CommonSizes =
        [(1920, 1080), (1680, 1050), (1600, 900), (1440, 900), (1366, 768), (1280, 1024), (1280, 800), (1280, 720), (1024, 768), (800, 600)];

    // The screen's size first, then common ones that fit on it
    public static IReadOnlyList<(int W, int H)> Sizes(int screenWidth, int screenHeight) =>
        CommonSizes.Where(s => s.W <= screenWidth && s.H <= screenHeight)
            .Prepend((screenWidth, screenHeight))
            .Distinct()
            .ToList();
}

public readonly record struct ScreenMode(int Width, int Height, bool FullScreen)
{
    // FullScrType that means full screen: records 0 and 2 set 1, 1 and 3 set 0
    public const int FullScreenType = 1;
}

// Lines kept as they are, CRLF and all; values matched by section and key, ignoring case
sealed class IniText
{
    static readonly Encoding Latin1 = Encoding.Latin1;
    readonly List<string> _lines;
    readonly string _newline;

    IniText(List<string> lines, string newline)
    {
        _lines = lines;
        _newline = newline;
    }

    public static IniText Load(string path)
    {
        var text = File.Exists(path) ? Latin1.GetString(File.ReadAllBytes(path)) : "";
        var newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        var lines = text.Split(newline).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return new IniText(lines, newline);
    }

    public string? Get(string section, string key)
    {
        var (_, line) = this.Find(section, key);
        return line < 0 ? null : _lines[line][(_lines[line].IndexOf('=') + 1)..].Trim();
    }

    public void Set(string section, string key, string value)
    {
        var (header, line) = this.Find(section, key);
        if (line >= 0)
        {
            var old = _lines[line];
            _lines[line] = old[..(old.IndexOf('=') + 1)] + value;
        }
        else if (header >= 0)
        {
            var end = header + 1;
            while (end < _lines.Count && !IsHeader(_lines[end]))
                end++;
            while (end > header + 1 && _lines[end - 1].Trim().Length == 0)
                end--;
            _lines.Insert(end, $"{key}={value}");
        }
        else
        {
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, Latin1.GetBytes(string.Join(_newline, _lines) + _newline));
        File.Move(temp, path, overwrite: true);
    }

    (int Header, int Line) Find(string section, string key)
    {
        var header = -1;
        var inSection = false;
        for (var i = 0; i < _lines.Count; i++)
        {
            var trimmed = _lines[i].Trim();
            if (IsHeader(trimmed))
            {
                inSection = string.Equals(trimmed[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
                if (inSection && header < 0)
                    header = i;
                continue;
            }
            var eq = trimmed.IndexOf('=');
            if (inSection && eq > 0 && string.Equals(trimmed[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return (header, i);
        }
        return (header, -1);
    }

    static bool IsHeader(string line)
    {
        var t = line.Trim();
        return t.Length > 1 && t[0] == '[' && t[^1] == ']';
    }
}
