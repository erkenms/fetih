using System.Text.Json.Serialization;
using Fetih.Core.Logging;
using Fetih.Web;
using Fetih.Web.Accounts;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features;
using Fetih.Web.Game;
using Fetih.Web.Pages;
using Fetih.Web.Pages.Shared;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Fetih.Web.config"), optional: false)
    .AddEnvironmentVariables("FETIH_WEB_")
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

Fetih.Account.Database.ServerDbContext.Configuration = new()
{
    Hostname = config.AccountDatabase.Hostname, Schema = config.AccountDatabase.Schema,
    Username = config.AccountDatabase.Username, Password = config.AccountDatabase.Password,
};
Fetih.Game.Database.ServerDbContext.Configuration = new()
{
    Hostname = config.Database.Hostname, Schema = config.Database.Schema,
    Username = config.Database.Username, Password = config.Database.Password,
};

builder.WebHost.UseUrls(config.Web.Urls);
// Serves wwwroot from the project folder when run from a build; a publish copies it instead
builder.WebHost.UseStaticWebAssets();
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
builder.Services.AddSingleton<ClientFiles>();
builder.Services.AddSingleton<GameRpc>();
builder.Services.AddHostedService(services => services.GetRequiredService<GameRpc>());
builder.Services.AddWebAuth(config);
builder.Services.AddSingleton<SignIn>();
builder.Services.AddSingleton<PickerSearch>();
// Pages keep Turkish letters as they are instead of &#x…; escapes
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
    options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(
        System.Text.Unicode.UnicodeRanges.All));
builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);
builder.Services.AddRazorPages(options =>
        options.Conventions.AuthorizeFolder("/Admin", Permissions.Policy(Permission.AdminArea)))
    .AddMvcOptions(options => options.Filters.Add<PageChecks>());
foreach (var feature in FeatureRegistry.All)
    feature.AddServices(builder.Services, config);

if (config.Web.ForwardedHeaders)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

var app = builder.Build();
if (config.Web.ForwardedHeaders)
    app.UseForwardedHeaders();

static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments("/api");

app.UseWhen(IsApi, api => api.UseExceptionHandler(error => error.Run(async context =>
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
})));

// Pages: errors and empty 403/404/503 answers render Pages/Hata
app.UseWhen(context => !IsApi(context), pages =>
{
    pages.UseExceptionHandler("/hata/500");
    pages.UseStatusCodePagesWithReExecute("/hata/{0}");
});

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

app.UseStaticFiles();
// After the error pages, so a re-executed /hata/… request is routed again
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

var api = app.MapGroup("/api");
api.MapAuth();
app.MapAuthCheck();
foreach (var feature in FeatureRegistry.All)
{
    feature.Map(api);
    feature.MapRoot(app);
}
api.MapFallback(() => Results.NotFound(new ApiError("not_found")));
app.MapRazorPages();

if (frontendFiles != null)
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = frontendFiles });

LogEvents.For<WebConfiguration>(WebEvents.Start).Information("Web listening on {Urls}", config.Web.Urls);
await app.RunAsync();
return 0;

public partial class Program;
