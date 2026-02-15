using Init7Tv.Dal.Entities;
using Init7Tv.Shared;

namespace Init7Tv.Dal.Repositories;

public interface IAppSettingsRepository
{
    Task CreateAppSettingsAsync();
    Task<OperationResult<AppSettings>> GetAppSettingsAsync();
    Task<OperationResult<AppSettings>> UpdateEmailAppSettingsAsync(AppSettings update);
    Task<OperationResult<AppSettings>> UpdateGeneralSettingsAsync(AppSettings update);
}