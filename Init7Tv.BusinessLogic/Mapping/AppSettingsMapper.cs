using Init7Tv.Dal.Entities;
using Init7Tv.Dto.Settings;

namespace Init7Tv.BusinessLogic.Mapping;

public static class AppSettingsMapper
{
    public static EmailAppSettingsDto ToDto(this AppSettings appSettings)
    {
        return new EmailAppSettingsDto
        {
            EmailFrom = appSettings.EmailFrom,
            Port = appSettings.Port,
            SmtpHost = appSettings.SmtpHost,
            Username = appSettings.Username,
            // write-only: never sent back to the browser, blank on update means "keep"
            Password = string.Empty,
            SecureSocketOptions = appSettings.SecureSocketOptions,
        };
    }

    public static GeneralAppSettingsDto ToGeneralDto(this AppSettings appSettings)
    {
        return new GeneralAppSettingsDto
        {
            BaseDomain = appSettings.BaseDomain,
            FfmpegPreset = appSettings.FfmpegPreset,
            FfmpegLogLevel = appSettings.FfmpegLogLevel,
            RecordingPreset = appSettings.RecordingPreset,
            RecordingPreRollMinutes = appSettings.RecordingPreRollMinutes,
            RecordingPostRollMinutes = appSettings.RecordingPostRollMinutes
        };
    }

    public static AppSettings ToEntity(this GeneralAppSettingsDto appSettingsDto)
    {
        return new AppSettings
        {
            BaseDomain = appSettingsDto.BaseDomain,
            FfmpegLogLevel = appSettingsDto.FfmpegLogLevel,
            FfmpegPreset =  appSettingsDto.FfmpegPreset,
            RecordingPreset = appSettingsDto.RecordingPreset,
            RecordingPreRollMinutes = appSettingsDto.RecordingPreRollMinutes,
            RecordingPostRollMinutes = appSettingsDto.RecordingPostRollMinutes,
        };
    }

    public static AppSettings ToEntity(this EmailAppSettingsDto emailAppSettingsDto)
    {
        return new AppSettings
        {
            EmailFrom = emailAppSettingsDto.EmailFrom,
            Port = emailAppSettingsDto.Port,
            SmtpHost = emailAppSettingsDto.SmtpHost,
            Username = emailAppSettingsDto.Username,
            Password = emailAppSettingsDto.Password,
            SecureSocketOptions = emailAppSettingsDto.SecureSocketOptions,
        };
    }
}