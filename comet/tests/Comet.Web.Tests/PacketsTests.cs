namespace Comet.Web.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Comet.Game.Packets;
using Comet.Game.States;
using Comet.Web.Features.Packets;
using Xunit;

[Collection(WebCollection.Name)]
public class PacketsTests(WebFixture web)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // Encode leaves the length to the socket, which writes it on send
    private static byte[] Framed(byte[] packet)
    {
        BitConverter.TryWriteBytes(packet, (ushort)packet.Length);
        return packet;
    }

    private static byte[] Sealed(byte[] packet, string seal = "TQClient") =>
        [.. Framed(packet), .. Encoding.ASCII.GetBytes(seal)];

    private static PacketField Field(PacketDecodeResponse r, string name) =>
        Assert.Single(r.Fields, f => f.Name == name);

    [Fact]
    public void ClientTalkDecodesWithTheServersReaderAndSeal()
    {
        var talk = new MsgTalk(1000001, MsgTalk.TalkChannel.Whisper, "hello there");
        var r = PacketDecoder.Decode(Sealed(talk.Encode()), PacketServer.Game, PacketDirection.In);

        Assert.Equal("MsgTalk", r.TypeName);
        Assert.Equal(PacketReaderKind.Server, r.Reader);
        Assert.Equal(typeof(MsgTalk).FullName, r.ReaderName);
        Assert.True(r.Handled);
        Assert.Equal("TQClient", r.Seal);
        Assert.Null(r.Error);
        Assert.Equal("hello there", Field(r, "Message").Value);
        Assert.Equal(PacketFieldKind.Enum, Field(r, "Channel").Kind);
        Assert.StartsWith("Whisper (", Field(r, "Channel").Value);
        Assert.Equal("1000001", Field(r, "CharacterID").Value);
        Assert.DoesNotContain(r.Fields, f => f.Name is "Length" or "Type");
    }

    [Fact]
    public void ActionDecodesItsArgumentsAsAList()
    {
        var jump = MsgAction.Jump(1000001, 300, 310, 305, 315, 2);
        var r = PacketDecoder.Decode(Framed(jump.Encode()), PacketServer.Game, PacketDirection.In);

        Assert.Equal(PacketReaderKind.Server, r.Reader);
        Assert.StartsWith("MapJump (", Field(r, "Action").Value);
        Assert.Equal("300", Field(r, "X").Value);
        var args = Field(r, "Arguments");
        Assert.Equal(PacketFieldKind.List, args.Kind);
        Assert.NotNull(args.Children);
    }

    [Fact]
    public void ServerOnlyPacketFallsBackToTheClientReader()
    {
        var info = new MsgMagicInfo(1000, 3, 1234);
        var r = PacketDecoder.Decode(Framed(info.Encode()), PacketServer.Game, PacketDirection.Out);

        Assert.Equal("MsgMagicInfo", r.TypeName);
        Assert.Equal(PacketReaderKind.Client, r.Reader);
        Assert.False(r.Handled);
        Assert.Equal("1000", Field(r, "Type").Value);
        Assert.Equal("3", Field(r, "Level").Value);
        Assert.Equal("1234", Field(r, "Experience").Value);
    }

    [Fact]
    public void UnknownTypeKeepsTheHeaderAndDump()
    {
        byte[] packet = [0x0C, 0x00, 0xA8, 0xFD, 1, 2, 3, 4, 5, 6, 7, 8];
        var r = PacketDecoder.Decode(packet, PacketServer.Game, PacketDirection.In);

        Assert.Equal(0xFDA8, r.Type);
        Assert.Null(r.TypeName);
        Assert.Equal(PacketReaderKind.None, r.Reader);
        Assert.Empty(r.Fields);
        Assert.Equal(12, r.DeclaredLength);
        Assert.StartsWith("0000:", r.Dump);
    }

    [Fact]
    public void TruncatedPacketReportsTheReaderError()
    {
        byte[] full = Framed(new MsgTalk(1, MsgTalk.TalkChannel.Talk, "a much longer message").Encode());
        var r = PacketDecoder.Decode(full[..20], PacketServer.Game, PacketDirection.In);

        Assert.Equal(PacketReaderKind.Server, r.Reader);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void AccountLoginDecodesWithTheAccountServersReader()
    {
        string hex = "F0006404" + new string('0', 8) + Convert.ToHexString(Encoding.ASCII.GetBytes("bot0001@bots.local"))
            .PadRight(2 * 236, '0');
        Assert.True(PacketDecoder.TryParseHex(hex, out byte[] packet));
        var r = PacketDecoder.Decode(packet, PacketServer.Account, PacketDirection.In);

        Assert.Equal("MsgAccountSRP6", r.TypeName);
        Assert.Equal(PacketReaderKind.Server, r.Reader);
        Assert.Equal(typeof(Comet.Account.Packets.MsgAccountSRP6).FullName, r.ReaderName);
    }

    [Theory]
    [InlineData("1C00 1C04 3022 35AB", true)]
    [InlineData("1c-00-1c-04", true)]
    [InlineData("1C001C", false)]
    [InlineData("1C001C04Z", false)]
    [InlineData("1C001C040", false)]
    [InlineData("", false)]
    public void HexParsing(string text, bool ok) =>
        Assert.Equal(ok, PacketDecoder.TryParseHex(text, out _));

    [Fact]
    public async Task DecodeEndpointNeedsModeratorAndRejectsBadHex()
    {
        string hex = Convert.ToHexString(Sealed(new MsgTalk(7, MsgTalk.TalkChannel.Talk, "hi").Encode()));
        object body = new { hex, server = "Game", direction = "In" };

        using var anon = web.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/packets/decode", body)).StatusCode);

        using var player = await web.LoginAsync(await web.CreateAccountAsync((ushort)Authority.Player), "secret1");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync("/api/packets/decode", body)).StatusCode);

        using var mod = await web.LoginAsync(await web.CreateAccountAsync((ushort)Authority.Moderator), "secret1");
        var ok = await mod.PostAsJsonAsync("/api/packets/decode", body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var decoded = await ok.Content.ReadFromJsonAsync<PacketDecodeResponse>(Json);
        Assert.NotNull(decoded);
        Assert.Equal("hi", Assert.Single(decoded.Fields, f => f.Name == "Message").Value);

        var bad = await mod.PostAsJsonAsync("/api/packets/decode", new { hex = "nothex" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
