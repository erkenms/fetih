namespace Comet.Web.Features;

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
        new Dashboard.DashboardFeature(),
        new Broadcasts.BroadcastsFeature(),
        new Logs.LogsFeature(),
        new Changes.ChangesFeature(),
    ];
}
