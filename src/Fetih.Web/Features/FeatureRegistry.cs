namespace Fetih.Web.Features;

public static class FeatureRegistry
{
    public static readonly IFeature[] All =
    [
        new Health.HealthFeature(),
        new Audit.AuditFeature(),
        new Items.ItemsFeature(),
        new Catalogs.CatalogsFeature(),
        new PublicAccount.PublicAccountFeature(),
        new PublicSite.PublicSiteFeature(),
        new PublicHeatmap.PublicHeatmapFeature(),
        new Dashboard.DashboardFeature(),
        new Economy.EconomyFeature(),
        new Broadcasts.BroadcastsFeature(),
        new Logs.LogsFeature(),
        new ItemHistory.ItemHistoryFeature(),
        new Changes.ChangesFeature(),
        new Social.SocialFeature(),
        new Pk.PkFeature(),
        new Maps.MapsFeature(),
        new NpcDialogs.NpcDialogsFeature(),
        new MapEdit.MapEditFeature(),
        new Accounts.AccountsFeature(),
        new Characters.CharactersFeature(),
        new Players.PlayersFeature(),
        new MonsterEdit.MonsterEditFeature(),
        new DataEdit.DataEditFeature(),
        new Integrity.IntegrityFeature(),
        new Chat.ChatFeature(),
        new Live.LiveFeature(),
        new Looks.LooksFeature(),
        new Server.ServerFeature(),
    ];
}
