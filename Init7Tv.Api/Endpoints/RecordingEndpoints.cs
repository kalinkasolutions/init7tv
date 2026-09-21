using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dto;
using Init7Tv.Extensions;
using Init7Tv.Shared;

namespace Init7Tv.Endpoints;

public static class RecordingEndpoints
{
    // idle connections are dropped by proxies, and nothing else tells the
    // server that a viewer's browser has gone away
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

    public static void MapRecordingEndpoints(this IEndpointRouteBuilder app)
    {
        // the page is only offered to these two, and the endpoints say so as well:
        // hiding a tab is not a rule, it is only a courtesy
        var group = app.MapGroup("/api/recording")
            .WithTags("Recording")
            .RequireAuthorization(policy =>
                policy.RequireRole(Init7TvRoles.Admin, Init7TvRoles.Recording));

        group.MapGet("/planned", GetPlanned);
        group.MapPost("/planned", Plan);
        group.MapPost("/record-now", RecordNow);
        group.MapDelete("/planned/{programmeId:guid}", Cancel);

        group.MapGet("/recordings", GetRecordings);
        group.MapGet("/events", GetEvents);
        // MapGet alone answers HEAD with 405, and a player checking the length
        // before it starts is entitled to an answer
        group.MapMethods("/recordings/{recordingId:guid}/file", ["GET", "HEAD"], GetRecordingFile);
        group.MapGet("/recordings/{recordingId:guid}/playlist.m3u8", GetPlaylist);
        group.MapGet("/recordings/{recordingId:guid}/ad-breaks", GetAdBreaks);
        group.MapMethods("/recordings/{recordingId:guid}/part/{part:int}.ts", ["GET", "HEAD"], GetPart);
        group.MapPost("/recordings/{recordingId:guid}/stop", StopRecording);
        group.MapDelete("/recordings/{recordingId:guid}", DeleteRecording);
    }

    /// <summary>
    /// Says when a recording has started, finished, been stopped or been given up on, so the page
    /// can ask again at the moment there is something to ask about rather than every fifteen
    /// seconds in the hope of it.
    ///
    /// Nothing is sent but the news itself: the client then reads the same list it would have read
    /// anyway, which keeps GET /recordings the one place that decides what a given user may see.
    /// </summary>
    private static IResult GetEvents(IRecordingEventBus eventBus, CancellationToken cancellationToken)
    {
        return TypedResults.ServerSentEvents(RecordingChanges(eventBus, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<string>> RecordingChanges(
        IRecordingEventBus eventBus,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // Every pass publishes, and a pass only runs when something could have changed. Which
        // recording changed does not matter here: one pass is one reason to look again, and a
        // reader that missed three of them still only has to look once.
        var changes = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

        using var subscription = eventBus.Subscribe(_ => changes.Writer.TryWrite(true));

        // a browser that has just connected, or reconnected after the socket dropped, has no idea
        // what it missed
        yield return Changed;

        while (!cancellationToken.IsCancellationRequested)
        {
            var changed = false;

            using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                heartbeat.CancelAfter(HeartbeatInterval);
                try
                {
                    changed = await changes.Reader.ReadAsync(heartbeat.Token);
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

            yield return changed ? Changed : new SseItem<string>(string.Empty, "ping");
        }
    }

    private static SseItem<string> Changed => new(string.Empty, "changed");

    private static async Task<IResult> GetRecordings(
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.GetAsync(userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    /// <summary>
    /// Writes the recording out as an mp4 as it goes, rather than turning it into one first.
    ///
    /// The transport stream on disk is already H.264 and AAC, so this is a change of wrapper and
    /// nothing is decoded or encoded. Nothing is kept either: a copy would cost as much disk again
    /// for something most recordings never need, and making it takes about as long as sending it.
    /// </summary>
    private static async Task<IResult> GetRecordingFile(
        Guid recordingId,
        bool? withoutAds,
        IRecordingService service,
        IRecordingDownloadWriter writer,
        IUserIdentityProvider userIdentityProvider
    )
    {
        var result = await service.GetDownloadAsync(
            recordingId, withoutAds == true, userIdentityProvider.UserName, userIdentityProvider.IsAdmin);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        var download = result.Value;

        // no length: how long an mp4 of these ranges comes out is not known until it has been
        // written, and guessing would break the download rather than the progress bar
        return Results.Stream(
            stream => writer.WriteAsync(download, stream),
            "video/mp4",
            fileDownloadName: download.DownloadName);
    }

    private static async Task<IResult> GetAdBreaks(
        Guid recordingId,
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.GetAdBreaksAsync(
            recordingId, userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    private static async Task<IResult> GetPlaylist(
        Guid recordingId,
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.GetPlaylistAsync(
            recordingId, userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    /// <summary>
    /// A capture the playlist points into. Ranged, because that is the whole arrangement: the player
    /// asks for the stretch it wants out of the file ffmpeg is still writing.
    /// </summary>
    private static async Task<IResult> GetPart(
        Guid recordingId,
        int part,
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        var result = await service.GetPartAsync(
            recordingId, part, userIdentityProvider.UserName, userIdentityProvider.IsAdmin);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // no last-modified or etag: the file is still growing, and a player told it had not changed
        // would sit on what it already has
        return Results.File(result.Value.Path, "video/mp2t", enableRangeProcessing: true);
    }

    private static async Task<IResult> StopRecording(
        Guid recordingId,
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.StopAsync(
            recordingId, userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    private static async Task<IResult> DeleteRecording(
        Guid recordingId,
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.DeleteAsync(
            recordingId, userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    private static async Task<IResult> GetPlanned(
        IPlannedRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.GetAsync(userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    private static async Task<IResult> Plan(
        PlannedRecordingDto recording,
        IPlannedRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.PlanAsync(userIdentityProvider.UserName, recording)).ToHttpResult();
    }

    /// <summary>
    /// Records what is on a channel now, with no end in mind: it runs until somebody stops it, or
    /// until the disk has no more room for it.
    /// </summary>
    private static async Task<IResult> RecordNow(
        Guid channelId,
        IPlannedRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.RecordNowAsync(userIdentityProvider.UserName, channelId)).ToHttpResult();
    }

    /// <summary>
    /// Drops a pick. <paramref name="owner"/> says whose, which an admin needs because they are
    /// shown everybody's; left out it means the caller's own.
    /// </summary>
    private static async Task<IResult> Cancel(
        Guid programmeId,
        string? owner,
        IPlannedRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.CancelAsync(
                userIdentityProvider.UserName,
                userIdentityProvider.IsAdmin,
                programmeId,
                owner ?? userIdentityProvider.UserName))
            .ToHttpResult();
    }
}
