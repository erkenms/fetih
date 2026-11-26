#nullable enable
namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using Comet.Web.Features.NpcDialogs;
using Xunit;

[Collection(WebCollection.Name)]
public class NpcDialogsTests(WebFixture fixture)
{
    [Fact]
    public async Task PlayerIsRefused()
    {
        var client = await fixture.LoginAsync("test", "test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/npc-dialogs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/npc-dialogs/30104")).StatusCode);
    }

    [Fact]
    public async Task ScriptedNpcIsReadFromItsMethod()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

        var list = await client.GetFromJsonAsync<NpcDialogListResponse>("/api/npc-dialogs");
        Assert.Contains(list!.Npcs, n => n.NpcId == 30104 && n.MapId == 1002 && n.Kind == "script");

        var guard = await client.GetFromJsonAsync<NpcDialogDetailResponse>("/api/npc-dialogs/30104");
        Assert.Equal("TwinCity.GuardAsync", guard!.Handler);
        Assert.Equal(Path.Combine("Npcs", "TwinCity.cs"), guard.SourceFile);
        var page = Assert.Single(guard.Pages);
        Assert.Equal(["I am in charge of the security of the city. No bad guys can escape here."], page.Lines);
        Assert.Equal(new NpcDialogOption("Wow, it is wonderful.", 255, "close"), Assert.Single(page.Options));
        Assert.Equal(30, page.Face);
    }

    [Fact]
    public async Task TeleporterShowsThePageTheGameBuilds()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");

        var conductress = await client.GetFromJsonAsync<NpcDialogDetailResponse>("/api/npc-dialogs/10050");
        Assert.Equal("teleporter", conductress!.Npc.Kind);
        Assert.Equal(100u, conductress.Fare);
        var page = Assert.Single(conductress.Pages);
        Assert.Equal(["Where are you heading for? I can teleport you for a price of 100 silver."], page.Lines);
        Assert.Equal(1, page.Face);
        Assert.Equal(7, page.Options.Length);
        Assert.Equal(new NpcDialogOption("Phoenix Castle", 1, "choice", 1002, page.Options[0].TargetMapName, 958, 555),
            page.Options[0]);
        Assert.Equal(new NpcDialogOption("Just passing by.", 255, "close"), page.Options[^1]);
    }

    [Fact]
    public async Task UnknownNpcIsNotFound()
    {
        var client = await fixture.LoginAsync(await fixture.CreateAccountAsync(authority: 2), "secret1");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/npc-dialogs/4000000000")).StatusCode);
    }

    [Fact]
    public void SourceIsSplitIntoPagesWithConstantsResolved()
    {
        const string source = """
            public static async Task OtherAsync(NpcTalk talk) => await talk.Text("Not this one.").ShowAsync();

            public static async Task MarkControllerAsync(NpcTalk talk)
            {
                if (talk.Option != Leave)
                {
                    await talk.Text("Do you want to leave the market? " + "I can teleport you for free.")
                        .Choice("Yeah. Thanks.", Leave)
                        .Choice("No, I shall stay here.", MsgTaskDialog.NoOption)
                        .Face(MarkControllerFace).ShowAsync();
                    return;
                }
                await talk.Text($"You need {Fare} silver, {client.Character.Name}.")
                    .Text("+12 gear is " + name + ".")
                    .TextBox("Enter a name", 2, 16)
                    .ShowAsync();
            }
            """;

        var pages = NpcScriptExtractor.FromSource(source, "MarkControllerAsync",
            name => name switch { "Leave" => 1, "MarkControllerFace" => 9, _ => null });

        Assert.Equal(2, pages.Length);
        Assert.Equal(["Do you want to leave the market? I can teleport you for free."], pages[0].Lines);
        Assert.Equal(
            [new NpcDialogOption("Yeah. Thanks.", 1, "choice"), new NpcDialogOption("No, I shall stay here.", 255, "close")],
            pages[0].Options);
        Assert.Equal(9, pages[0].Face);
        Assert.Equal(["You need {Fare} silver, {client.Character.Name}.", "+12 gear is {name}."], pages[1].Lines);
        Assert.Equal([new NpcDialogOption("Enter a name", 2, "textbox")], pages[1].Options);
    }

    [Fact]
    public void HelpersTheMethodHandsTheTalkToAreRead()
    {
        const string source = """
            public static Task AlexAsync(NpcTalk talk) => TalkAsync(talk, 2, "an Exemption Token");

            private static async Task TalkAsync(NpcTalk talk, int count, string item)
            {
                await talk.Text($"Bring me {count} of {item}.").Choice("Here.", (byte)count).ShowAsync();
            }
            """;

        var page = Assert.Single(NpcScriptExtractor.FromSource(source, "AlexAsync", _ => null));
        Assert.Equal(["Bring me {count} of {item}."], page.Lines);
        Assert.Equal([new NpcDialogOption("Here.", null, "choice")], page.Options);
    }
}
