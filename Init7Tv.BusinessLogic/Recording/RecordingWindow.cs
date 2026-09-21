using Init7Tv.Dal.Entities;
using Init7Tv.Dto.Settings;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// The time arithmetic behind the schedule: how far one pass looks, which picks its window catches,
/// and when it has to come back.
///
/// All of it works off what it is handed, so a pass is decided by the rows and the clock rather than
/// by anything remembered between passes.
/// </summary>
public static class RecordingWindow
{
    /// <summary>
    /// How far ahead one pass reads, and so the longest it sleeps in one go. A pick set inside this
    /// window rings the doorbell, so this only bounds the damage from a suspended machine or a
    /// system-clock jump: one late look rather than an arbitrarily long oversleep.
    /// </summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(1);

    /// <summary>ffmpeg ends itself with -t; this is how long after that we stop waiting.</summary>
    public static readonly TimeSpan OverrunGrace = TimeSpan.FromMinutes(1);

    public static TimeSpan PreRoll(GeneralAppSettingsDto settings) =>
        TimeSpan.FromMinutes(Math.Max(settings.RecordingPreRollMinutes, 0));

    public static TimeSpan PostRoll(GeneralAppSettingsDto settings) =>
        TimeSpan.FromMinutes(Math.Max(settings.RecordingPostRollMinutes, 0));

    /// Picks were stored in UTC, but Sqlite hands them back with no kind at all.
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>The picks whose padded window is open right now, which are the ones to act on.</summary>
    public static PlannedRecording[] Due(
        IEnumerable<PlannedRecording> plans,
        TimeSpan preRoll,
        TimeSpan postRoll,
        DateTime now
    )
    {
        return plans
            .Where(plan => now >= AsUtc(plan.StartsAt) - preRoll && now < AsUtc(plan.EndsAt) + postRoll)
            .ToArray();
    }

    /// <summary>
    /// Whether this programme has been had a go at since it was asked for. A pick made after the
    /// attempt is somebody asking again — having stopped the first one, say — and starts a new one.
    /// </summary>
    public static bool AlreadyAttempted(
        IReadOnlyDictionary<Guid, DateTime> attempts,
        IGrouping<Guid, PlannedRecording> group
    )
    {
        return attempts.TryGetValue(group.Key, out var attemptedAt)
               && attemptedAt >= group.Max(plan => AsUtc(plan.PlannedAt));
    }

    /// <summary>
    /// The earliest moment inside the window that something has to happen: a pick's padded start, or
    /// the point at which a capture that has outstayed its window gets stopped.
    ///
    /// Only what is still ahead counts: a moment left in the past would hand the caller a zero delay
    /// and spin on it.
    /// </summary>
    /// <param name="whileRecording">
    /// How soon to come back while something is being captured, whatever else the schedule holds.
    /// A recording with no end has nothing of its own to bring the loop back before the horizon,
    /// and the free space has to be looked at rather more often than once an hour.
    /// </param>
    public static DateTime? NextMoment(
        IReadOnlyList<RecordingRow> unfinished,
        IReadOnlyList<PlannedRecording> plans,
        TimeSpan preRoll,
        DateTime now,
        DateTime horizon,
        TimeSpan whileRecording
    )
    {
        var moments = new List<DateTime>();
        var claimed = new HashSet<Guid>();

        foreach (var row in unfinished)
        {
            claimed.Add(row.ProgrammeId);

            if (row.State == RecordingState.Recording)
            {
                moments.Add(row.ScheduledEnd + OverrunGrace);
                moments.Add(now + whileRecording);
            }
        }

        // a pick a row already stands for is being dealt with, and its start has been and gone
        foreach (var plan in plans.Where(plan => !claimed.Contains(plan.ProgrammeId)))
        {
            moments.Add(AsUtc(plan.StartsAt) - preRoll);
        }

        var ahead = moments.Where(moment => moment > now && moment <= horizon).ToArray();

        return ahead.Length == 0 ? null : ahead.Min();
    }
}
