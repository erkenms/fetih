namespace Fetih.Web.Tests;

using System.Net.Http.Json;
using Fetih.Account.Database.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

// The web app against its own copy of both schemas, migrated from sql/. The seed account
// "test" (password "test") is there; tests add accounts with the account server's code.
public sealed class WebFixture : IAsyncLifetime
{
    private static readonly string DbHost = Env("FETIH_TEST_DB_HOST", "localhost");
    private static readonly string DbUser = Env("FETIH_TEST_DB_USER", "root");
    private static readonly string DbPassword = Env("FETIH_TEST_DB_PASSWORD", "password");

    private readonly string RunId = Guid.NewGuid().ToString("N")[..8];
    public string AccountSchema => $"fetih_test_{this.RunId}_account";
    public string GameSchema => $"fetih_test_{this.RunId}_game";
    public WebApplicationFactory<Program> Factory { get; private set; }

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private string ConnectionString(string database = "") => new MySqlConnectionStringBuilder
    {
        Server = DbHost, UserID = DbUser, Password = DbPassword, Database = database,
        AllowUserVariables = true,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await this.LoadSchemaAsync("account", this.AccountSchema);
        await this.LoadSchemaAsync("game", this.GameSchema);

        var settings = new Dictionary<string, string>
        {
            ["Database:Hostname"] = DbHost, ["Database:Schema"] = this.GameSchema,
            ["Database:Username"] = DbUser, ["Database:Password"] = DbPassword,
            ["AccountDatabase:Hostname"] = DbHost, ["AccountDatabase:Schema"] = this.AccountSchema,
            ["AccountDatabase:Username"] = DbUser, ["AccountDatabase:Password"] = DbPassword,
            ["Web:LoginAttemptsPerMinute"] = "1000",
            // Nothing listens there, so the game server counts as offline
            ["GameRpc:Port"] = "1",
            ["Paths:FrontendDirectory"] = "",
            ["Paths:AssetsDirectory"] = "",
            ["Logging:Directory"] = Path.Combine(Path.GetTempPath(), "fetih-web-tests-" + this.RunId),
        };
        foreach (var (key, value) in settings)
            Environment.SetEnvironmentVariable("FETIH_WEB_" + key.Replace(":", "__"), value);
        this.Factory = new WebApplicationFactory<Program>();
        _ = this.Factory.Server;
    }

    public async Task DisposeAsync()
    {
        await this.Factory.DisposeAsync();
        await using var db = new MySqlConnection(this.ConnectionString());
        await db.OpenAsync();
        foreach (var schema in new[] { this.AccountSchema, this.GameSchema })
            await new MySqlCommand($"DROP DATABASE IF EXISTS `{schema}`", db).ExecuteNonQueryAsync();
    }

    private static string SqlRoot()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Fetih.slnx")))
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("Can't find Fetih.slnx");
        return Path.Combine(root, "sql");
    }

    private Task LoadSchemaAsync(string server, string schema) =>
        Fetih.Core.Database.SchemaMigrator.ApplyAsync(DbHost, schema, DbUser, DbPassword,
            Fetih.Core.Database.SchemaMigrator.LoadFromDirectory(
                Path.Combine(SqlRoot(), $"fetih_{server}.sql"), Path.Combine(SqlRoot(), "migrations", server)));

    public async Task<string> CreateAccountAsync(ushort authority, ushort status = 2)
    {
        string email = $"web{Guid.NewGuid():N}"[..12] + "@test.local";
        var (account, error) = await AccountsRepository.CreateAsync(email, "secret1");
        Assert.Null(error);
        await using var db = new MySqlConnection(this.ConnectionString(this.AccountSchema));
        await db.OpenAsync();
        var command = new MySqlCommand(
            "UPDATE account SET AuthorityID = @a, StatusID = @s WHERE AccountID = @id", db);
        command.Parameters.AddWithValue("@a", authority);
        command.Parameters.AddWithValue("@s", status);
        command.Parameters.AddWithValue("@id", account.AccountID);
        await command.ExecuteNonQueryAsync();
        return email;
    }

    // A client that signs in through /giris and doesn't follow redirects, for page tests
    public async Task<HttpClient> SignInAsync(string username, string password)
    {
        var client = this.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await PostFormAsync(client, "/giris", new() { ["kullanici"] = username, ["sifre"] = password });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    // Opens the page for its anti-forgery token, then posts the form to url (a page handler)
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url,
        Dictionary<string, string> fields)
    {
        string page = await client.GetStringAsync(url.Split('?')[0]);
        var token = System.Text.RegularExpressions.Regex.Match(page,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(token.Success, "No anti-forgery token on " + url);
        fields["__RequestVerificationToken"] = token.Groups[1].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    public async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = this.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return client;
    }
}

[CollectionDefinition(Name)]
public class WebCollection : ICollectionFixture<WebFixture>
{
    public const string Name = "web";
}
