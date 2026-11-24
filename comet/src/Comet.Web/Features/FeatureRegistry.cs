namespace Comet.Web.Features;

public static class FeatureRegistry
{
    public static readonly IFeature[] All =
    [
        new Health.HealthFeature(),
        new Audit.AuditFeature(),
        new Items.ItemsFeature(),
        new Catalogs.CatalogsFeature(),
    ];
}
