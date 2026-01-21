using Init7Tv.BusinessLogic;
using Init7Tv.Dal;
using Init7Tv.Extensions;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv;

public static class Seed
{
    public static async Task Initialize(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<Init7TvContext>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Seed));
        await db.Database.MigrateAsync();

        foreach (var role in Init7TvRoles.Roles)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var addRoleRes = await roleManager.CreateAsync(new IdentityRole(role));
            if (!addRoleRes.Succeeded)
            {
                logger.LogError("Failed to seed roles: {Errors}", addRoleRes.ToErrorText());
            }
        }

        const string adminUserName = "admin";
        const string adminEmail = "admin@localhost";
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
            logger.LogError("Failed to add Admin: {Errors}", addAdmin.ToErrorText());
        }

        var addAdminRoleRes = await userManager.AddToRoleAsync(adminUser, Init7TvRoles.Admin);
        if (!addAdminRoleRes.Succeeded)
        {
            logger.LogError("Failed to add Admin to role {Role}: {Errors}", Init7TvRoles.Admin, addAdminRoleRes.ToErrorText());
        }
    }
}