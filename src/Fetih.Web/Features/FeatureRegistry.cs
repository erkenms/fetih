namespace Fetih.Web.Features;

public static class FeatureRegistry
{
    public static readonly IFeature[] All =
    [
        new Health.HealthFeature(),
        new Items.ItemsFeature(),
        new Catalogs.CatalogsFeature(),
        new PublicAccount.PublicAccountFeature(),
        new PublicSite.PublicSiteFeature(),
        new Broadcasts.BroadcastsFeature(),
        new Changes.ChangesFeature(),
        new Maps.MapsFeature(),
        new NpcDialogs.NpcDialogsFeature(),
        new MapEdit.MapEditFeature(),
        new Players.PlayersFeature(),
        new MonsterEdit.MonsterEditFeature(),
        new DataEdit.DataEditFeature(),
        new Looks.LooksFeature(),
        new Server.ServerFeature(),
    ];
}
