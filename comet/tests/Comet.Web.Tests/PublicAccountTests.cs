namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Account.Database.Repositories;
using Comet.Web.Audit;
using Comet.Web.Features.PublicAccount;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using AccountDbContext = Comet.Account.Database.ServerDbContext;
using GameDbContext = Comet.Game.Database.ServerDbContext;

[Collection(WebCollection.Name)]
public class PublicAccountTests(WebFixture fixture)
{
    private sealed class StubCaptchaVerifier(bool accept) : ICaptchaVerifier
    {
        public Task<bool> VerifyAsync(string token, string remoteIp, CancellationToken ct) =>
            Task.FromResult(accept && token == "valid-token");
    }

    [Fact]
    public async Task ConfigExposesLimitsAndDisabledCaptchaInDev()
    {
        var config = await fixture.Factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/api/public/account/config");

        Assert.False(config.GetProperty("captchaEnabled").GetBoolean());
        Assert.Equal(AccountRules.MinPasswordLength, config.GetProperty("minPasswordLength").GetInt32());
        Assert.Equal(AccountRules.MaxPasswordLength, config.GetProperty("maxPasswordLength").GetInt32());
        Assert.Equal(AccountRules.MaxUsernameLength, config.GetProperty("maxUsernameLength").GetInt32());
    }

