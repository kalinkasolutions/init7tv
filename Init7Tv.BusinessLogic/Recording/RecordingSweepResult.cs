namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// What one pass over the schedule found: the two moments the loop needs to work out its sleep.
///
/// <see cref="NextFireAt"/> is the earliest moment something has to happen <em>within the window this
/// pass read</em> — a pick's padded start, or the end of a recording that has to be stopped — and is
/// null when the window holds nothing more. <see cref="Horizon"/> is where that window ends, so a null
/// next moment is not "sleep forever" but "sleep until the window runs out and look again".
///
/// Sleeping to <c>NextFireAt ?? Horizon</c> is therefore always safe: nothing beyond the horizon can
/// come due before the next pass reads it, and anything that arrives inside the window rings
/// <see cref="RecordingSignal"/> and cuts the sleep short.
/// </summary>
public sealed record RecordingSweepResult(DateTime? NextFireAt, DateTime Horizon);
