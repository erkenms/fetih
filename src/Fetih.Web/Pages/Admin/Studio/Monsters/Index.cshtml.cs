namespace Fetih.Web.Pages.Admin.Studio.Monsters;

using Fetih.Game.DataEditing;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features.Looks;
using Fetih.Web.Features.MonsterEdit;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class IndexModel(
    WebConfiguration config,
    MonsterDataService data,
    LookStore looks,
    GameRpc gameRpc,
    AuditLog audit) : PageModel
{
    public bool StudioReadOnly => config.Web.StudioReadOnly;
    public bool HasStudio => this.User.Has(Permission.Studio);
    public bool CanEdit => this.HasStudio && !this.StudioReadOnly;
    public bool GameConnected => gameRpc.Connected;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Boss { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "id";

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = "asc";

    public int TotalCount { get; private set; }
    public MonsterRow[] Rows { get; private set; } = [];
    public MonsterProblemDto[] Problems { get; private set; } = [];

    public sealed record MonsterRow(
        MonsterDto Monster,
        string? LookImage,
        int SpawnAreaCount,
        int TotalSpawnAmount,
        int BossScheduleCount,
        bool HasCustomDrops);

    public Task OnGetAsync(CancellationToken ct) =>
        data.WithLockAsync(dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            var monsters = file.All();
            var spawns = data.LoadSpawns(dataDir)
                .GroupBy(s => s.MonsterID)
                .ToDictionary(g => g.Key, g => (Areas: g.Count(), Total: g.Sum(x => x.Amount)));
            var bosses = data.LoadBosses(dataDir)
                .GroupBy(b => b.MonsterID)
                .ToDictionary(g => g.Key, g => g.Count());
            var drops = data.LoadDrops(dataDir)
                .Select(d => d.MonsterID)
                .ToHashSet();
            var lookImages = looks.Looks.Monster
                .ToDictionary(l => l.Look, l => l.Image);

            var all = new List<MonsterRow>(monsters.Count);
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = MonsterDataService.ToDto(monsters[i]);
                spawns.TryGetValue(m.Id, out var spawnInfo);
                bosses.TryGetValue(m.Id, out int bossCount);
                lookImages.TryGetValue(m.Lookface, out var image);
                all.Add(new MonsterRow(m, image, spawnInfo.Areas, spawnInfo.Total, bossCount, drops.Contains(m.Id)));
            }

            this.TotalCount = all.Count;

            IEnumerable<MonsterRow> filtered = all;
            string q = this.Q?.Trim() ?? "";
            if (q.Length > 0)
            {
                bool isId = uint.TryParse(q, out uint qId);
                filtered = filtered.Where(r =>
                    (isId && (r.Monster.Id == qId || r.Monster.Lookface == qId))
                    || r.Monster.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (this.Boss == "boss")
                filtered = filtered.Where(r => r.Monster.Boss);
            else if (this.Boss == "normal")
                filtered = filtered.Where(r => !r.Monster.Boss);

            bool desc = string.Equals(this.Dir, "desc", StringComparison.OrdinalIgnoreCase);
            filtered = (this.Sort?.ToLowerInvariant()) switch
            {
                "name" => desc ? filtered.OrderByDescending(r => r.Monster.Name, StringComparer.OrdinalIgnoreCase) : filtered.OrderBy(r => r.Monster.Name, StringComparer.OrdinalIgnoreCase),
                "level" => desc ? filtered.OrderByDescending(r => r.Monster.Level).ThenBy(r => r.Monster.Id) : filtered.OrderBy(r => r.Monster.Level).ThenBy(r => r.Monster.Id),
                "life" => desc ? filtered.OrderByDescending(r => r.Monster.Life).ThenBy(r => r.Monster.Id) : filtered.OrderBy(r => r.Monster.Life).ThenBy(r => r.Monster.Id),
                "attack" => desc ? filtered.OrderByDescending(r => r.Monster.AttackMax).ThenBy(r => r.Monster.Id) : filtered.OrderBy(r => r.Monster.AttackMax).ThenBy(r => r.Monster.Id),
                "defense" => desc ? filtered.OrderByDescending(r => r.Monster.Defense).ThenBy(r => r.Monster.Id) : filtered.OrderBy(r => r.Monster.Defense).ThenBy(r => r.Monster.Id),
                "spawns" => desc ? filtered.OrderByDescending(r => r.TotalSpawnAmount).ThenBy(r => r.Monster.Id) : filtered.OrderBy(r => r.TotalSpawnAmount).ThenBy(r => r.Monster.Id),
                _ => desc ? filtered.OrderByDescending(r => r.Monster.Id) : filtered.OrderBy(r => r.Monster.Id),
            };

            this.Rows = filtered.ToArray();
            if (this.HasStudio)
                this.Problems = data.ValidateMonsters(dataDir, file, checkDependents: false);

            return Task.FromResult(true);
        }, ct);

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostReloadAsync()
    {
        var result = await StudioData.ReloadFileAsync(DataFileKinds.Monsters.FileName, gameRpc, audit);
        if (result.Applied)
            this.Flash(string.IsNullOrWhiteSpace(result.Summary)
                ? "monsters.json yeniden yüklendi."
                : $"monsters.json: {result.Summary}");
        else
            this.FlashError($"monsters.json yüklenemedi: {string.Join("; ", result.Errors)}");

        return this.RedirectToPage();
    }

    public string SortUrl(string column)
    {
        string nextDir = string.Equals(this.Sort, column, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(this.Dir, "desc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(this.Q))
            parts.Add("q=" + Uri.EscapeDataString(this.Q));
        if (!string.IsNullOrWhiteSpace(this.Boss))
            parts.Add("boss=" + Uri.EscapeDataString(this.Boss));
        parts.Add("sort=" + column);
        parts.Add("dir=" + nextDir);
        return "/admin/studio/monsters?" + string.Join("&", parts);
    }

    public string SortArrow(string column)
    {
        if (!string.Equals(this.Sort, column, StringComparison.OrdinalIgnoreCase))
            return "";
        return string.Equals(this.Dir, "desc", StringComparison.OrdinalIgnoreCase) ? " ▼" : " ▲";
    }
}
