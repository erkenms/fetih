namespace Fetih.Web.Pages.Admin.Players;

using System.Globalization;
using Fetih.Web.Auth;
using Fetih.Web.Features.Players;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.AdminArea)]
public class IndexModel(PlayerService players) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ip { get; set; }
    [BindProperty(SupportsGet = true)] public ushort? Status { get; set; }
    [BindProperty(SupportsGet = true)] public ushort? Authority { get; set; }
    [BindProperty(SupportsGet = true)] public bool Online { get; set; }
    [BindProperty(SupportsGet = true)] public bool Jailed { get; set; }
    [BindProperty(SupportsGet = true)] public byte? ClassLine { get; set; }
    [BindProperty(SupportsGet = true)] public uint? MapId { get; set; }
    [BindProperty(SupportsGet = true)] public byte? MinLevel { get; set; }
    [BindProperty(SupportsGet = true)] public byte? MaxLevel { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "sayfa")] public int Sayfa { get; set; } = 1;

    public PlayerListResponse Result { get; private set; } = new([], 0, 1, PageSize, false);

    public int Pages => Math.Max(1, (int)Math.Ceiling((double)this.Result.Total / PageSize));

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(this.Q) || !string.IsNullOrWhiteSpace(this.Ip) ||
        this.Status.HasValue || this.Authority.HasValue || this.Online || this.Jailed ||
        this.ClassLine.HasValue || this.MapId.HasValue || this.MinLevel.HasValue ||
        this.MaxLevel.HasValue || !string.IsNullOrWhiteSpace(this.Sort);

    public async Task OnGetAsync(CancellationToken ct) => await this.LoadAsync(ct);

    public async Task<IActionResult> OnGetListAsync(CancellationToken ct)
    {
        this.Sayfa = 1;
        await this.LoadAsync(ct);
        this.Response.Headers["HX-Replace-Url"] = this.CleanUrl();
        return this.Partial("_List", this);
    }

    [Requires(Permission.Moderate)]
    public Task<IActionResult> OnPostBulkKickAsync(uint[]? accountIds, string? reason, CancellationToken ct) =>
        this.RunBulkAsync("kick", "oyundan atıldı", accountIds, reason, ct);

    [Requires(Permission.ManageCharacters)]
    public Task<IActionResult> OnPostBulkBanAsync(uint[]? accountIds, string? reason, CancellationToken ct) =>
        this.RunBulkAsync("ban", "yasaklandı", accountIds, reason, ct);

    private async Task<IActionResult> RunBulkAsync(
        string action, string verb, uint[]? accountIds, string? reason, CancellationToken ct)
    {
        if (accountIds == null || accountIds.Length == 0)
        {
            this.FlashError("En az bir oyuncu seç.");
            return this.Redirect(this.CleanUrl());
        }

        var result = await players.BulkAsync(action, accountIds, reason, this.User, ct);
        if (result.Failures.Length == 0)
        {
            this.Flash($"{result.Succeeded} oyuncu {verb}.");
        }
        else
        {
            var details = string.Join("; ", result.Failures.Take(5)
                .Select(f => $"{f.Name ?? $"#{f.AccountId}"}: {PlayerText.Error(f.Error)}"));
            string extra = result.Failures.Length > 5 ? $" (+{result.Failures.Length - 5} diğer)" : "";
            this.FlashError($"{result.Succeeded} başarılı, {result.Failures.Length} başarısız: {details}{extra}");
        }

        return this.Redirect(this.CleanUrl());
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var query = new PlayerQuery(
            this.Q, this.Ip, this.Status, this.Authority, this.Online, this.Jailed,
            this.ClassLine, this.MapId, this.MinLevel, this.MaxLevel, this.Sort);
        this.Result = await players.ListAsync(query, Math.Max(1, this.Sayfa), PageSize, ct);
    }

    public string CleanUrl()
    {
        var qb = new QueryBuilder();
        if (!string.IsNullOrWhiteSpace(this.Q)) qb.Add("q", this.Q.Trim());
        if (!string.IsNullOrWhiteSpace(this.Ip)) qb.Add("ip", this.Ip.Trim());
        if (this.Status.HasValue) qb.Add("status", this.Status.Value.ToString(CultureInfo.InvariantCulture));
        if (this.Authority.HasValue) qb.Add("authority", this.Authority.Value.ToString(CultureInfo.InvariantCulture));
        if (this.Online) qb.Add("online", "true");
        if (this.Jailed) qb.Add("jailed", "true");
        if (this.ClassLine.HasValue) qb.Add("classLine", this.ClassLine.Value.ToString(CultureInfo.InvariantCulture));
        if (this.MapId.HasValue) qb.Add("mapId", this.MapId.Value.ToString(CultureInfo.InvariantCulture));
        if (this.MinLevel.HasValue) qb.Add("minLevel", this.MinLevel.Value.ToString(CultureInfo.InvariantCulture));
        if (this.MaxLevel.HasValue) qb.Add("maxLevel", this.MaxLevel.Value.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(this.Sort)) qb.Add("sort", this.Sort);
        if (this.Sayfa > 1) qb.Add("sayfa", this.Sayfa.ToString(CultureInfo.InvariantCulture));
        return "/admin/players" + qb.ToQueryString();
    }
}
