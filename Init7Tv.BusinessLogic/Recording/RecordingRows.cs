using Init7Tv.Dal.Entities;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// The rows behind a capture. One capture per programme however many people asked for it, and one
/// row per person, so what each of them ends up with is whatever the capture ends up holding.
/// </summary>
public static class RecordingRows
{
    public static RecordingRow[] New(
        IEnumerable<PlannedRecording> plans,
        string directory,
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime now
    )
    {
        return plans.Select(plan => new RecordingRow
        {
            RecordingId = Guid.NewGuid(),
            ProgrammeId = plan.ProgrammeId,
            UserName = plan.UserName,
            ChannelId = plan.ChannelId,
            ChannelName = plan.ChannelName,
            CanonicalName = plan.CanonicalName,
            Title = plan.Title,
            SubTitle = plan.SubTitle,
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledEnd,
            State = RecordingState.Pending,
            Directory = directory,
            CreatedAt = now
        }).ToArray();
    }

    /// <summary>
    /// Rows for whoever has picked it since the capture started, and nothing for those who already
    /// have one. The same window, the same directory and the same state: it is one capture.
    /// </summary>
    public static RecordingRow[] Joining(
        IEnumerable<PlannedRecording> plans,
        IReadOnlyList<RecordingRow> rows,
        DateTime now
    )
    {
        var already = rows.Select(row => row.UserName).ToHashSet();
        var running = rows[0];

        return plans
            .Where(plan => !already.Contains(plan.UserName))
            .Select(plan => new RecordingRow
            {
                RecordingId = Guid.NewGuid(),
                ProgrammeId = running.ProgrammeId,
                UserName = plan.UserName,
                ChannelId = running.ChannelId,
                ChannelName = running.ChannelName,
                CanonicalName = running.CanonicalName,
                Title = running.Title,
                SubTitle = running.SubTitle,
                ScheduledStart = running.ScheduledStart,
                ScheduledEnd = running.ScheduledEnd,
                StartedAt = running.StartedAt,
                State = running.State,
                Directory = running.Directory,
                CreatedAt = now
            })
            .ToArray();
    }
}
