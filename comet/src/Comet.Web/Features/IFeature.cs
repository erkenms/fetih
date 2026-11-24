namespace Comet.Web.Features;

// A feature is one folder under Features/ with a class implementing this, listed once in
// FeatureRegistry. Its endpoints hang under /api.
public interface IFeature
{
    void AddServices(IServiceCollection services, WebConfiguration config) { }

    void Map(RouteGroupBuilder api);
}
