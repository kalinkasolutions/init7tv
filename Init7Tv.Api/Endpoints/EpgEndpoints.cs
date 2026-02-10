using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Extensions;

namespace Init7Tv.Endpoints;

public static class EpgEndpoints
{
    public static void MapEpgEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/epg")
            .WithTags("Epg")
            .RequireAuthorization();

        group.MapGet("/{channelId:guid}", GetEpg);
    }

    private static async Task<IResult> GetEpg(Guid channelId, IEpgService epgService)
    {
        return (await epgService.GetEpg(channelId)).ToHttpResult();
    }
}