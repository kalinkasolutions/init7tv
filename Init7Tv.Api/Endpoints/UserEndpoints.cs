using Init7Tv.BusinessLogic;
using Init7Tv.Dto;

namespace Init7Tv.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/user")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/user-info", GetUserInfo);
    }

    private static IResult GetUserInfo(IUserIdentityProvider userIdentityProvider)
    {
        return Results.Ok(new UserInfo
        {
            UserRoles = userIdentityProvider.UserRoles,
            IsAdmin = userIdentityProvider.IsAdmin
        });
    }
}