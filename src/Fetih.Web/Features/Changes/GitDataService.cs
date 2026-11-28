namespace Fetih.Web.Features.Changes;

using System.Diagnostics;
using System.Text;

// Runs read-only git inspection and scoped checkout on Fetih.Game/Data.
public sealed class GitDataService
{
    public async Task<(bool GitAvailable, Dictionary<string, string> StatusByFile)> GetStatusAsync(
        string dataDir, CancellationToken ct = default)
    {
        var result = await RunGitAsync(dataDir, ct, "status", "--porcelain=v1", "-uall", "--", ".");
        if (!result.Success)
            return (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        return (true, ParseStatusPorcelain(result.Stdout));
    }

    public async Task<string> GetDiffAsync(
        string dataDir, string fileName, string gitStatus, CancellationToken ct = default)
    {
        if (gitStatus == "untracked")
        {
            string path = Path.Combine(dataDir, fileName);
            if (!File.Exists(path))
                return "";
            string content = await File.ReadAllTextAsync(path, ct);
            var lines = content.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            sb.AppendLine($"--- /dev/null");
            sb.AppendLine($"+++ b/{fileName}");
            sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
            foreach (var line in lines)
                sb.AppendLine($"+{line}");
            return sb.ToString();
        }

        // Diff against HEAD so both staged and unstaged edits are shown.
        var headDiff = await RunGitAsync(dataDir, ct, "diff", "--no-color", "HEAD", "--", fileName);
        if (headDiff.Success)
            return headDiff.Stdout;

        var workingDiff = await RunGitAsync(dataDir, ct, "diff", "--no-color", "--", fileName);
        return workingDiff.Success ? workingDiff.Stdout : "";
    }

    public async Task<bool> DiscardAsync(string dataDir, string fileName, CancellationToken ct = default)
    {
        var result = await RunGitAsync(dataDir, ct, "checkout", "--", fileName);
        return result.Success;
    }

    public async Task<string> BuildApplyCommandAsync(
        string dataDir,
        IReadOnlyList<(string File, string GitStatus)> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            return "";

        var patch = new StringBuilder();
        var tracked = files.Where(f => f.GitStatus != "untracked").Select(f => f.File).ToArray();
        if (tracked.Length > 0)
        {
            var args = new List<string> { "diff", "--no-color", "HEAD", "--" };
            args.AddRange(tracked);
            var res = await RunGitAsync(dataDir, ct, [.. args]);
            if (res.Success && !string.IsNullOrEmpty(res.Stdout))
            {
                patch.Append(res.Stdout);
                if (!res.Stdout.EndsWith('\n'))
                    patch.Append('\n');
            }
        }

        foreach (var (file, status) in files)
        {
            if (status != "untracked")
                continue;
            string path = Path.Combine(dataDir, file);
            if (!File.Exists(path))
                continue;

            string repoPath = $"src/Fetih.Game/Data/{file}";
            string content = (await File.ReadAllTextAsync(path, ct)).Replace("\r\n", "\n");
            bool endsWithNewline = content.EndsWith('\n');
            if (endsWithNewline)
                content = content[..^1];
            var lines = content.Length == 0 ? [] : content.Split('\n');

            patch.AppendLine($"diff --git a/{repoPath} b/{repoPath}");
            patch.AppendLine("new file mode 100644");
            patch.AppendLine("--- /dev/null");
            patch.AppendLine($"+++ b/{repoPath}");
            if (lines.Length > 0)
            {
                patch.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
                foreach (var line in lines)
                    patch.AppendLine($"+{line}");
                if (!endsWithNewline)
                    patch.AppendLine("\\ No newline at end of file");
            }
        }

        return FormatApplyCommand(patch.ToString(), [.. files.Select(f => f.File)]);
    }

    public static string FormatApplyCommand(string patch, IReadOnlyList<string> fileNames)
    {
        if (string.IsNullOrWhiteSpace(patch))
            return "";

        string delimiter = "FETIH_PATCH";
        int suffix = 1;
        while (patch.Contains(delimiter, StringComparison.Ordinal))
            delimiter = $"FETIH_PATCH_{suffix++}";

        if (!patch.EndsWith('\n'))
            patch += "\n";

        string summary = string.Join(", ", fileNames)
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");

        return $"git apply --3way <<'{delimiter}'\n{patch}{delimiter}\ngit add src/Fetih.Game/Data && git commit -m \"Studio: {summary}\"";
    }

    // `git status --porcelain=v1 -uall -- .` run with WorkingDirectory = dataDir still reports
    // paths relative to the repo root, so take the file name (Data/ has no subdirectories).
    public static Dictionary<string, string> ParseStatusPorcelain(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(output))
            return map;

        foreach (var raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length < 4)
                continue;

            string code = line[..2];
            string pathPart = line[3..].Trim();
            if (pathPart.StartsWith('"') && pathPart.EndsWith('"') && pathPart.Length >= 2)
                pathPart = pathPart[1..^1];

            // Renamed entries look like "old -> new"
            int arrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                pathPart = pathPart[(arrow + 4)..];

            string fileName = Path.GetFileName(pathPart.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName))
                continue;

            string status = code switch
            {
                "??" => "untracked",
                _ when code.Contains('D') => "deleted",
                _ when code.Contains('A') => "added",
                _ when code.Contains('R') => "renamed",
                _ when code.Contains('M') => "modified",
                _ => "modified",
            };
            map[fileName] = status;
        }
        return map;
    }

    public static (int Added, int Removed) CountDiffLines(string diff)
    {
        if (string.IsNullOrEmpty(diff))
            return (0, 0);

        int added = 0;
        int removed = 0;
        foreach (var raw in diff.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith('+') && !line.StartsWith("+++"))
                added++;
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                removed++;
        }
        return (added, removed);
    }

    private static Task<(bool Success, string Stdout)> RunGitAsync(
        string workingDirectory, CancellationToken ct, params string[] args) =>
        GitProcess.RunAsync(workingDirectory, args, ct: ct);
}
