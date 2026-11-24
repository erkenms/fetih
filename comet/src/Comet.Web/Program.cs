using System.Text.Json.Serialization;
using Comet.Core.Logging;
using Comet.Web;
using Comet.Web.Accounts;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Features;
using Comet.Web.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Comet.Web.config"), optional: false)
    .AddEnvironmentVariables("COMET_WEB_")
    .AddCommandLine(args);

var config = builder.Configuration.Get<WebConfiguration>() ?? new WebConfiguration();
LogSetup.Configure("web", config.Logging);
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);
if (!config.Valid)
{
    Log.Error("Invalid web configuration: Database and AccountDatabase are required");
    Log.CloseAndFlush();
    return 1;
}

Comet.Account.Database.ServerDbContext.Configuration = new()
{
    Hostname = config.AccountDatabase.Hostname, Schema = config.AccountDatabase.Schema,
    Username = config.AccountDatabase.Username, Password = config.AccountDatabase.Password,
};
Comet.Game.Database.ServerDbContext.Configuration = new()
{
    Hostname = config.Database.Hostname, Schema = config.Database.Schema,
    Username = config.Database.Username, Password = config.Database.Password,
};

builder.WebHost.UseUrls(config.Web.Urls);
builder.Services.AddSingleton(config);
builder.Services.AddHttpContextAccessor();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddDbContextFactory<WebDbContext>(options => options.UseMySql(
    config.Database.ConnectionString, MySqlServerVersion.LatestSupportedServerVersion));
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<GameRpc>();
builder.Services.AddHostedService(services => services.GetRequiredService<GameRpc>());
builder.Services.AddWebAuth(config);
foreach (var feature in FeatureRegistry.All)
    feature.AddServices(builder.Services, config);

var app = builder.Build();

app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is GameOfflineException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new ApiError("game_offline"));
        return;
    }

    LogEvents.For<WebConfiguration>(WebEvents.Error).Error(exception,
        "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiError("server_error"));
}));

// Client art, built by tools/assets into web/assets. Pages show placeholders without it.
string? assets = WebConfiguration.PathsSection.Resolve(config.Paths.AssetsDirectory);
if (assets != null)
{
    Directory.CreateDirectory(assets);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(assets),
        RequestPath = "/assets",
    });
}

// The built frontend (npm run build). In development Vite serves it and proxies /api here.
string? frontend = WebConfiguration.PathsSection.Resolve(config.Paths.FrontendDirectory);
var frontendFiles = frontend != null && File.Exists(Path.Combine(frontend, "index.html"))
    ? new PhysicalFileProvider(frontend) : null;
if (frontendFiles != null)
    app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendFiles });

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

var api = app.MapGroup("/api");
api.MapAuth();
foreach (var feature in FeatureRegistry.All)
    feature.Map(api);
api.MapFallback(() => Results.NotFound(new ApiError("not_found")));

if (frontendFiles != null)
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = frontendFiles });

LogEvents.For<WebConfiguration>(WebEvents.Start).Information("Web listening on {Urls}", config.Web.Urls);
await app.RunAsync();
return 0;

public partial class Program;
