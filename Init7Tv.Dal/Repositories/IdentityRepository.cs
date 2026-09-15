using Init7Tv.Dal.Extensions;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Init7Tv.Dal.Repositories;

public sealed class IdentityRepository : IIdentityRepository
{
    private readonly ILogger<IdentityRepository> m_logger;
    private readonly UserManager<IdentityUser> m_userManager;
    private readonly RoleManager<IdentityRole> m_roleManager;

    public IdentityRepository(
        ILogger<IdentityRepository> logger,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager
    )
    {
        m_logger = logger;
        m_userManager = userManager;
        m_roleManager = roleManager;
    }

    public Task<IdentityUser[]> GetUsersAsync()
    {
        return m_userManager.Users.ToArrayAsync();
    }

    public async Task<string[]> GetRolesForUserAsync(IdentityUser user)
    {
        return (await m_userManager.GetRolesAsync(user)).ToArray();
    }

    public Task<bool> IsAdminAsync(IdentityUser user)
    {
        return m_userManager.IsInRoleAsync(user, Init7TvRoles.Admin);
    }

    public async Task<string[]> GetRoleNamesAsync()
    {
        return await m_roleManager.Roles
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => r.Name!)
            .ToArrayAsync();
    }

    public async Task<OperationResult<IdentityUser>> AddUserAsync(IdentityUser user, string[] roles, string password)
    {
        var createUserResult = await m_userManager.CreateAsync(user, password);
        if (!createUserResult.Succeeded)
        {
            return OperationResult<IdentityUser>.Error(createUserResult.ToErrorString());
        }

        if (roles.Length != 0)
        {
            var createRolesResult = await m_userManager.AddToRolesAsync(user, roles);
            if (!createRolesResult.Succeeded)
            {
                return OperationResult<IdentityUser>.Error(createRolesResult.ToErrorString());
            }
        }

        return OperationResult<IdentityUser>.Success(user);
    }

    public async Task<OperationResult<IdentityUser>> UpdateUserAsync(
        IdentityUser updateUser,
        string[] roles,
        string? password
    )
    {
        var userToUpdate = await m_userManager.FindByIdAsync(updateUser.Id);
        if (userToUpdate == null)
        {
            return OperationResult<IdentityUser>.NotFound("User not found");
        }

        if (await m_userManager.IsInRoleAsync(userToUpdate, Init7TvRoles.Admin) &&
            (await m_userManager.GetUsersInRoleAsync(Init7TvRoles.Admin)).Count <= 1 &&
            !roles.Contains(Init7TvRoles.Admin))
        {
            return OperationResult<IdentityUser>.Conflict("Unable to remove the last admin role");
        }

        var currentRoles = await m_userManager.GetRolesAsync(userToUpdate);
        var removeRoleResults = await m_userManager.RemoveFromRolesAsync(userToUpdate, currentRoles);
        if (!removeRoleResults.Succeeded)
        {
            return OperationResult<IdentityUser>.Error(removeRoleResults.ToErrorString());
        }

        foreach (var role in roles)
        {
            if (!await m_roleManager.RoleExistsAsync(role))
            {
                m_logger.LogError("Role {Role} does not exist", role);
                continue;
            }

            var addToRoleResult = await m_userManager.AddToRoleAsync(userToUpdate, role);
            if (!addToRoleResult.Succeeded)
            {
                return OperationResult<IdentityUser>.Error(addToRoleResult.ToErrorString());
            }
        }

        userToUpdate.UserName = updateUser.UserName;
        userToUpdate.Email = updateUser.Email;

        var updateUserResult = await m_userManager.UpdateAsync(userToUpdate);
        if (!updateUserResult.Succeeded)
        {
            return OperationResult<IdentityUser>.Error(updateUserResult.ToErrorString());
        }

        if (!string.IsNullOrEmpty(password))
        {
            var token = await m_userManager.GeneratePasswordResetTokenAsync(userToUpdate);
            var resetPasswordResult = await m_userManager.ResetPasswordAsync(userToUpdate, token, password);

            if (!resetPasswordResult.Succeeded)
            {
                return OperationResult<IdentityUser>.Error(resetPasswordResult.ToErrorString());
            }
        }

        return await GetUserByIdAsync(userToUpdate.Id);
    }

    public async Task<OperationResult<string>> DeleteUserAsync(string userId)
    {
        var deleteUser = await m_userManager.FindByIdAsync(userId);
        if (deleteUser == null)
        {
            return OperationResult<string>.NotFound("User not found");
        }

        if (await m_userManager.IsInRoleAsync(deleteUser, Init7TvRoles.Admin) &&
            (await m_userManager.GetUsersInRoleAsync(Init7TvRoles.Admin)).Count <= 1)
        {
            return OperationResult<string>.Conflict("Unable to delete the last admin");
        }

        var deleteResult = await m_userManager.DeleteAsync(deleteUser);
        if (!deleteResult.Succeeded)
        {
            return OperationResult<string>.Error(deleteResult.ToErrorString());
        }

        return OperationResult<string>.Success(userId);
    }

    public async Task<OperationResult<IdentityUser>> GetUserByEmailAsync(string email)
    {
        var user = await m_userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return OperationResult<IdentityUser>.Error($"User not found with email: {email}");
        }

        return OperationResult<IdentityUser>.Success(user);
    }

    public async Task<OperationResult<IdentityUser>> GetUserByIdAsync(string userId)
    {
        var user = await m_userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return OperationResult<IdentityUser>.Error($"User not found with userId: {userId}");
        }

        return OperationResult<IdentityUser>.Success(user);
    }

    public Task<string> GeneratePasswordResetTokenAsync(IdentityUser user)
    {
        return m_userManager.GeneratePasswordResetTokenAsync(user);
    }
}