using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dto;
using Init7Tv.Extensions;
using Init7Tv.Shared;

namespace Init7Tv.Endpoints;

public static class RecordingEndpoints
{
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
        group.MapDelete("/planned/{programmeId:guid}", Cancel);

        group.MapGet("/recordings", GetRecordings);
        // MapGet alone answers HEAD with 405, and a player checking the length
        // before it starts is entitled to an answer
        group.MapMethods("/recordings/{recordingId:guid}/file", ["GET", "HEAD"], GetRecordingFile);
        group.MapGet("/recordings/{recordingId:guid}/playlist.m3u8", GetPlaylist);
        group.MapMethods("/recordings/{recordingId:guid}/part/{part:int}.ts", ["GET", "HEAD"], GetPart);
        group.MapPost("/recordings/{recordingId:guid}/stop", StopRecording);
        group.MapDelete("/recordings/{recordingId:guid}", DeleteRecording);
    }

    private static async Task<IResult> GetRecordings(
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.GetAsync(userIdentityProvider.UserName, userIdentityProvider.IsAdmin)).ToHttpResult();
    }

    /// <summary>
    /// Served from the path rather than through OperationResult, whose file case
    /// hands Results.File a byte array: a recording is gigabytes, and reading one
    /// into memory per request would be the end of the process. The path overload
    /// also brings range requests, which is what lets a browser seek.
    /// </summary>
    private static async Task<IResult> GetRecordingFile(
        Guid recordingId,
        bool? download,
        IRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        var result = await service.GetFileAsync(
            recordingId, userIdentityProvider.UserName, userIdentityProvider.IsAdmin);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        var file = result.Value;

        return Results.File(
            file.Path,
            "video/mp4",
            fileDownloadName: download == true ? file.DownloadName : null,
            lastModified: file.LastModified,
            entityTag: null,
            enableRangeProcessing: true);
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

    private static async Task<IResult> Cancel(
        Guid programmeId,
        IPlannedRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.CancelAsync(userIdentityProvider.UserName, programmeId)).ToHttpResult();
    }
}
