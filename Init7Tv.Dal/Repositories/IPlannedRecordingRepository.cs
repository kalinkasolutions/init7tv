using Init7Tv.Dal.Entities;

namespace Init7Tv.Dal.Repositories;

public interface IPlannedRecordingRepository
{
    Task<PlannedRecording[]> GetForUserAsync(string userName);
    Task AddAsync(PlannedRecording recording);
    Task<bool> RemoveAsync(string userName, Guid programmeId);
}
