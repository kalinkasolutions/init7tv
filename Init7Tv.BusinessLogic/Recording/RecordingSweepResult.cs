namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// What one pass over the schedule found: the two moments the loop needs to work out its sleep.
///
/// <see cref="NextFireAt"/> is the earliest moment something has to happen <em>within the window this
/// pass read</em> — a pick's padded start, or the end of a recording that has to be stopped — and is
/// null when the window holds nothing more. <see cref="Horizon"/> is where that window ends, so a null
/// next moment is not "sleep forever" but "sleep until the window runs out and look again".
/// </summary>
public sealed record RecordingSweepResult(DateTime? NextFireAt, DateTime Horizon)
{
    /// <summary>A floor on the sleep, so a horizon already past cannot turn the loop into a spin.</summary>
    public static readonly TimeSpan MinSleep = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long to sleep before the next pass. Sleeping to <c>NextFireAt ?? Horizon</c> is always
    /// safe: nothing beyond the horizon can come due before the next pass reads it, and anything
    /// arriving inside the window rings <see cref="RecordingSignal"/> and cuts the sleep short.
    ///
    /// Taking the horizon from the pass rather than adding it here is what keeps the two exactly
    /// aligned: measuring a fresh one from now would put the wake-up a whole pass past the window,
    /// leaving a sliver at the end that nothing had read.
    /// </summary>
    /// <param name="now">
    /// The current time rather than the pass's start: probing a channel takes seconds, and a pick
    /// that fell due while the pass was working should be taken up next turn rather than slept through.
    /// </param>
    public TimeSpan SleepFrom(DateTime now)
    {
        var delay = (NextFireAt ?? Horizon) - now;

        return delay < MinSleep ? MinSleep : delay;
    }
}
