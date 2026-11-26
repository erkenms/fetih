namespace Comet.Web.Features.NpcDialogs;

using System.Text.RegularExpressions;
using Comet.Game.Npcs;
using Comet.Game.Packets;
using static Comet.Game.Packets.MsgTaskDialog;

// What a dialog shows, two ways. NPCs driven by data (teleporters, talkers, the default answer)
// run the game's own code on a preview talk, which records the first page. Scripted NPCs can't
// run without a player, so their method's source is read instead: every Text, Choice and
// TextBox in order, one page per ShowAsync, with option numbers and faces named by constants
// resolved, then the same for each method in the file it hands the talk to. Branches and loops
// aren't evaluated, and text built at run time keeps its {holes}.
public static partial class NpcScriptExtractor
{
    public static NpcDialogPage[] FromPreview(NpcTalk talk) =>
        talk.Shown.Select(page => new NpcDialogPage(
            page.Parts.Where(p => p.Action == DialogAction.Text).Select(p => string.Concat(p.Strings)).ToArray(),
            page.Parts.Where(p => p.Action is DialogAction.Option or DialogAction.Input)
                .Select(p => new NpcDialogOption(string.Concat(p.Strings), p.Option, OptionKind(p.Action, p.Option)))
                .ToArray(),
            page.Face)).ToArray();

    private static string OptionKind(DialogAction action, byte option) =>
        action == DialogAction.Input ? "textbox" : option == NoOption ? "close" : "choice";

    // The source file declaring the class, under the game's source folder
    public static string? FindSourceFile(string? gameSourceDir, string typeName)
    {
        if (string.IsNullOrEmpty(gameSourceDir) || !Directory.Exists(gameSourceDir))
            return null;
        var declaration = new Regex($@"\bclass\s+{Regex.Escape(typeName)}\b");
        return Directory.EnumerateFiles(gameSourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .FirstOrDefault(f => declaration.IsMatch(File.ReadAllText(f)));
    }

    // `constant` gives the value of a name the script uses for an option or face
    public static NpcDialogPage[] FromSource(string source, string methodName, Func<string, int?> constant)
    {
        var bodies = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([methodName]);
        while (queue.TryDequeue(out var name))
        {
            if (!seen.Add(name) || MethodBody(source, name) is not { } found)
                continue;
            bodies.Add(found);
            foreach (Match call in HelperCallRegex().Matches(found))
                queue.Enqueue(call.Groups[1].Value);
        }
        string body = string.Join('\n', bodies);

        var pages = new List<NpcDialogPage>();
        var lines = new List<string>();
        var options = new List<NpcDialogOption>();
        int face = 0;
        foreach (Match m in CallRegex().Matches(body))
        {
            string args = m.Groups["args"].Value;
            switch (m.Groups["call"].Value)
            {
                case "Text":
                    if (Text(args) is { } text)
                        lines.Add(text);
                    break;
                case "Choice":
                case "TextBox":
                    if (Text(FirstArgument(args)) is { } label)
                    {
                        byte? option = Number(SecondArgument(args), constant) is int n and >= 0 and <= 255 ? (byte)n : null;
                        options.Add(new NpcDialogOption(label, option,
                            m.Groups["call"].Value == "TextBox" ? "textbox" : option == NoOption ? "close" : "choice"));
                    }
                    break;
                case "Face":
                    face = Number(args, constant) ?? face;
                    break;
                case "ShowAsync":
                    if (lines.Count > 0 || options.Count > 0)
                        pages.Add(new NpcDialogPage([.. lines], [.. options], face));
                    lines.Clear();
                    options.Clear();
                    break;
            }
        }
        return [.. pages];
    }

    // The text between the braces (or after =>) of the first method with that name
    internal static string? MethodBody(string source, string methodName)
    {
        var start = Regex.Match(source, $@"\b{Regex.Escape(methodName)}\s*\([^)]*\)\s*(?<open>\{{|=>)");
        if (!start.Success)
            return null;
        int i = start.Groups["open"].Index;
        if (source[i] == '=')
        {
            int end = source.IndexOf(";\n", i, StringComparison.Ordinal);
            return end < 0 ? source[i..] : source[i..end];
        }
        int depth = 0;
        for (int j = i; j < source.Length; j++)
        {
            if (source[j] == '{')
                depth++;
            else if (source[j] == '}' && --depth == 0)
                return source[i..(j + 1)];
        }
        return null;
    }

    // A number, or a constant like Leave or MsgTaskDialog.NoOption
    internal static int? Number(string expression, Func<string, int?> constant)
    {
        expression = expression.Trim();
        if (int.TryParse(expression, out int n))
            return n;
        if (expression.EndsWith("NoOption", StringComparison.Ordinal))
            return NoOption;
        return constant(expression[(expression.LastIndexOf('.') + 1)..]);
    }

    // The text of string literals joined with +: an interpolated literal keeps its {holes},
    // and any other part shows as {expression}. Null when no part is a literal.
    internal static string? Text(string expression)
    {
        var parts = Split(expression, '+').Where(p => p.Length > 0);
        var text = new List<string>();
        bool literal = false;
        foreach (var part in parts)
        {
            var m = StringRegex().Match(part);
            if (m.Success && m.Length == part.Length)
            {
                text.Add(Regex.Unescape(m.Groups[1].Value));
                literal = true;
            }
            else
                text.Add($"{{{part}}}");
        }
        return literal ? string.Concat(text) : null;
    }

    private static string FirstArgument(string args) => Split(args, ',').FirstOrDefault() ?? "";

    private static string SecondArgument(string args) => Split(args, ',').Skip(1).FirstOrDefault() ?? "";

    // Split at the separator outside strings and brackets
    private static IEnumerable<string> Split(string args, char separator)
    {
        int depth = 0, from = 0;
        bool quoted = false;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c == '"' && (i == 0 || args[i - 1] != '\\'))
                quoted = !quoted;
            else if (!quoted && c is '(' or '[')
                depth++;
            else if (!quoted && c is ')' or ']')
                depth--;
            else if (!quoted && depth == 0 && c == separator)
            {
                yield return args[from..i].Trim();
                from = i + 1;
            }
        }
        yield return args[from..].Trim();
    }

    [GeneratedRegex(@"\.(?<call>Text|Choice|TextBox|Face|ShowAsync)\s*\((?<args>(?:""(?:\\.|[^""\\])*""|[^()""]|\((?:[^()]|\([^()]*\))*\))*)\)")]
    private static partial Regex CallRegex();

    // A call that hands the conversation on: SomeAsync(talk, ...)
    [GeneratedRegex(@"(?<![.\w])([A-Z]\w*)\s*\(\s*talk\b")]
    private static partial Regex HelperCallRegex();

    [GeneratedRegex(@"^\$?""((?:\\.|[^""\\])*)""$")]
    private static partial Regex StringRegex();
}
