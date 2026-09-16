using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.Dto;
using Init7Tv.Extensions;

namespace Init7Tv.Endpoints;

public static class StreamingEndpoint
{
    // idle connections are dropped by proxies, and nothing else tells the
    // server that a viewer's browser has gone away
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

    public static void MapStreamingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/streaming")
            .WithTags("Streaming")
            .RequireAuthorization();

        group.MapGet("/channels", Channels);
        group.MapGet("/start-stream", StartStream);
        group.MapGet("/playlist", GetPlaylist);
        group.MapGet("/segment/{streamId}/{name}", GetSegment);
        group.MapGet("/events", GetEvents);
    }

    /// <summary>
    /// Tells the viewer which of their streams are still running, so the player
    /// can say the stream ended instead of silently stalling.
    /// </summary>
    private static IResult GetEvents(
        IStreamEventBus streamEventBus,
        IStreamManager streamManager,
        IUserIdentityProvider userIdentityProvider,
        CancellationToken cancellationToken
    )
    {
        return TypedResults.ServerSentEvents(
            ViewerStreams(userIdentityProvider.UserName, streamEventBus, streamManager, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<ViewerStreamsDto>> ViewerStreams(
        string userName,
        IStreamEventBus streamEventBus,
        IStreamManager streamManager,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // only the newest state matters, a slow reader should skip the rest
        var updates = Channel.CreateBounded<CurrentStreamDto[]>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

        using var subscription = streamEventBus.Subscribe(streams => updates.Writer.TryWrite(streams));

        // send the current state up front so a reconnecting client resyncs
        var last = StreamIdsOf(userName, streamManager.GetCurrentStreams());
        yield return Streams(last);

        while (!cancellationToken.IsCancellationRequested)
        {
            CurrentStreamDto[]? streams = null;

            using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                heartbeat.CancelAfter(HeartbeatInterval);
                try
                {
                    streams = await updates.Reader.ReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException)
                {
                    // heartbeat elapsed, or the client disconnected
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (streams == null)
            {
                yield return new SseItem<ViewerStreamsDto>(new ViewerStreamsDto(), "ping");
                continue;
            }

            var current = StreamIdsOf(userName, streams);
            if (current.SequenceEqual(last))
            {
                continue;
            }

            last = current;
            yield return Streams(current);
        }
    }

    private static SseItem<ViewerStreamsDto> Streams(string[] streamIds) =>
        new(new ViewerStreamsDto { StreamIds = streamIds }, "streams");

    private static string[] StreamIdsOf(string userName, CurrentStreamDto[] streams)
    {
        return streams
            .Where(x => x.UserNames.Contains(userName))
            .Select(x => x.StreamId)
            .Order(StringComparer.Ordinal)
            .ToArray();
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