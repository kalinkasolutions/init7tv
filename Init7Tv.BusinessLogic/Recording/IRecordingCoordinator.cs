namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// The decisions a pass makes, kept out of the hosted service so they can be tested without hosting
/// anything.
/// </summary>
public interface IRecordingCoordinator
{
    /// <summary>
    /// Works out what became of recordings the process did not outlive. Runs once, before the first
    /// pass, because a restart always leaves rows claiming to be recording with no process behind them.
    /// </summary>
    Task ReconcileAsync();

    /// <summary>
    /// One pass over the schedule: starts everything whose time has come, finishes everything whose
    /// capture has ended, and reports when the caller should look again.
    ///
    /// A pass only reads one sleep ahead, so what it reports is the earliest moment inside that window
    /// — see <see cref="RecordingSweepResult"/> for why sleeping to the window's end when it holds
    /// nothing is still safe.
    ///
    /// <paramref name="now"/> is passed in rather than read here so a test can sweep at a chosen moment
    /// without waiting for one.
    /// </summary>
    Task<RecordingSweepResult> SweepAsync(DateTime now);
}
