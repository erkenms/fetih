namespace Fetih.Web.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[Collection(WebCollection.Name)]
public class GirisTests(WebFixture fixture)
{
    private HttpClient Client() =>
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task ModeratorSignsInToTheAdmin()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = this.Client();
        var response = await WebFixture.PostFormAsync(client, "/giris",
            new() { ["kullanici"] = email, ["sifre"] = "secret1" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task SignInReturnsToTheAskedPage()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var response = await WebFixture.PostFormAsync(this.Client(), "/giris",
            new() { ["kullanici"] = email, ["sifre"] = "secret1", ["donus"] = "/admin/broadcasts" });
        Assert.Equal("/admin/broadcasts", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SignInRefusesAnOutsideReturnUrl()
    {
        var response = await WebFixture.PostFormAsync(this.Client(), "/giris",
            new() { ["kullanici"] = "test", ["sifre"] = "test", ["donus"] = "https://example.com/" });
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task WrongPasswordShowsTheForm()
    {
        var client = this.Client();
        var response = await WebFixture.PostFormAsync(client, "/giris",
            new() { ["kullanici"] = "test", ["sifre"] = "wrong1" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hesap adı ya da şifre yanlış.", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task PostWithoutTheAntiForgeryTokenIsRefused()
    {
        var response = await this.Client().PostAsync("/giris",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["kullanici"] = "test", ["sifre"] = "test" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignOutEndsTheSession()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        var response = await WebFixture.PostFormAsync(client, "/giris?handler=Cikis", new());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var admin = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.StartsWith("http://localhost/giris?donus=", admin.Headers.Location?.ToString());
    }
}
