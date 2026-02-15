using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic.AppSettingsService;

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IAppSettingsRepository m_appSettingsRepository;
    private readonly IMemoryCache m_memoryCache;

    public AppSettingsService(
        IAppSettingsRepository appSettingsRepository,
        IMemoryCache memoryCache
    )
    {
        m_appSettingsRepository = appSettingsRepository;
        m_memoryCache = memoryCache;
    }

    public async Task<OperationResult<EmailAppSettingsDto>> GetEmailAppSettingsAsync()
    {
        var appSettings = await GetAppSettings();
        if (!appSettings.IsSuccess)
        {
            return appSettings.MapError<EmailAppSettingsDto>();
        }

        return OperationResult<EmailAppSettingsDto>.Success(appSettings.Value.ToDto());
    }

    public async Task<OperationResult<EmailAppSettingsDto>> UpdateEmailAppSettingsAsync(EmailAppSettingsDto emailAppSettings)
    {
        m_memoryCache.Remove(nameof(AppSettings));
        var updateResult = await m_appSettingsRepository.UpdateEmailAppSettingsAsync(emailAppSettings.ToEntity());
        if (!updateResult.IsSuccess)
        {
            return updateResult.MapError<EmailAppSettingsDto>();
        }

        return OperationResult<EmailAppSettingsDto>.Success(updateResult.Value.ToDto());
    }

    public async Task<OperationResult<GeneralAppSettingsDto>> GetGeneralSettingsAsync()
    {
        var appSettings = await GetAppSettings();
        if (!appSettings.IsSuccess)
        {
            return appSettings.MapError<GeneralAppSettingsDto>();
        }

        return OperationResult<GeneralAppSettingsDto>.Success(appSettings.Value.ToGeneralDto());
    }

    public async Task<OperationResult<GeneralAppSettingsDto>> UpdateGeneralSettingsAsync(GeneralAppSettingsDto appSettingsDto)
    {
        m_memoryCache.Remove(nameof(AppSettings));
        var updateResult = await m_appSettingsRepository.UpdateGeneralSettingsAsync(appSettingsDto.ToEntity());
        if (!updateResult.IsSuccess)
        {
            return updateResult.MapError<GeneralAppSettingsDto>();
        }

        return OperationResult<GeneralAppSettingsDto>.Success(updateResult.Value.ToGeneralDto());
    }

    private async Task<OperationResult<AppSettings>> GetAppSettings()
    {
        if (m_memoryCache.TryGetValue(nameof(AppSettings), out AppSettings? appSettings) && appSettings != null)
        {
            return OperationResult<AppSettings>.Success(appSettings);
        }

        return await m_appSettingsRepository.GetAppSettingsAsync();
    }
}