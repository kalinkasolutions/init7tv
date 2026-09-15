using System.ComponentModel.DataAnnotations;
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
            }

            result.Add(await ToGetUserDto(user));
        }

        return OperationResult<GetUserDto[]>.Success(result.ToArray());
    }

    public async Task<OperationResult<string[]>> GetRolesNamesAsync()
    {
        return OperationResult<string[]>.Success(await m_identityRepository.GetRoleNamesAsync());
    }

    public async Task<OperationResult<GetUserDto>> AddUserAsync(AddUserDto addUserDto)
    {
        var validation = Validate<GetUserDto>(addUserDto);
        if (validation != null)
        {
            return validation;
        }

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
            return addUserResult.MapError<GetUserDto>();
        }

        return await GetUserByEmailAsync(addUserDto.Email);
    }

    public async Task<OperationResult<GetUserDto>> UpdateUserAsync(string userId, UpdateUserDto updateUserDto)
    {
        var validation = Validate<GetUserDto>(updateUserDto);
        if (validation != null)
        {
            return validation;
        }

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
            return updateResult.MapError<GetUserDto>();
        }

        return await GetUserById(updateResult.Value.Id);
    }

    public Task<OperationResult<string>> DeleteUserAsync(string userId)
    {
        return m_identityRepository.DeleteUserAsync(userId);
    }

    public async Task<OperationResult<string>> GetPasswordResetTokenAsync(string email)
    {
        var userResult = await m_identityRepository.GetUserByEmailAsync(email);
        if (!userResult.IsSuccess)
        {
            return userResult.MapError<string>();
        }

        return OperationResult<string>.Success(await m_identityRepository.GeneratePasswordResetTokenAsync(userResult.Value));
    }

    private async Task<OperationResult<GetUserDto>> GetUserByEmailAsync(string email)
    {
        var userResult = await m_identityRepository.GetUserByEmailAsync(email);
        if (!userResult.IsSuccess)
        {
            return userResult.MapError<GetUserDto>();
        }

        return OperationResult<GetUserDto>.Success(await ToGetUserDto(userResult.Value));
    }

    private async Task<OperationResult<GetUserDto>> GetUserById(string userId)
    {
        var userResult = await m_identityRepository.GetUserByIdAsync(userId);
        if (!userResult.IsSuccess)
        {
            return userResult.MapError<GetUserDto>();
        }

        return OperationResult<GetUserDto>.Success(await ToGetUserDto(userResult.Value));
    }

    /// <summary>Returns null when the payload is valid.</summary>
    private static OperationResult<T>? Validate<T>(object dto)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true))
        {
            return null;
        }

        return OperationResult<T>.Invalid(string.Join(", ", results.Select(x => x.ErrorMessage)));
    }

    private async Task<GetUserDto> ToGetUserDto(IdentityUser user)
    {
        var roles = await m_identityRepository.GetRolesForUserAsync(user);

        return new GetUserDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Roles = roles,
            IsAdmin = roles.Contains(Init7TvRoles.Admin)
        };
    }
}