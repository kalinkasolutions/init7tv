using Init7Tv.Dal.Entities;

namespace Init7Tv.Dal.Repositories;

public interface IPlannedRecordingRepository
{
    Task<PlannedRecording[]> GetForUserAsync(string userName);

    /// <summary>
    /// Everybody's picks that overlap the window, which is what the scheduler works from. Bounded
    /// rather than the whole table: a pick outlives the recording it asked for, so the table only
    /// grows, and a pass reads it every time it wakes.
    /// </summary>
    Task<PlannedRecording[]> GetInWindowAsync(DateTime from, DateTime to);
    /// <summary>Everybody's, for an admin looking over the lot.</summary>
    Task<PlannedRecording[]> GetAllAsync();

    /// <summary>Whether anybody at all is still waiting for this programme.</summary>
    Task<bool> AnyForProgrammeAsync(Guid programmeId);

    Task AddAsync(PlannedRecording recording);
    Task<bool> RemoveAsync(string userName, Guid programmeId);
}
