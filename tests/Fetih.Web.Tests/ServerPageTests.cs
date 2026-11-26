namespace Fetih.Web.Tests;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[Collection(WebCollection.Name)]
public class ServerPageTests(WebFixture fixture)
{
    [Fact]
    public async Task SignedOutGoesToSignIn()
    {
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/server");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fserver", response.Headers.Location?.PathAndQuery);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task NonAdministratorsAreForbidden(ushort authority)
    {
        string email = await fixture.CreateAccountAsync(authority);
        var client = await fixture.SignInAsync(email, "secret1");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/server")).StatusCode);

        string page = await client.GetStringAsync("/giris");
        string token = Regex.Match(page,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        async Task<HttpStatusCode> PostAsync(string handler, Dictionary<string, string> extra = null)
        {
            var form = extra ?? new();
            form["__RequestVerificationToken"] = token;
            var resp = await client.PostAsync($"/admin/server?handler={handler}", new FormUrlEncodedContent(form));
            return resp.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("StartShutdown",
            new() { ["mode"] = "Restart", ["minutes"] = "5", ["reason"] = "Test" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("CancelShutdown"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("Maintenance",
            new() { ["enabled"] = "true", ["message"] = "Bakım" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("Motd",
            new() { ["text"] = "Merhaba" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("StartGuildWar"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("EndGuildWar"));
    }

    [Fact]
    public async Task AdminHomeShowsServerStatusForModerator()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");

        string home = await client.GetStringAsync("/admin");
        Assert.Contains("Sunucu Durumu", home);
        Assert.Contains("Oyun kapalı", home);
        Assert.Contains("Planlı kapatma yok.", home);
        Assert.Contains("href=\"/admin/board\"", home);
        Assert.DoesNotContain("Sunucu kontrolüne git", home);

        string partial = await client.GetStringAsync("/admin?handler=Status");
        Assert.DoesNotContain("<html", partial);
        Assert.Contains("Oyun kapalı", partial);
    }

    [Fact]
    public async Task AdministratorCanUpdateMaintenanceAndMotd()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.SignInAsync(email, "secret1");

        string home = await client.GetStringAsync("/admin");
        Assert.Contains("Sunucu kontrolüne git", home);

        string statusPartial = await client.GetStringAsync("/admin/server?handler=Status");
        Assert.DoesNotContain("<html", statusPartial);
        Assert.Contains("Oyun kapalı", statusPartial);

        var maint = await WebFixture.PostFormAsync(client, "/admin/server?handler=Maintenance", new()
        {
            ["enabled"] = "true",
            ["message"] = "Donanım yükseltmesi",
            ["kickNonGm"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, maint.StatusCode);
        string html = await client.GetStringAsync("/admin/server");
        Assert.Contains("Bakım ayarları kaydedildi; oyun sunucusu açıldığında geçerli olacak.", html);
        Assert.Contains("Bakım AÇIK", html);
        Assert.Contains("Donanım yükseltmesi", html);

        var motd = await WebFixture.PostFormAsync(client, "/admin/server?handler=Motd", new()
        {
            ["text"] = "Hafta sonu çifte tecrübe etkinliği!",
        });
        Assert.Equal(HttpStatusCode.Redirect, motd.StatusCode);
        html = await client.GetStringAsync("/admin/server");
        Assert.Contains("MOTD kaydedildi; oyun sunucusu açıldığında geçerli olacak.", html);
        Assert.Contains("Hafta sonu çifte tecrübe etkinliği!", html);

        var clearMotd = await WebFixture.PostFormAsync(client, "/admin/server?handler=Motd", new()
        {
            ["text"] = "Hafta sonu çifte tecrübe etkinliği!",
            ["clear"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, clearMotd.StatusCode);
        html = await client.GetStringAsync("/admin/server");
        Assert.DoesNotContain("Hafta sonu çifte tecrübe etkinliği!", html);

        // Reset maintenance so other tests see the default state
        var reset = await WebFixture.PostFormAsync(client, "/admin/server?handler=Maintenance", new()
        {
            ["enabled"] = "false",
            ["message"] = "",
            ["kickNonGm"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
    }

    [Fact]
    public async Task ShutdownAndGuildWarValidateAndReportOfflineGame()
    {
        string email = await fixture.CreateAccountAsync(authority: 5);
        var client = await fixture.SignInAsync(email, "secret1");

        var invalid = await WebFixture.PostFormAsync(client, "/admin/server?handler=StartShutdown", new()
        {
            ["mode"] = "Restart",
            ["minutes"] = "0",
            ["reason"] = "Yama",
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        string invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Geri sayım 1 ile 120 dakika arasında olmalı.", invalidHtml);
        Assert.Contains("value=\"Yama\"", invalidHtml);

        var startShutdown = await WebFixture.PostFormAsync(client, "/admin/server?handler=StartShutdown", new()
        {
            ["mode"] = "Restart",
            ["minutes"] = "5",
            ["reason"] = "Yama",
        });
        Assert.Equal(HttpStatusCode.Redirect, startShutdown.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await client.GetStringAsync("/admin/server"));

        var cancelShutdown = await WebFixture.PostFormAsync(client, "/admin/server?handler=CancelShutdown", new());
        Assert.Equal(HttpStatusCode.Redirect, cancelShutdown.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await client.GetStringAsync("/admin/server"));

        var startWar = await WebFixture.PostFormAsync(client, "/admin/server?handler=StartGuildWar", new());
        Assert.Equal(HttpStatusCode.Redirect, startWar.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await client.GetStringAsync("/admin/server"));

        var endWar = await WebFixture.PostFormAsync(client, "/admin/server?handler=EndGuildWar", new());
        Assert.Equal(HttpStatusCode.Redirect, endWar.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await client.GetStringAsync("/admin/server"));
    }
}
