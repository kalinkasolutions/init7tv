using Init7Tv.BusinessLogic.Email;
using Init7Tv.BusinessLogic.User;
using Init7Tv.Dal.Extensions;
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
        var user = await userManager.FindByNameAsync(username) ?? await userManager.FindByEmailAsync(username);
        if (user == null)
        {
            return Results.Redirect("/login.html?error=invalid");
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            password,
            isPersistent: true,
            lockoutOnFailure: true
        );

        if (result.Succeeded)
        {
            return Results.Redirect("/");
        }

        if (result.IsLockedOut)
        {
            return Results.Redirect("/login.html?error=locked");
        }

        return Results.Redirect("/login.html?error=invalid");
    }

    private static async Task<IResult> SendResetPasswordMailAsync(
        [FromForm] string email,
        IIdentityService identityService,
        IEmailService emailService
    )
    {
        var tokenResult = await identityService.GetPasswordResetTokenAsync(email);
        if (!tokenResult.IsSuccess)
        {
            return Results.Redirect("login.html");
        }

        await emailService.SendResetPasswordMailAsync(email, tokenResult.Value);
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

        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            // keep email and token so a rejected password can just be retyped
            return Results.Redirect(
                $"/resetPassword.html?email={Uri.EscapeDataString(email)}" +
                $"&token={Uri.EscapeDataString(token)}" +
                $"&error={Uri.EscapeDataString(result.ToErrorString())}");
        }

        return Results.Redirect("/login.html?reset=success");
    }

    private static async Task<IResult> Logout(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/login.html");
    }
}