    [Fact]
    public async Task RegisterCreatesAccountSignsInAndAudits()
    {
        string email = $"reg{Guid.NewGuid():N}"[..12] + "@test.local";
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/public/account/register",
            new { username = email.ToUpperInvariant(), password = "secret1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, created.GetProperty("username").GetString());
        Assert.Equal(1, created.GetProperty("authority").GetInt32());

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(email, me.GetProperty("username").GetString());
        uint accountId = me.GetProperty("accountId").GetUInt32();

        using var scope = fixture.Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WebDbContext>>();
        await using var webDb = await factory.CreateDbContextAsync();
        bool audited = await webDb.Audit.AnyAsync(x =>
            x.Action == "account.register" && x.TargetID == accountId.ToString());
        Assert.True(audited);
    }

    [Theory]
    [InlineData("not-an-email", "secret1", "invalid_username")]
    [InlineData("bad@test.local", "123", "invalid_password")]
    public async Task RegisterRejectsInvalidInput(string username, string password, string expectedError)
    {
        var response = await fixture.Factory.CreateClient().PostAsJsonAsync(
            "/api/public/account/register", new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedError, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RegisterRejectsDuplicateEmailCaseInsensitively()
    {
        string email = await fixture.CreateAccountAsync(authority: 1);
        var response = await fixture.Factory.CreateClient().PostAsJsonAsync(
            "/api/public/account/register", new { username = email.ToUpperInvariant(), password = "secret1" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("username_taken", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ProfileRequiresAuthAndReturnsCharactersAndLogins()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fixture.Factory.CreateClient().GetAsync("/api/public/account/profile")).StatusCode);

        string email = await fixture.CreateAccountAsync(authority: 1);
        var account = await AccountsRepository.FindAsync(email);
        Assert.NotNull(account);

        await using (var accountDb = new AccountDbContext())
        {
            accountDb.Logins.AddRange(
                new Comet.Account.Database.Models.DbLogin
                {
                    AccountID = account.AccountID,
                    Timestamp = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
                    IPAddress = "10.0.0.1",
                },
                new Comet.Account.Database.Models.DbLogin
                {
                    AccountID = account.AccountID,
                    Timestamp = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc),
                    IPAddress = "10.0.0.2",
                });
            await accountDb.SaveChangesAsync();
        }

        await using (var gameDb = new GameDbContext())
        {
            gameDb.Characters.Add(new Comet.Game.Database.Models.DbCharacter(account.AccountID, "TestHero")
            {
                Level = 120,
                Rebirths = 1,
                CurrentClass = 15,
                MapID = 1002,
                Silver = 50000,
                Jewels = 25,
                VIPLevel = 2,
                Spouse = "None",
                Registered = DateTime.UtcNow,
            });
            await gameDb.SaveChangesAsync();
        }

        var client = await fixture.LoginAsync(email, "secret1");
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/public/account/profile");

        Assert.Equal(email, profile.GetProperty("username").GetString());
        var characters = profile.GetProperty("characters").EnumerateArray().ToArray();
        Assert.Single(characters);
        Assert.Equal("TestHero", characters[0].GetProperty("name").GetString());
        Assert.Equal(120, characters[0].GetProperty("level").GetInt32());

        var logins = profile.GetProperty("logins").EnumerateArray().ToArray();
        Assert.Equal(2, logins.Length);
        Assert.Equal("10.0.0.2", logins[0].GetProperty("ipAddress").GetString());
        Assert.Equal("10.0.0.1", logins[1].GetProperty("ipAddress").GetString());
    }

    [Fact]
    public async Task ChangePasswordValidatesUpdatesAndAudits()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fixture.Factory.CreateClient().PostAsJsonAsync("/api/public/account/password",
                new { currentPassword = "secret1", newPassword = "secret2" })).StatusCode);

        string email = await fixture.CreateAccountAsync(authority: 1);
        var client = await fixture.LoginAsync(email, "secret1");

        var wrongCurrent = await client.PostAsJsonAsync("/api/public/account/password",
            new { currentPassword = "wrongpw", newPassword = "secret2" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Equal("wrong_password",
            (await wrongCurrent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var invalidNew = await client.PostAsJsonAsync("/api/public/account/password",
            new { currentPassword = "secret1", newPassword = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidNew.StatusCode);
        Assert.Equal("invalid_password",
            (await invalidNew.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var success = await client.PostAsJsonAsync("/api/public/account/password",
            new { currentPassword = "secret1", newPassword = "secret2" });
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);

        var oldLogin = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        await fixture.LoginAsync(email, "secret2");

        var account = await AccountsRepository.FindAsync(email);
        using var scope = fixture.Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WebDbContext>>();
        await using var webDb = await factory.CreateDbContextAsync();
        bool audited = await webDb.Audit.AnyAsync(x =>
            x.Action == "account.change_password" && x.TargetID == account!.AccountID.ToString());
        Assert.True(audited);
    }

    [Fact]
    public async Task CaptchaWhenEnabledEnforcesTokenVerification()
    {
        await using var captchaFactory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PublicAccount:CaptchaEnabled", "true");
            builder.UseSetting("PublicAccount:CaptchaSiteKey", "test-site-key");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICaptchaVerifier>();
                services.AddSingleton<ICaptchaVerifier>(new StubCaptchaVerifier(accept: true));
            });
        });

        var client = captchaFactory.CreateClient();
        string baseEmail = $"cap{Guid.NewGuid():N}"[..10];

        var missing = await client.PostAsJsonAsync("/api/public/account/register",
            new { username = $"{baseEmail}1@test.local", password = "secret1" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("captcha_required",
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var invalid = await client.PostAsJsonAsync("/api/public/account/register",
            new { username = $"{baseEmail}2@test.local", password = "secret1", captchaToken = "bad-token" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("captcha_failed",
            (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var valid = await client.PostAsJsonAsync("/api/public/account/register",
            new { username = $"{baseEmail}3@test.local", password = "secret1", captchaToken = "valid-token" });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
    }

    [Fact]
    public async Task RegisterIsRateLimitedPerIp()
    {
        await using var limitedFactory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PublicAccount:RegisterAttemptsPerMinute", "2");
        });

        var client = limitedFactory.CreateClient();
        string prefix = $"rl{Guid.NewGuid():N}"[..10];

        for (int i = 0; i < 2; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/public/account/register",
                new { username = $"{prefix}{i}@test.local", password = "secret1" });
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/public/account/register",
            new { username = $"{prefix}2@test.local", password = "secret1" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
