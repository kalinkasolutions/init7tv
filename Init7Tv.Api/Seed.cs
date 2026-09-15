using Init7Tv.Dal;
using Init7Tv.Dal.Extensions;
using Init7Tv.Dal.Repositories;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv;

public static class Seed
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<Init7TvContext>();
        await db.Database.MigrateAsync();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Seed));
        await AddInitialUsersAndRolesAsync(services, logger);
        await AddInitialSettingsRecordAsync(services);
    }

    private static async Task AddInitialUsersAndRolesAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in Init7TvRoles.Roles)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var addRoleRes = await roleManager.CreateAsync(new IdentityRole(role));
            if (!addRoleRes.Succeeded)
            {
                logger.LogError("Failed to seed roles: {Errors}", addRoleRes.ToErrorString());
            }
        }

        const string adminUserName = "admin";
        const string adminEmail = "admin@local";
        const string adminPassword = "admin";

        var adminUser = await userManager.FindByNameAsync(adminUserName);
        if (adminUser != null)
        {
            return;
        }

        adminUser = new IdentityUser
        {
            UserName = adminUserName,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var addAdmin = await userManager.CreateAsync(adminUser, adminPassword);
        if (!addAdmin.Succeeded)
        {
            logger.LogError("Failed to add Admin: {Errors}", addAdmin.ToErrorString());
        }

        var addAdminRoleRes = await userManager.AddToRoleAsync(adminUser, Init7TvRoles.Admin);
        if (!addAdminRoleRes.Succeeded)
        {
            logger.LogError("Failed to add Admin to role {Role}: {Errors}", Init7TvRoles.Admin, addAdminRoleRes.ToErrorString());
        }
    }

    private static async Task AddInitialSettingsRecordAsync(IServiceProvider services)
    {
        var appSettingsRepo = services.GetRequiredService<IAppSettingsRepository>();
        await appSettingsRepo.CreateAppSettingsAsync();
    }
}