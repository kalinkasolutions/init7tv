using Init7Tv.Shared;

namespace Init7Tv.Dto.Settings;

public sealed class EmailAppSettingsDto
{
    public string EmailFrom { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public EmailSocketOptions SecureSocketOptions { get; set; }
}