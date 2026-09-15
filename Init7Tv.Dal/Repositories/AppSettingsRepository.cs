using Init7Tv.Dal.Entities;
using Init7Tv.Shared;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Dal.Repositories;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private readonly Init7TvContext m_context;

    public AppSettingsRepository(Init7TvContext context)
    {
        m_context = context;
    }

    public async Task CreateAppSettingsAsync()
    {
        var existing = await m_context.AppSettings.OrderBy(x => x.Id).ToArrayAsync();

        if (existing.Length == 0)
        {
            m_context.AppSettings.Add(new AppSettings());
            await m_context.SaveChangesAsync();
            return;
        }

        // earlier versions appended a blank row on every startup
        if (existing.Length > 1)
        {
            m_context.AppSettings.RemoveRange(existing.Skip(1));
            await m_context.SaveChangesAsync();
        }
    }

    public async Task<OperationResult<AppSettings>> GetAppSettingsAsync()
    {
        var appSettings = await m_context.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (appSettings == null)
        {
            return OperationResult<AppSettings>.NotFound("Failed to find app settings");
        }

        return OperationResult<AppSettings>.Success(appSettings);
    }

    public async Task<OperationResult<AppSettings>> UpdateEmailAppSettingsAsync(AppSettings update)
    {
        var existingResult = await GetAppSettingsAsync();
        if (!existingResult.IsSuccess)
        {
            return existingResult;
        }

        var existing = existingResult.Value;

        existing.EmailFrom = update.EmailFrom;
        existing.SmtpHost = update.SmtpHost;
        existing.Port = update.Port;
        existing.Username = update.Username;
        existing.Password = update.Password;
        existing.EnableSsl = update.EnableSsl;
        existing.SecureSocketOptions = update.SecureSocketOptions;

        await m_context.SaveChangesAsync();

        return OperationResult<AppSettings>.Success(existing);
    }

    public async Task<OperationResult<AppSettings>> UpdateGeneralSettingsAsync(AppSettings update)
    {
        var existingResult = await GetAppSettingsAsync();
        if (!existingResult.IsSuccess)
        {
            return existingResult;
        }

        var existing = existingResult.Value;

        existing.BaseDomain = update.BaseDomain;
        existing.FfmpegPreset = update.FfmpegPreset;
        existing.FfmpegLogLevel = update.FfmpegLogLevel;

        await m_context.SaveChangesAsync();

        return OperationResult<AppSettings>.Success(existing);
    }
}