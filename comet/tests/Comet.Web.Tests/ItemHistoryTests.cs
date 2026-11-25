#nullable enable
namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comet.Web.Features.ItemHistory;
using Comet.Web.Features.Logs;
using Xunit;

[Collection(WebCollection.Name)]
public class ItemHistoryTests(WebFixture fixture)
{
    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/item-history/1001")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/item-history/duplicates")).StatusCode);
    }

    [Fact]
    public async Task ModeratorCanQueryItemHistoryEvenWhenLokiIsDown()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

        var response = await client.GetAsync("/api/item-history/999999");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(999999u, doc.GetProperty("itemId").GetUInt32());
        Assert.Empty(doc.GetProperty("currentHolders").EnumerateArray());
        Assert.Contains("999999", doc.GetProperty("query").GetString());
    }

    [Fact]
    public void ParserFollowsCreatedTradedAndBoothSoldOwners()
    {
        const uint itemId = 55001;
        var names = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alice"] = 10,
            ["Bob"] = 20,
            ["Cara"] = 30,
        };

        // Lines as the game writes them
        var created = Line("""{"ts":"2026-11-25T10:00:00Z","level":"info","server":"game","event":"item.created","msg":"Alice got Club (item 55001, type 480339; drop)","name":"Alice","itemName":"Club","itemId":55001,"typeId":480339,"reason":"drop","characterId":10,"accountId":1}""");
        var traded = Line("""{"ts":"2026-11-25T10:05:00Z","level":"info","server":"game","event":"trade.completed","msg":"Alice gave Bob items [55001] ...","name":"Alice","partner":"Bob","itemIds":[55001],"typeIds":[480339],"silver":0,"conquerPoints":0,"partnerItemIds":[],"partnerTypeIds":[],"partnerSilver":0,"partnerConquerPoints":0,"characterId":10,"partnerId":20,"accountId":1}""");
        var boothSold = Line("""{"ts":"2026-11-25T10:10:00Z","level":"info","server":"game","event":"booth.sold","msg":"Cara bought item 55001 (type 480339) from Bob's booth 100091 for 50000 silver","name":"Cara","itemId":55001,"typeId":480339,"owner":"Bob","boothId":100091,"price":50000,"currency":"silver","characterId":30,"accountId":3}""");
        // An item whose ID starts with the same digits must not match
        var unrelated = Line("""{"ts":"2026-11-25T10:11:00Z","level":"info","server":"game","event":"item.created","msg":"Alice got Other (item 550012)","name":"Alice","itemId":550012,"characterId":10}""");

        Assert.True(ItemHistoryParser.MatchesItem(created, itemId));
        Assert.True(ItemHistoryParser.MatchesItem(traded, itemId));
        Assert.True(ItemHistoryParser.MatchesItem(boothSold, itemId));
        Assert.False(ItemHistoryParser.MatchesItem(unrelated, itemId));

        var step1 = ItemHistoryParser.ToStep(created, itemId, names);
        Assert.Equal(10u, step1.OwnerCharacterId);
        Assert.Equal("Alice", step1.OwnerName);
        Assert.Null(step1.FromCharacterId);

        var step2 = ItemHistoryParser.ToStep(traded, itemId, names);
        Assert.Equal(10u, step2.FromCharacterId);
        Assert.Equal("Alice", step2.FromName);
        Assert.Equal(20u, step2.OwnerCharacterId);
        Assert.Equal("Bob", step2.OwnerName);

        var step3 = ItemHistoryParser.ToStep(boothSold, itemId, names);
        Assert.Equal(20u, step3.FromCharacterId);
        Assert.Equal("Bob", step3.FromName);
        Assert.Equal(30u, step3.OwnerCharacterId);
        Assert.Equal("Cara", step3.OwnerName);
    }

    [Fact]
    public void DetectsDuplicateLiveHoldersAndMultipleCreations()
    {
        const uint itemId = 777;
        var holders = new[]
        {
            new ItemHolderInfo(itemId, 480339, "Club", null, 10, "Alice", 0, 0, 1, 0, 0, 0, 0, 0, false),
            new ItemHolderInfo(itemId, 480339, "Club", null, 20, "Bob", 0, 0, 1, 0, 0, 0, 0, 0, false),
        };
        var steps = new[]
        {
            new ItemHistoryStep(new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc), "item.created", "created 1", 10, "Alice", null, null, 480339, new()),
            new ItemHistoryStep(new DateTime(2026, 11, 25, 10, 2, 0, DateTimeKind.Utc), "item.created", "created 2", 20, "Bob", null, null, 480339, new()),
        };

        var flags = ItemHistoryParser.DetectDuplicates(itemId, holders, steps);
        Assert.Equal(2, flags.Length);
        Assert.Contains(flags, f => f.Kind == "duplicate_live_id");
        Assert.Contains(flags, f => f.Kind == "multiple_creation_events");
    }

    private static LogEntry Line(string json) =>
        Assert.Single(LokiClient.ParseResponse(
            "{\"data\":{\"result\":[{\"stream\":{},\"values\":[[\"0\"," + JsonSerializer.Serialize(json) + "]]}]}}"));
}
