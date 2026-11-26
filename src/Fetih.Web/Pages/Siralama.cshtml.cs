namespace Fetih.Web.Pages;

using Fetih.Web.Features.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class SiralamaModel(PublicSiteService site) : PageModel
{
    public const int PageSize = PublicSiteService.DefaultPageSize;

    public static readonly (string Slug, string Title)[] Kinds =
    [
        ("seviye", "Seviye"),
        ("soyluluk", "Soyluluk"),
        ("lonca", "Loncalar"),
        ("arena", "Arena"),
        ("cicek", "Çiçekler"),
        ("pk", "PK Puanı"),
        ("aile", "Klanlar"),
    ];

    public static readonly (string Slug, string ApiKey, string Label)[] FlowerKinds =
    [
        ("gul", "redRose", "Kırmızı Gül"),
        ("zambak", "lily", "Zambak"),
        ("orkide", "orchid", "Orkide"),
        ("lale", "tulip", "Lale"),
        ("toplam", "total", "Toplam"),
    ];

    [BindProperty(SupportsGet = true)]
    public int Sayfa { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int? Sinif { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sirala { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Mod { get; set; }

    [BindProperty(SupportsGet = true)]
    public uint? Sezon { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cicek { get; set; }

    public string Kind { get; private set; } = "seviye";
    public string KindTitle { get; private set; } = "Seviye";
    public int Total { get; private set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(this.Total / (double)PageSize));
    public GuildWarSummary? GuildWar { get; private set; }

    public RankingPage<LevelRankEntry>? Level { get; private set; }
    public RankingPage<NobilityRankEntry>? Nobility { get; private set; }
    public RankingPage<GuildRankEntry>? Guilds { get; private set; }
    public RankingPage<ArenaRankEntry>? Arena { get; private set; }
    public ArenaSeasonInfo[] ArenaSeasons { get; private set; } = [];
    public RankingPage<ArenaSeasonRankEntry>? ArenaSeason { get; private set; }
    public RankingPage<FlowerRankEntry>? Flowers { get; private set; }
    public RankingPage<PkRankEntry>? Pk { get; private set; }
    public RankingPage<FamilyRankEntry>? Families { get; private set; }

    public string GuildSort => this.Sirala == "uye" ? "uye" : "fon";
    public string ArenaMode => this.Mod is "onur" or "sezon" ? this.Mod : "puan";
    public string FlowerSlug => FlowerKinds.Any(f => f.Slug == this.Cicek) ? this.Cicek! : "gul";

    public async Task OnGetAsync(string? tur, CancellationToken ct)
    {
        this.Sayfa = Math.Max(1, this.Sayfa);
        var match = Kinds.FirstOrDefault(k => k.Slug == tur?.ToLowerInvariant());
        this.Kind = match.Slug ?? "seviye";
        this.KindTitle = match.Title ?? "Seviye";

        this.GuildWar = (await site.GetHomeAsync(ct)).GuildWar;

        switch (this.Kind)
        {
            case "soyluluk":
                this.Nobility = await site.GetNobilityRankingAsync(this.Sayfa, PageSize);
                this.Total = this.Nobility.Total;
                break;

            case "lonca":
                string sort = this.GuildSort == "uye" ? "members" : "fund";
                this.Guilds = await site.GetGuildRankingAsync(sort, this.Sayfa, PageSize);
                this.Total = this.Guilds.Total;
                break;

            case "arena":
                this.ArenaSeasons = await site.GetArenaSeasonsAsync();
                if (this.ArenaMode == "sezon")
                {
                    uint? target = this.Sezon ?? this.ArenaSeasons.FirstOrDefault()?.Season;
                    this.Sezon = target;
                    if (target != null)
                    {
                        this.ArenaSeason = await site.GetArenaSeasonRankingAsync(target.Value, this.Sayfa, PageSize);
                        this.Total = this.ArenaSeason.Total;
                    }
                }
                else
                {
                    string arenaKind = this.ArenaMode == "onur" ? "honor" : "points";
                    this.Arena = await site.GetArenaRankingAsync(arenaKind, this.Sayfa, PageSize);
                    this.Total = this.Arena.Total;
                }
                break;

            case "cicek":
                string flowerKey = FlowerKinds.First(f => f.Slug == this.FlowerSlug).ApiKey;
                this.Flowers = await site.GetFlowerRankingAsync(flowerKey, this.Sayfa, PageSize);
                this.Total = this.Flowers.Total;
                break;

            case "pk":
                this.Pk = await site.GetPkRankingAsync(this.Sayfa, PageSize);
                this.Total = this.Pk.Total;
                break;

            case "aile":
                this.Families = await site.GetFamilyRankingAsync(this.Sayfa, PageSize);
                this.Total = this.Families.Total;
                break;

            default:
                this.Level = await site.GetLevelRankingAsync(this.Sinif, this.Sayfa, PageSize);
                this.Total = this.Level.Total;
                break;
        }
    }
}
