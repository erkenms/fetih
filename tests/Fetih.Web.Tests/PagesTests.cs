namespace Fetih.Web.Tests;

using System.Net;
using System.Reflection;
using Fetih.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

[Collection(WebCollection.Name)]
public class PagesTests(WebFixture fixture)
{
    // Razor Pages can't authorize one handler, so each OnPost… names its permission with
    // [Requires], which PageChecks enforces. Only pages open to everyone are exempt.
    [Fact]
    public void EveryPostHandlerRequiresAPermission()
    {
        var pages = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(PageModel)) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<AllowAnonymousAttribute>() == null);
        var missing = pages.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("OnPost") && m.GetCustomAttribute<RequiresAttribute>() == null)
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task HealthzReportsTheOfflineGameServer()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("down: game server", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminHomeListsTheMenuForAModerator()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        string html = await client.GetStringAsync("/admin");
        Assert.Contains("href=\"/admin/broadcasts\"", html);
        Assert.Contains("href=\"/admin/players\"", html);
        Assert.DoesNotContain("href=\"/admin/server\"", html);
    }

    [Fact]
    public async Task PickersAnswerWithAList()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        foreach (string kind in new[] { "Item", "Monster", "Look", "Map" })
        {
            var response = await client.GetAsync($"/admin/pickers?kind={kind}&q=a");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("<html", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task UnknownPageAnswers404()
    {
        string email = await fixture.CreateAccountAsync(authority: 2);
        var client = await fixture.SignInAsync(email, "secret1");
        var response = await client.GetAsync("/hata/404");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sayfa bulunamadı", await response.Content.ReadAsStringAsync());
    }
}
