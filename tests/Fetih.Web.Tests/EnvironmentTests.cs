namespace Fetih.Web.Tests;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Collection(WebCollection.Name)]
public class EnvironmentTests(WebFixture fixture)
{
    [Fact]
    public async Task StaffOnlyRefusesPlayersAndPublicRegistration()
    {
        var config = fixture.Factory.Services.GetRequiredService<WebConfiguration>();
        bool prevStaffOnly = config.Web.StaffOnly;
        config.Web.StaffOnly = true;
        try
        {
            var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var playerLogin = await WebFixture.PostFormAsync(client, "/giris",
                new() { ["kullanici"] = WebFixture.Player, ["sifre"] = WebFixture.PlayerPassword });
            Assert.Equal(HttpStatusCode.OK, playerLogin.StatusCode);
            Assert.Contains("yetkili", await playerLogin.Content.ReadAsStringAsync());

            var register = await WebFixture.PostFormAsync(client, "/kayit",
                new() { ["Eposta"] = "newplayer@test.local", ["Sifre"] = "secret1", ["SifreTekrar"] = "secret1" });
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
            Assert.Contains("yetkili", await register.Content.ReadAsStringAsync());

            string modEmail = await fixture.CreateAccountAsync(authority: 2);
            var modLogin = await WebFixture.PostFormAsync(client, "/giris",
                new() { ["kullanici"] = modEmail, ["sifre"] = "secret1" });
            Assert.Equal(HttpStatusCode.Redirect, modLogin.StatusCode);
        }
        finally
        {
            config.Web.StaffOnly = prevStaffOnly;
        }
    }
}
