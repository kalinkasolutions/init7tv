using Init7Tv.BusinessLogic.User;
using Init7Tv.Dto.Admin;
using Init7Tv.Extensions;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Endpoints;

public static class AdminEndpoint
{
    public static void MapAdminEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(new AuthorizeAttribute { Roles = Init7TvRoles.Admin });

        group.MapGet("/users", GetUsersAsync);
        group.MapGet("/roles", GetRoleNamesAsync);
        group.MapPost("/add-user", AddUserAsync);
        group.MapPut("/update-user/{id}", UpdateUser);
        group.MapDelete("/delete-user/{id}", DeleteUser);
    }

    private static async Task<IResult> GetUsersAsync(IIdentityService identityService)
    {
        return (await identityService.GetUsersAsync()).ToHttpResult();
    }

    private static async Task<IResult> GetRoleNamesAsync(IIdentityService identityService)
    {
        return (await identityService.GetRolesNamesAsync()).ToHttpResult();
    }

    private static async Task<IResult> AddUserAsync(AddUserDto addUserDto, IIdentityService identityService)
    {
        return (await identityService.AddUserAsync(addUserDto)).ToHttpResult();
    }

    private static async Task<IResult> UpdateUser(
        string id,
        UpdateUserDto updateUserDto,
        IIdentityService identityService
    )
    {
        return (await identityService.UpdateUserAsync(id, updateUserDto)).ToHttpResult();
    }

    private static async Task<IResult> DeleteUser(string id, IIdentityService identityService)
    {
        return (await identityService.DeleteUserAsync(id)).ToHttpResult();
    }
}