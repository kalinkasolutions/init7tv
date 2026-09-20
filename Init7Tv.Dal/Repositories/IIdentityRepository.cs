using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;

namespace Init7Tv.Dal.Repositories;

public interface IIdentityRepository
{
    Task<IdentityUser[]> GetUsersAsync();
    Task<string[]> GetRolesForUserAsync(IdentityUser user);
    Task<string[]> GetRoleNamesAsync();
    Task<OperationResult<IdentityUser>> AddUserAsync(IdentityUser user, string[] roles, string password);
    Task<OperationResult<IdentityUser>> UpdateUserAsync(IdentityUser updateUser, string[] roles, string? password);
    Task<OperationResult<string>> DeleteUserAsync(string userId);
    Task<OperationResult<IdentityUser>> GetUserByEmailAsync(string email);
    Task<OperationResult<IdentityUser>> GetUserByIdAsync(string userId);
    Task<string> GeneratePasswordResetTokenAsync(IdentityUser userResultValue);
}