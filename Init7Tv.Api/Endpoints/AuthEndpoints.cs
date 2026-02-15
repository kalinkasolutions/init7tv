using Init7Tv.BusinessLogic.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Init7Tv.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/login", Login).DisableAntiforgery();
        app.MapPost("/send-reset-password-mail", SendResetPasswordMailAsync).DisableAntiforgery();
        app.MapPost("/reset-password", ResetPasswordAsync).DisableAntiforgery();
        app.MapPost("/logout", Logout);
    }

    private static async Task<IResult> Login(
        [FromForm] string username,
        [FromForm] string password,
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager
    )
    {
        var user = await userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Redirect("/login.html?error=invalid");
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            password,
            isPersistent: true,
            lockoutOnFailure: false
        );

        if (result.Succeeded)
        {
            return Results.Redirect("/");
        }

        return Results.Redirect("/login.html?error=invalid");
    }

    private static async Task<IResult> SendResetPasswordMailAsync(
        [FromForm] string email,
        UserManager<IdentityUser> userManager,
        IEmailService emailService
    )
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return Results.Redirect("login.html");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        await emailService.SendResetPasswordMailAsync(email, email, token);

        return Results.Redirect("login.html");
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromForm] string email,
        [FromForm] string token,
        [FromForm] string password,
        UserManager<IdentityUser> userManager
    )
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return Results.Redirect("/login.html");
        }

        await userManager.ResetPasswordAsync(user, token, password);

        return Results.Redirect("/login.html?reset=success");
    }

    private static async Task<IResult> Logout(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/login.html");
    }
}