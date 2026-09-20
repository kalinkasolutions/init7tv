using Init7Tv.BusinessLogic.Recording;

namespace Init7Tv.UnitTest;

public class RecordingSweepResultTest
{
    private static readonly DateTime Now = new(2026, 9, 20, 20, 0, 0, DateTimeKind.Utc);

    [Test]
    public void TheSleepReachesTheNextMomentThePassFound()
    {
        var result = new RecordingSweepResult(Now.AddMinutes(10), Now.AddHours(1));

        Assert.That(result.SleepFrom(Now), Is.EqualTo(TimeSpan.FromMinutes(10)));
    }

    [Test]
    public void AWindowWithNothingInItIsSleptToItsEnd()
    {
        // not "sleep forever": the window runs out and the schedule is read again
        var result = new RecordingSweepResult(NextFireAt: null, Now.AddHours(1));

        Assert.That(result.SleepFrom(Now), Is.EqualTo(TimeSpan.FromHours(1)));
    }

    [Test]
    public void TheHorizonIsTheOneThePassRead()
    {
        // measuring a fresh horizon from now would put the wake-up a whole pass
        // past the window, leaving a sliver at the end that nothing had read
        var result = new RecordingSweepResult(NextFireAt: null, Now.AddHours(1));

        Assert.That(result.SleepFrom(Now.AddMinutes(5)), Is.EqualTo(TimeSpan.FromMinutes(55)));
    }

    [Test]
    public void TimeSpentWorkingComesOffTheSleep()
    {
        // probing a channel takes seconds, and a pick that fell due while the pass
        // was working should be taken up next turn rather than slept through
        var result = new RecordingSweepResult(Now.AddMinutes(10), Now.AddHours(1));

        Assert.That(result.SleepFrom(Now.AddSeconds(30)), Is.EqualTo(TimeSpan.FromMinutes(9.5)));
    }

    [Test]
    public void AMomentAlreadyPastStillSleeps()
    {
        // a pass that took longer than the moment it found must not spin
        var result = new RecordingSweepResult(Now.AddMinutes(-5), Now.AddHours(1));

        Assert.That(result.SleepFrom(Now), Is.EqualTo(RecordingSweepResult.MinSleep));
    }

    [Test]
    public void AHorizonAlreadyPastStillSleeps()
    {
        var result = new RecordingSweepResult(NextFireAt: null, Now.AddSeconds(-1));

        Assert.That(result.SleepFrom(Now), Is.EqualTo(RecordingSweepResult.MinSleep));
    }
}
