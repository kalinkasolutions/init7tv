using Init7Tv.BusinessLogic;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Init7Tv.Endpoints;

public static class StreamingEndpoint
{
    public static void MapStreamingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/streaming")
            .WithTags("Streaming")
            .RequireAuthorization();

        group.MapGet("/channels", Channels);
        group.MapGet("/start-stream", StartStream);
        group.MapGet("/playlist", GetPlaylist);
        group.MapGet("/segment/{streamId}/{name}", GetSegment);
    }

    private static async Task<IResult> Channels(IChannelParserService channelParserService)
    {
        return (await channelParserService.GetChannelsAsync()).ToHttpResult();
    }

    private static async Task<IResult> StartStream(string streamUrl, int? audioStreamIndex, IStreamManager streamManager)
    {
        return (await streamManager.StartStream(streamUrl, audioStreamIndex.GetValueOrDefault())).ToHttpResult();
    }

    private static IResult GetPlaylist(string streamId, IStreamManager streamManager)
    {
        return streamManager.GetPlaylist(streamId).ToHttpResult();
    }

    private static IResult GetSegment(string streamId, string name, IStreamManager streamManager)
    {
        return streamManager.GetSegment(streamId, name).ToHttpResult();
    }
}