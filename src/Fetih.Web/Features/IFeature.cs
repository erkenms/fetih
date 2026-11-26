namespace Fetih.Web.Features;

// A feature is one folder under Features/ with a class implementing this, listed once in FeatureRegistry.
public interface IFeature
{
    void AddServices(IServiceCollection services, WebConfiguration config) { }

    void MapRoot(IEndpointRouteBuilder app) { }
}
