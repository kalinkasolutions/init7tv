using Init7Tv.Dto.Settings;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.AppSettingsService;

public interface IAppSettingsService
{
    Task<OperationResult<EmailAppSettingsDto>> GetEmailAppSettingsAsync();
    Task<OperationResult<EmailAppSettingsDto>> UpdateEmailAppSettingsAsync(EmailAppSettingsDto emailAppSettings);
    Task<OperationResult<GeneralAppSettingsDto>> GetGeneralSettingsAsync();
    Task<OperationResult<GeneralAppSettingsDto>> UpdateGeneralSettingsAsync(GeneralAppSettingsDto appSettingsDto);
}