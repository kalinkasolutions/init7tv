using Init7Tv.BusinessLogic.Recording;

namespace Init7Tv.UnitTest;

/// <summary>
/// The scheduler's doorbell. It needs no fixture — it is the one piece of the recording feature that
/// touches neither the database nor a process.
/// </summary>
public class RecordingSignalTest
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    [Test]
    public async Task AnUnrungSignalSimplyTimesOut()
    {
        var signal = new RecordingSignal();

        Assert.That(await signal.WaitAsync(NoWait, CancellationToken.None), Is.False);
    }

    /// <summary>
    /// The whole point: a pick arriving between working out the sleep and starting it must not be
    /// slept through. The permit is banked, so the wait returns at once.
    /// </summary>
    [Test]
    public async Task ARingThatLandsBeforeTheWaitIsStillHeard()
    {
        var signal = new RecordingSignal();

        signal.Signal();

        Assert.That(await signal.WaitAsync(NoWait, CancellationToken.None), Is.True);
    }

    /// <summary>
    /// Binary rather than counting: the loop re-reads the whole schedule when it wakes, so a second
    /// wake-up would only repeat a query it has already run.
    /// </summary>
    [Test]
    public async Task SeveralRingsWakeTheLoopOnce()
    {
        var signal = new RecordingSignal();

        signal.Signal();
        signal.Signal();
        signal.Signal();

        Assert.That(await signal.WaitAsync(NoWait, CancellationToken.None), Is.True);
        Assert.That(await signal.WaitAsync(NoWait, CancellationToken.None), Is.False);
    }

    [Test]
    public void ACancelledWaitThrowsSoTheLoopCanStop()
    {
        var signal = new RecordingSignal();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(
            () => signal.WaitAsync(TimeSpan.FromMinutes(1), cancellation.Token));
    }
}
