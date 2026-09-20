namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// The scheduler's doorbell, and the only thing it keeps in memory. A singleton on purpose: the
/// services that ring it are scoped, and a field there would be born and die with a single request.
///
/// There is deliberately no schedule here. The database is the schedule: the loop asks it what is due
/// and when the next thing falls due, every time it wakes. Nothing can drift out of sync with a
/// schedule that is never copied, which is why callers need only say "something changed" and never
/// what changed.
/// </summary>
public sealed class RecordingSignal
{
    // Binary rather than counting: several picks landing while the loop sleeps only need to wake it
    // once, because it re-reads the whole schedule when it wakes.
    private readonly SemaphoreSlim m_changed = new(0, 1);

    /// <summary>Wakes the loop, if it is asleep, so it takes a fresh look at the schedule.</summary>
    public void Signal()
    {
        // Only bank a permit if none is waiting. The race with another ringer is harmless — losing it
        // means a permit is already pending, which is exactly the outcome we wanted.
        if (m_changed.CurrentCount > 0)
        {
            return;
        }

        try
        {
            m_changed.Release();
        }
        catch (SemaphoreFullException)
        {
            // Another ringer got there first; one wake-up is enough.
        }
    }

    /// <summary>
    /// Sleeps until <paramref name="delay"/> elapses or someone rings, whichever comes first.
    ///
    /// A ring that landed *before* this call banked a permit, so it returns immediately; that is what
    /// closes the gap between working out how long to sleep and going to sleep.
    /// </summary>
    /// <returns>True if someone rang, false if the delay simply elapsed.</returns>
    public Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        m_changed.WaitAsync(delay, cancellationToken);
}
