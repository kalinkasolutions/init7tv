using Init7Tv.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Dal.Repositories;

public sealed class PlannedRecordingRepository : IPlannedRecordingRepository
{
    private readonly Init7TvContext m_context;

    public PlannedRecordingRepository(Init7TvContext context)
    {
        m_context = context;
    }

    public async Task<PlannedRecording[]> GetForUserAsync(string userName)
    {
        return await m_context.PlannedRecordings
            .Where(x => x.UserName == userName)
            .OrderBy(x => x.StartsAt)
            .ToArrayAsync();
    }

    public async Task<PlannedRecording[]> GetInWindowAsync(DateTime from, DateTime to)
    {
        return await m_context.PlannedRecordings
            .Where(x => x.EndsAt > from && x.StartsAt <= to)
            .OrderBy(x => x.StartsAt)
            .ToArrayAsync();
    }

    public async Task AddAsync(PlannedRecording recording)
    {
        var existing = await m_context.PlannedRecordings
            .FirstOrDefaultAsync(x => x.UserName == recording.UserName && x.ProgrammeId == recording.ProgrammeId);

        if (existing != null)
        {
            // asked for twice, which is not an error and not a second recording
            return;
        }

        m_context.PlannedRecordings.Add(recording);
        await m_context.SaveChangesAsync();
    }

    public async Task<bool> RemoveAsync(string userName, Guid programmeId)
    {
        var existing = await m_context.PlannedRecordings
            .FirstOrDefaultAsync(x => x.UserName == userName && x.ProgrammeId == programmeId);

        if (existing == null)
        {
            return false;
        }

        m_context.PlannedRecordings.Remove(existing);
        await m_context.SaveChangesAsync();
        return true;
    }
}
