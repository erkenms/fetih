namespace Comet.Web.Features.Changes;

using System.Diagnostics;
using System.Text;

// Runs read-only git inspection and scoped checkout on Comet.Game/Data.
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

    private static async Task<(bool Success, string Stdout)> RunGitAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        if (!Directory.Exists(workingDirectory))
            return (false, "");

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return (false, "");

            string stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode == 0, stdout);
        }
        catch
        {
            return (false, "");
        }
    }
}
