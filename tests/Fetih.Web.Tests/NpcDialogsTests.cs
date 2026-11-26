#nullable enable
namespace Fetih.Web.Tests;

using Fetih.Web.Features.NpcDialogs;
using Xunit;

public class NpcDialogsTests
{
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
