using Init7Tv.Dto.Admin;
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

        group.MapGet("/users", GetUsers);
        group.MapGet("/roles", GetRoles);
        group.MapPost("/add-user", AddUser);
        group.MapPut("/update-user/{id}", UpdateUser);
        group.MapDelete("/delete-user/{id}", DeleteUser);
    }

    private static async Task<IResult> GetUsers(UserManager<IdentityUser> userManager)
    {
        var users = await userManager.Users.ToListAsync();
        var result = new List<GetUserDto>(users.Count);
        foreach (var user in users)
        {
            result.Add(new GetUserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Roles = await userManager.GetRolesAsync(user),
                IsAdmin = await userManager.IsInRoleAsync(user, Init7TvRoles.Admin)
            });
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetRoles(RoleManager<IdentityRole> roleManager)
    {
        var roles = await roleManager.Roles.Select(r => r.Name).ToListAsync();
        return Results.Ok(roles);
    }

    private static async Task<IResult> AddUser(AddUserDto addUserDto, RoleManager<IdentityRole> roleManager, UserManager<IdentityUser> userManager)
    {
        var user = new IdentityUser
        {
            UserName = addUserDto.UserName,
            Email = addUserDto.Email
        };

        var result = await userManager.CreateAsync(user, addUserDto.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new
            {
                Message = result.Errors
            });
        }

        if (addUserDto.Roles.Count != 0)
        {
            await userManager.AddToRolesAsync(user, addUserDto.Roles);
        }

        return Results.Ok(new GetUserDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Roles = await userManager.GetRolesAsync(user)
        });
    }

    private static async Task<IResult> UpdateUser(
        string id,
        UpdateUserDto updateUserDto,
        RoleManager<IdentityRole> roleManager,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager
    )
    {
        var updateUser = await userManager.FindByIdAsync(id);
        if (updateUser == null)
        {
            return Results.NotFound(new { Message = "User not found" });
        }

        if (await userManager.IsInRoleAsync(updateUser, Init7TvRoles.Admin) &&
            (await userManager.GetUsersInRoleAsync(Init7TvRoles.Admin)).Count <= 1 &&
            !updateUserDto.Roles.Contains(Init7TvRoles.Admin))
        {
            return Results.UnprocessableEntity(new { Message = "Unable to remove last admin role" });
        }

        var currentRoles = await userManager.GetRolesAsync(updateUser);
        var needsRefresh = !updateUserDto.Roles.Contains(Init7TvRoles.Admin) && currentRoles.Contains(Init7TvRoles.Admin);
        var removeRoleResults = await userManager.RemoveFromRolesAsync(updateUser, currentRoles);

        if (!removeRoleResults.Succeeded)
        {
            return Results.UnprocessableEntity(removeRoleResults.Errors);
        }

        foreach (var role in updateUserDto.Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                return Results.BadRequest($"Role '{role}' does not exist.");
            }

            var addToRoleResult = await userManager.AddToRoleAsync(updateUser, role);
            if (!addToRoleResult.Succeeded)
            {
                return Results.BadRequest(addToRoleResult.Errors);
            }
        }

        if (needsRefresh)
        {
            await userManager.UpdateSecurityStampAsync(updateUser);
        }

        updateUser.UserName = updateUserDto.UserName;
        updateUser.Email = updateUserDto.Email;

        var updateUserResult = await userManager.UpdateAsync(updateUser);
        if (!updateUserResult.Succeeded)
        {
            return Results.BadRequest(updateUserResult.Errors);
        }

        if (!string.IsNullOrEmpty(updateUserDto.Password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(updateUser);
            var result = await userManager.ResetPasswordAsync(updateUser, token, updateUserDto.Password);

            if (!result.Succeeded)
            {
                return Results.UnprocessableEntity(result.Errors);
            }
        }

        return Results.Ok(new GetUserDto
        {
            Id = updateUser.Id,
            UserName = updateUser.UserName,
            Email = updateUser.Email,
            Roles = await userManager.GetRolesAsync(updateUser)
        });
    }

    private static async Task<IResult> DeleteUser(string id, UserManager<IdentityUser> userManager)
    {
        var deleteUser = await userManager.FindByIdAsync(id);
        if (deleteUser == null)
        {
            return Results.NotFound(new { Message = "User not found" });
        }

        if (await userManager.IsInRoleAsync(deleteUser, Init7TvRoles.Admin) && (await userManager.GetUsersInRoleAsync(Init7TvRoles.Admin)).Count <= 1)
        {
            return Results.UnprocessableEntity(new { Message = "Unable to delete last admin" });
        }

        var result = await userManager.DeleteAsync(deleteUser);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { Message = result.Errors });
        }

        return Results.Ok(id);
    }
}