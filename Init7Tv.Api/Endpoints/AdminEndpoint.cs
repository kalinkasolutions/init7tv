using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.BusinessLogic.Email;
using Init7Tv.BusinessLogic.User;
using Init7Tv.Dto;
using Init7Tv.Dto.Admin;
using Init7Tv.Dto.Settings;
using Init7Tv.Extensions;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Authorization;

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
        group.MapPut("/update-user/{id}", UpdateUserAsync);
        group.MapDelete("/delete-user/{id}", DeleteUserAsync);
        group.MapGet("/get-email-app-settings", GetEmailAppSettings);
        group.MapPut("/update-email-app-settings", UpdateEmailAppSettingsAsync);
        group.MapGet("/get-general-app-settings", GetGeneralSettingsAsync);
        group.MapPut("/update-general-app-settings", UpdateGeneralAppSettingsAsync);
        group.MapGet("/send-test-mail", SendTestMail);
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

    private static async Task<IResult> UpdateUserAsync(
        string id,
        UpdateUserDto updateUserDto,
        IIdentityService identityService
    )
    {
        return (await identityService.UpdateUserAsync(id, updateUserDto)).ToHttpResult();
    }

    private static async Task<IResult> DeleteUserAsync(string id, IIdentityService identityService)
    {
        return (await identityService.DeleteUserAsync(id)).ToHttpResult();
    }

    private static async Task<IResult> GetEmailAppSettings(IAppSettingsService appSettingsService)
    {
        return (await appSettingsService.GetEmailAppSettingsAsync()).ToHttpResult();
    }

    private static async Task<IResult> UpdateEmailAppSettingsAsync(EmailAppSettingsDto emailAppSettingsDto, IAppSettingsService appSettingsService)
    {
        return (await appSettingsService.UpdateEmailAppSettingsAsync(emailAppSettingsDto)).ToHttpResult();
    }

    private static async Task<IResult> GetGeneralSettingsAsync(IAppSettingsService appSettingsService)
    {
        return (await appSettingsService.GetGeneralSettingsAsync()).ToHttpResult();
    }

    private static async Task<IResult> UpdateGeneralAppSettingsAsync(GeneralAppSettingsDto appSettingsDto, IAppSettingsService appSettingsService)
    {
        return (await appSettingsService.UpdateGeneralSettingsAsync(appSettingsDto)).ToHttpResult();
    }

    private static async Task<IResult> SendTestMail(IEmailService emailService)
    {
        return (await emailService.SendTestMailAsync()).ToHttpResult();
    }
}