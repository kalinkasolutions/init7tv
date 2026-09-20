using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Recording;

public interface IPlannedRecordingService
{
    Task<OperationResult<PlannedRecordingDto[]>> GetAsync(string userName, bool isAdmin);
    Task<OperationResult<PlannedRecordingDto>> PlanAsync(string userName, PlannedRecordingDto recording);
    /// <summary>
    /// Drops one pick. <paramref name="owner"/> is whose it is, which only an admin may make
    /// anybody but themselves: they are shown everybody's picks, and a list that cannot be acted
    /// on is worse than no list at all.
    /// </summary>
    Task<OperationResult<bool>> CancelAsync(string userName, bool isAdmin, Guid programmeId, string owner);
}
