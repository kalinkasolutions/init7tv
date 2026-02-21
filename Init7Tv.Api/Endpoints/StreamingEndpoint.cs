using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.Extensions;

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

    private static async Task<IResult> Channels(IChannelService channelService)
    {
        return (await channelService.GetChannelsAsync()).ToHttpResult();
    }

    private static async Task<IResult> StartStream(Guid channelId,
        int? audioStreamIndex,
        IUserIdentityProvider userIdentityProvider,
        IStreamManager streamManager,
        IAppSettingsService appSettingsService
    )
    {
        var appSettings = await appSettingsService.GetGeneralSettingsAsync();
        if (!appSettings.IsSuccess)
        {
            return appSettings.ToHttpResult();
        }

        return (await streamManager.StartStream(channelId, audioStreamIndex.GetValueOrDefault(), userIdentityProvider.UserName, appSettings.Value)).ToHttpResult();
    }

    private static IResult GetPlaylist(string streamId, IStreamManager streamManager, IUserIdentityProvider userIdentityProvider)
    {
        return streamManager.GetPlaylist(streamId, userIdentityProvider.UserName).ToHttpResult();
    }

    private static IResult GetSegment(string streamId, string name, IStreamManager streamManager)
    {
        return streamManager.GetSegment(streamId, name).ToHttpResult();
    }
}