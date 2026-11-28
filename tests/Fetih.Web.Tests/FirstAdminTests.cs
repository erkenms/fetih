namespace Fetih.Web.Tests;

using Fetih.Account.Database.Repositories;
using Xunit;

[Collection(WebCollection.Name)]
public class FirstAdminTests(WebFixture fixture)
{
    [Fact]
    public async Task EnsureFirstAdminAsync_CreatesMissingAdminAndNeverTouchesExisting()
    {
        _ = fixture;
        string email = $"first-{Guid.NewGuid():N}@fetihoyun.com";

        bool created = await AccountsRepository.EnsureFirstAdminAsync(email, "InitialPass123!");
        Assert.True(created);

        var admin = await AccountsRepository.FindAsync(email);
        Assert.NotNull(admin);
        Assert.Equal((ushort)5, admin.AuthorityID);
        string originalVerifier = admin.SrpVerifier;

        // Second call must not overwrite password or authority
        bool second = await AccountsRepository.EnsureFirstAdminAsync(email, "DifferentPass999!");
        Assert.False(second);

        var unchanged = await AccountsRepository.FindAsync(email);
        Assert.NotNull(unchanged);
        Assert.Equal(originalVerifier, unchanged.SrpVerifier);
        Assert.Equal((ushort)5, unchanged.AuthorityID);
    }
}
