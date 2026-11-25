namespace Comet.Web.Features.Packets;

using Comet.Web.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public class PacketsFeature : IFeature
{
    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/packets").RequirePermission(Permission.ViewLogs);
        group.MapPost("/decode", Decode)
            .WithName("decodePacket")
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static Results<Ok<PacketDecodeResponse>, JsonHttpResult<ApiError>> Decode(PacketDecodeRequest request)
    {
        if (!PacketDecoder.TryParseHex(request.Hex, out byte[] packet))
            return ApiError.Result("invalid_hex", StatusCodes.Status400BadRequest);
        return TypedResults.Ok(PacketDecoder.Decode(packet, request.Server, request.Direction));
    }
}
