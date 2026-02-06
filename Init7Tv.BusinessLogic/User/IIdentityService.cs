using Init7Tv.Dto.Admin;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.User;

public interface IIdentityService
{
    Task<OperationResult<GetUserDto[]>> GetUsersAsync();
    Task<OperationResult<string[]>> GetRolesNamesAsync();
    Task<OperationResult<GetUserDto>> AddUserAsync(AddUserDto addUserDto);
    Task<OperationResult<GetUserDto>> UpdateUserAsync(string userId, UpdateUserDto updateUserDto);
    Task<OperationResult<string>> DeleteUserAsync(string userId);
}