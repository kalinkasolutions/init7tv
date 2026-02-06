using Init7Tv.Dal.Repositories;
using Init7Tv.Dto.Admin;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.User;

public sealed class IdentityService : IIdentityService
{
    private readonly ILogger<IdentityService> m_logger;
    private readonly IIdentityRepository m_identityRepository;

    public IdentityService(
        ILogger<IdentityService> logger,
        IIdentityRepository identityRepository
    )
    {
        m_logger = logger;
        m_identityRepository = identityRepository;
    }

    public async Task<OperationResult<GetUserDto[]>> GetUsersAsync()
    {
        var users = await m_identityRepository.GetUsersAsync();
        var result = new List<GetUserDto>(users.Length);
        foreach (var user in users)
        {
            if (user.Email == null)
            {
                m_logger.LogWarning("User {UserId} does not have an email", user.Id);
                continue;
            }

            var getUserResult = await GetUserByEmailAsync(user.Email);
            if (!getUserResult.IsSuccess)
            {
                return OperationResult<GetUserDto[]>.Error(getUserResult.ErrorMessage);
            }

            result.Add(getUserResult.Value);
        }

        return OperationResult<GetUserDto[]>.Success(result.ToArray());
    }

    public async Task<OperationResult<string[]>> GetRolesNamesAsync()
    {
        return OperationResult<string[]>.Success(await m_identityRepository.GetRoleNamesAsync());
    }

    public async Task<OperationResult<GetUserDto>> AddUserAsync(AddUserDto addUserDto)
    {
        var addUserResult = await m_identityRepository.AddUserAsync(
            new IdentityUser
            {
                UserName = addUserDto.UserName,
                Email = addUserDto.Email
            },
            addUserDto.Roles.ToArray(),
            addUserDto.Password);

        if (!addUserResult.IsSuccess)
        {
            return OperationResult<GetUserDto>.Error(addUserResult.ErrorMessage);
        }

        return await GetUserByEmailAsync(addUserDto.Email);
    }

    public async Task<OperationResult<GetUserDto>> UpdateUserAsync(string userId, UpdateUserDto updateUserDto)
    {
        var updateResult = await m_identityRepository.UpdateUserAsync(
            new IdentityUser
            {
                Id = userId,
                Email = updateUserDto.Email,
                UserName = updateUserDto.UserName
            },
            updateUserDto.Roles.ToArray(),
            updateUserDto.Password
        );
        if (!updateResult.IsSuccess)
        {
            return OperationResult<GetUserDto>.Error(updateResult.ErrorMessage);
        }

        return await GetUserById(updateResult.Value.Id);
    }

    public Task<OperationResult<string>> DeleteUserAsync(string userId)
    {
        return m_identityRepository.DeleteUserAsync(userId);
    }

    private async Task<OperationResult<GetUserDto>> GetUserByEmailAsync(string email)
    {
        var userResult = await m_identityRepository.GetUserByEmailAsync(email);
        if (!userResult.IsSuccess)
        {
            return OperationResult<GetUserDto>.Error(userResult.ErrorMessage);
        }

        return await ToGetUserDto(userResult);
    }

    private async Task<OperationResult<GetUserDto>> GetUserById(string userId)
    {
        var userResult = await m_identityRepository.GetUserByIdAsync(userId);
        if (!userResult.IsSuccess)
        {
            return OperationResult<GetUserDto>.Error(userResult.ErrorMessage);
        }

        return await ToGetUserDto(userResult);
    }

    private async Task<OperationResult<GetUserDto>> ToGetUserDto(OperationResult<IdentityUser> userResult)
    {
        return OperationResult<GetUserDto>.Success(new GetUserDto
        {
            Id = userResult.Value.Id,
            UserName = userResult.Value.UserName,
            Email = userResult.Value.Email,
            Roles = await m_identityRepository.GetRolesForUserAsync(userResult.Value),
            IsAdmin = await m_identityRepository.IsAdminAsync(userResult.Value)
        });
    }
}