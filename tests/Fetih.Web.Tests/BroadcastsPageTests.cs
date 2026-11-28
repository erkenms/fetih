namespace Fetih.Web.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class BroadcastsPageTests(WebFixture fixture)
{
    [Fact]
    public async Task SignedOutGoesToSignIn()
    {
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/admin/broadcasts");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/giris?donus=%2Fadmin%2Fbroadcasts", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task PlayerIsForbidden()
    {
        var client = await fixture.SignInAsync(WebFixture.Player, WebFixture.PlayerPassword);
        var response = await client.GetAsync("/admin/broadcasts");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Yetkin yok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ModeratorSeesThePage()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        string html = await client.GetStringAsync("/admin/broadcasts");
        Assert.Contains("Zamanlanmış Duyurular", html);
        Assert.Contains("Oyun sunucusu kapalı", html);
    }

    [Fact]
    public async Task ModeratorCreatesEditsAndDeletesAnAnnouncement()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        string title = "Sayfa testi " + Guid.NewGuid().ToString("N")[..6];

        var created = await WebFixture.PostFormAsync(client, "/admin/broadcasts?handler=Save", new()
        {
            ["Id"] = "0", ["Title"] = title, ["Channel"] = "System", ["Message"] = "Merhaba",
            ["IntervalMinutes"] = "15", ["Enabled"] = "true", ["StartAt"] = "2026-12-01T20:00",
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        string html = await client.GetStringAsync("/admin/broadcasts");
        Assert.Contains("Duyuru kaydedildi.", html);
        Assert.Contains(title, html);
        Assert.Contains("value=\"2026-12-01T20:00\"", html);
        Assert.Contains("01.12.2026 20:00", html);

        uint id = (await fixture.Factory.Services.GetRequiredService<Fetih.Web.Features.Broadcasts.BroadcastService>()
            .ListAsync()).Single(a => a.Title == title).Id;
        var disabled = await WebFixture.PostFormAsync(client, "/admin/broadcasts?handler=Enable",
            new() { ["id"] = id.ToString(), ["enabled"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, disabled.StatusCode);
        Assert.Contains("Duyuru kapatıldı.", await client.GetStringAsync("/admin/broadcasts"));

        var deleted = await WebFixture.PostFormAsync(client, "/admin/broadcasts?handler=Delete",
            new() { ["id"] = id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        html = await client.GetStringAsync("/admin/broadcasts");
        Assert.Contains("Duyuru silindi.", html);
        Assert.DoesNotContain(title, html);
    }

    [Fact]
    public async Task AnInvalidAnnouncementReopensItsDialog()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        var response = await WebFixture.PostFormAsync(client, "/admin/broadcasts?handler=Save", new()
        {
            ["Id"] = "0", ["Title"] = "Pencere", ["Channel"] = "Center", ["Message"] = "x",
            ["IntervalMinutes"] = "15", ["StartAt"] = "2026-12-02T20:00", ["EndAt"] = "2026-12-01T20:00",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bitiş, başlangıçtan sonra olmalı.", html);
        Assert.Contains("id=\"announcement-0\" class=\"open\"", html);
    }

    [Fact]
    public async Task SendingWhileTheGameIsOfflineSaysSo()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        var response = await WebFixture.PostFormAsync(client, "/admin/broadcasts?handler=Send",
            new() { ["channel"] = "Center", ["text"] = "Bakım yakında" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Oyun sunucusu kapalı; işlem yapılamadı.", await client.GetStringAsync("/admin/broadcasts"));
    }

    [Fact]
    public async Task PlayerCannotPost()
    {
        var client = await fixture.SignInAsync(WebFixture.Player, WebFixture.PlayerPassword);
        string page = await client.GetStringAsync("/giris");
        var token = System.Text.RegularExpressions.Regex.Match(page,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/admin/broadcasts?handler=Delete", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = "1", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
