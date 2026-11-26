namespace Fetih.Web.Tests;

using System.Net;
using Fetih.Web.Audit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class AuthTests(WebFixture fixture)
{
    private HttpClient Client() =>
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task UsernameIsCaseInsensitive()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email.ToUpperInvariant(), "secret1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task BannedAccountIsRefused()
    {
        string email = await fixture.CreateAccountAsync(authority: 5, status: 5);
        var response = await WebFixture.PostFormAsync(this.Client(), "/giris",
            new() { ["kullanici"] = email, ["sifre"] = "secret1" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("yasaklı", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AuditedActionIsPersisted()
    {
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
            await audit.WriteAsync("test.action", "character", "1000001",
                before: new { level = 1 }, after: new { level = 2 }, summary: "level 1 to 2");
        }

        await using var db = await fixture.Factory.Services
            .GetRequiredService<IDbContextFactory<WebDbContext>>()
            .CreateDbContextAsync();
        var entry = await db.Audit
            .OrderByDescending(x => x.ID)
            .FirstOrDefaultAsync(x => x.Action == "test.action" && x.TargetID == "1000001");
        Assert.NotNull(entry);
        Assert.Equal("{\"level\":2}", entry.After);
    }

    [Fact]
    public async Task AuthCheckGatesGrafanaToStaffWithViewLogs()
    {
        var anon = this.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/auth/check")).StatusCode);

        var forwarded = new HttpRequestMessage(HttpMethod.Get, "/auth/check");
        forwarded.Headers.Add("X-Forwarded-Uri", "/grafana/d/fetih-overview/overview");
        var redirect = await anon.SendAsync(forwarded);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/giris?donus=%2Fgrafana%2Fd%2Ffetih-overview%2Foverview", redirect.Headers.Location?.ToString());

        var player = await fixture.SignInAsync("test", "test");
        Assert.Equal(HttpStatusCode.Unauthorized, (await player.GetAsync("/auth/check")).StatusCode);

        string modEmail = await fixture.CreateAccountAsync(authority: 2);
        var mod = await fixture.SignInAsync(modEmail, "secret1");
        var ok = await mod.GetAsync("/auth/check");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(modEmail, Assert.Single(ok.Headers.GetValues("X-Webauth-User")));
    }
}
