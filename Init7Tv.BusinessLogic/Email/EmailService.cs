using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Init7Tv.BusinessLogic.Email;

public sealed class EmailService : IEmailService
{
    private readonly IAppSettingsService m_appSettingsService;
    private readonly ILogger<EmailService> m_logger;

    public EmailService(
        IAppSettingsService appSettingsService,
        ILogger<EmailService> logger
    )
    {
        m_appSettingsService = appSettingsService;
        m_logger = logger;
    }

    private async Task<OperationResult<MessageDto>> SendMailAsync(string subject, string recipient, string body)
    {
        var appSettings = (await m_appSettingsService.GetEmailAppSettingsAsync()).Value;
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(appSettings.EmailFrom));
        email.To.Add(MailboxAddress.Parse(recipient));
        email.Subject = subject;

        email.Body = new TextPart("html")
        {
            Text = body
        };
        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(
                appSettings.SmtpHost,
                appSettings.Port,
                (MailKit.Security.SecureSocketOptions)appSettings.SecureSocketOptions
            );

            await smtp.AuthenticateAsync(appSettings.Username, appSettings.Password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
            return OperationResult<MessageDto>.Success(new MessageDto { Message = $"Mail sent to: {recipient}" });
        }
        catch (Exception e)
        {
            m_logger.LogError(e, "Error sending email");
            return OperationResult<MessageDto>.Error(e.Message);
        }
    }

    public async Task<OperationResult<MessageDto>> SendTestMailAsync()
    {
        var appSettings = await m_appSettingsService.GetEmailAppSettingsAsync();
        if (!appSettings.IsSuccess)
        {
            return appSettings.MapError<MessageDto>();
        }

        var body = await LoadTemplate("TestEmailTemplate.html", new Dictionary<string, string>
        {
            ["Title"] = $"Hello {appSettings.Value.EmailFrom}"
        });

        return await SendMailAsync("Email settings test Init7Tv", appSettings.Value.EmailFrom, body);
    }

    public async Task<OperationResult<MessageDto>> SendResetPasswordMailAsync(string recipient, string resetToken)
    {
        var appSettings = await m_appSettingsService.GetGeneralSettingsAsync();
        if (!appSettings.IsSuccess)
        {
            return appSettings.MapError<MessageDto>();
        }

        var body = await LoadTemplate("ResetPasswordEmailTemplate.html", new Dictionary<string, string>()
        {
            ["Title"] = "Reset password request",
            ["ResetUrl"] = GetResetUrl(recipient, resetToken, appSettings)
        });
        return await SendMailAsync("Reset password request", recipient, body);
    }


    public async Task<OperationResult<MessageDto>> SendInviteEmailAsync(string recipient, string resetToken)
    {
        var appSettings = await m_appSettingsService.GetGeneralSettingsAsync();
        if (!appSettings.IsSuccess)
        {
            return appSettings.MapError<MessageDto>();
        }

        var body = await LoadTemplate("InviteUserTemplate.html", new Dictionary<string, string>()
        {
            ["Title"] = "You're invited",
            ["SetPasswordUrl"] = GetResetUrl(recipient, resetToken, appSettings)
        });

        return await SendMailAsync("Invite to Init7Tv", recipient, body);
    }

    private static string GetResetUrl(string recipient, string resetToken, OperationResult<GeneralAppSettingsDto> appSettings)
    {
        return $"{appSettings.Value.BaseDomain}/resetPassword.html?email={Uri.EscapeDataString(recipient)}&token={Uri.EscapeDataString(resetToken)}";
    }

    private async Task<string> LoadTemplate(string templateName, Dictionary<string, string> templateData)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "EmailTemplates", templateName);
        if (!File.Exists(templatePath))
        {
            m_logger.LogError("Email template not found {TemplatePath}", templatePath);
            return string.Empty;
        }

        var htmlBody = await File.ReadAllTextAsync(templatePath);
        foreach (var (key, value) in templateData)
        {
            htmlBody = htmlBody.Replace($"@{key}", value);
        }

        return htmlBody;
    }
}