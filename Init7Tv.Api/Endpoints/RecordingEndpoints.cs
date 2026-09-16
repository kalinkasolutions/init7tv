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
    }

    private static async Task<IResult> GetPlanned(
        IPlannedRecordingService service,
        IUserIdentityProvider userIdentityProvider
    )
    {
        return (await service.GetAsync(userIdentityProvider.UserName)).ToHttpResult();
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
