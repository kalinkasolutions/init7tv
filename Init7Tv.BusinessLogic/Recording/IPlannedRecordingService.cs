using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Recording;

public interface IPlannedRecordingService
{
    Task<OperationResult<PlannedRecordingDto[]>> GetAsync(string userName, bool isAdmin);
    Task<OperationResult<PlannedRecordingDto>> PlanAsync(string userName, PlannedRecordingDto recording);
    Task<OperationResult<bool>> CancelAsync(string userName, Guid programmeId);
}
