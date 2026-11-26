namespace Fetih.Web.Features.PublicSite;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

public sealed class GitNewsService(IMemoryCache cache, WebConfiguration config)
{
    private const string CacheKey = "public:news";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private const int MaxCommits = 100;

    public async Task<NewsEntry[]> GetNewsAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out NewsEntry[]? cached) && cached != null)
            return cached;

        var entries = await ReadGitLogAsync(ct);
        cache.Set(CacheKey, entries, CacheDuration);
        return entries;
    }

    private async Task<NewsEntry[]> ReadGitLogAsync(CancellationToken ct)
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return [];

        var (ok, output) = await GitProcess.RunAsync(
            repoRoot,
            ["log", "-n", MaxCommits.ToString(CultureInfo.InvariantCulture), "--format=%H%x1f%h%x1f%aI%x1f%s%x1f%b%x1e"],
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
        return ok ? ParseRecords(output) : [];
    }

    private static NewsEntry[] ParseRecords(string output)
    {
        var entries = new List<NewsEntry>();
        foreach (var raw in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var record = raw.TrimStart('\r', '\n');
            if (string.IsNullOrEmpty(record))
                continue;
            var parts = record.Split('\x1f');
            if (parts.Length < 4)
                continue;
            if (!DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                continue;
            string? body = parts.Length > 4 ? parts[4].Trim() : null;
            if (string.IsNullOrEmpty(body))
                body = null;
            entries.Add(new NewsEntry(parts[0], parts[1], dto.UtcDateTime, parts[3], body));
        }
        return [.. entries];
    }

    private string? FindRepoRoot()
    {
        string?[] starts =
        [
            WebConfiguration.PathsSection.Resolve(config.Paths.DataDirectory),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        ];

        foreach (var start in starts)
        {
            if (string.IsNullOrEmpty(start))
                continue;
            string? dir = File.Exists(start) ? Path.GetDirectoryName(start) : start;
            while (!string.IsNullOrEmpty(dir))
            {
                string gitPath = Path.Combine(dir, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        return null;
    }
}
