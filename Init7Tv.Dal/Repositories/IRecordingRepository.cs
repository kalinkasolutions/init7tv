using Init7Tv.Dal.Entities;

namespace Init7Tv.Dal.Repositories;

public interface IRecordingRepository
{
    Task<Recording[]> GetForUserAsync(string userName);
    Task<Recording[]> GetAllAsync();
    Task<Recording?> GetByIdAsync(Guid recordingId);

    /// <summary>Everything the scheduler still has work to do on.</summary>
    Task<Recording[]> GetUnfinishedAsync();

    /// <summary>The rows of one attempt, which is one row per person who picked it.</summary>
    Task<Recording[]> GetByDirectoryAsync(string directory);

    /// <summary>The rows of several captures at once, for working out who shares one.</summary>
    Task<Recording[]> GetByDirectoriesAsync(string[] directories);

    /// <summary>
    /// When each programme was last attempted, so a pass does not start one twice. Compared against
    /// when the pick was made rather than treated as a flat "already done": asking again after
    /// stopping one is a new request and deserves a new attempt, while a pick that predates its own
    /// failed attempt must not set it off again every pass.
    /// </summary>
    Task<Dictionary<Guid, DateTime>> GetLatestAttemptsAsync(Guid[] programmeIds);

    Task AddRangeAsync(Recording[] recordings);
    Task SaveAsync(Recording[] recordings);
    Task RemoveAsync(Recording recording);
}
