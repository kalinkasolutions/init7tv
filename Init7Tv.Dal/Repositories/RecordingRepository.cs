using Init7Tv.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Dal.Repositories;

public sealed class RecordingRepository : IRecordingRepository
{
    private static readonly RecordingState[] UnfinishedStates =
        [RecordingState.Pending, RecordingState.Recording, RecordingState.Finalizing];

    private readonly Init7TvContext m_context;

    public RecordingRepository(Init7TvContext context)
    {
        m_context = context;
    }

    public async Task<Recording[]> GetForUserAsync(string userName)
    {
        return await m_context.Recordings
            .Where(x => x.UserName == userName)
            .OrderByDescending(x => x.ScheduledStart)
            .ToArrayAsync();
    }

    public async Task<Recording[]> GetAllAsync()
    {
        return await m_context.Recordings
            .OrderByDescending(x => x.ScheduledStart)
            .ToArrayAsync();
    }

    public async Task<Recording?> GetByIdAsync(Guid recordingId)
    {
        return await m_context.Recordings.FirstOrDefaultAsync(x => x.RecordingId == recordingId);
    }

    public async Task<Recording[]> GetUnfinishedAsync()
    {
        return await m_context.Recordings
            .Where(x => UnfinishedStates.Contains(x.State))
            .OrderBy(x => x.ScheduledStart)
            .ToArrayAsync();
    }

    public async Task<Recording[]> GetByDirectoryAsync(string directory)
    {
        return await m_context.Recordings
            .Where(x => x.Directory == directory)
            .ToArrayAsync();
    }

    public async Task<Dictionary<Guid, DateTime>> GetLatestAttemptsAsync(Guid[] programmeIds)
    {
        var attempts = await m_context.Recordings
            .Where(x => programmeIds.Contains(x.ProgrammeId))
            .GroupBy(x => x.ProgrammeId)
            .Select(group => new { ProgrammeId = group.Key, Latest = group.Max(x => x.CreatedAt) })
            .ToArrayAsync();

        return attempts.ToDictionary(x => x.ProgrammeId, x => x.Latest);
    }

    public async Task AddRangeAsync(Recording[] recordings)
    {
        m_context.Recordings.AddRange(recordings);
        await m_context.SaveChangesAsync();
    }

    public async Task SaveAsync(Recording[] recordings)
    {
        // they were read through this same context, so they are already tracked
        foreach (var recording in recordings)
        {
            if (m_context.Entry(recording).State == EntityState.Detached)
            {
                m_context.Recordings.Update(recording);
            }
        }

        await m_context.SaveChangesAsync();
    }

    public async Task RemoveAsync(Recording recording)
    {
        m_context.Recordings.Remove(recording);
        await m_context.SaveChangesAsync();
    }
}
