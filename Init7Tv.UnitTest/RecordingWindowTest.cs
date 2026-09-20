using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dal.Entities;
using Init7Tv.Dto.Settings;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.UnitTest;

public class RecordingWindowTest
{
    private static readonly DateTime Now = new(2026, 9, 20, 20, 0, 0, DateTimeKind.Utc);

    private static PlannedRecording Pick(
        DateTime startsAt,
        DateTime endsAt,
        Guid? programmeId = null,
        DateTime? plannedAt = null,
        string userName = "niggi"
    ) => new()
    {
        ProgrammeId = programmeId ?? Guid.NewGuid(),
        UserName = userName,
        StartsAt = startsAt,
        EndsAt = endsAt,
        PlannedAt = plannedAt ?? startsAt.AddHours(-2),
        Title = "Tagesschau"
    };

    private static RecordingRow Row(Guid programmeId, RecordingState state, DateTime scheduledEnd) => new()
    {
        RecordingId = Guid.NewGuid(),
        ProgrammeId = programmeId,
        UserName = "niggi",
        State = state,
        ScheduledStart = scheduledEnd.AddHours(-1),
        ScheduledEnd = scheduledEnd
    };

    [Test]
    public void ATimeTheDatabaseForgotTheKindOfIsReadAsUtc()
    {
        // Sqlite hands them back unmarked, and taking one for local time moves it by hours
        var unmarked = new DateTime(2026, 9, 20, 20, 0, 0, DateTimeKind.Unspecified);

        var utc = RecordingWindow.AsUtc(unmarked);

        Assert.Multiple(() =>
        {
            Assert.That(utc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(utc, Is.EqualTo(Now));
        });
    }

    [Test]
    public void ALocalTimeIsTurnedIntoTheInstantItMeans()
    {
        var local = Now.ToLocalTime();

        Assert.That(RecordingWindow.AsUtc(local), Is.EqualTo(Now));
    }

    [Test]
    public void ANegativeRollIsTakenAsNone()
    {
        // a hand-edited setting must not pull a start earlier than it was asked for
        var settings = new GeneralAppSettingsDto { RecordingPreRollMinutes = -5, RecordingPostRollMinutes = -5 };

        Assert.Multiple(() =>
        {
            Assert.That(RecordingWindow.PreRoll(settings), Is.EqualTo(TimeSpan.Zero));
            Assert.That(RecordingWindow.PostRoll(settings), Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void APickInsideItsPreRollIsDue()
    {
        var pick = Pick(Now.AddMinutes(3), Now.AddHours(1));

        var due = RecordingWindow.Due([pick], TimeSpan.FromMinutes(5), TimeSpan.Zero, Now);

        Assert.That(due, Is.EqualTo(new[] { pick }));
    }

    [Test]
    public void APickStillBeyondItsPreRollIsNot()
    {
        var pick = Pick(Now.AddMinutes(30), Now.AddHours(1));

        Assert.That(RecordingWindow.Due([pick], TimeSpan.FromMinutes(5), TimeSpan.Zero, Now), Is.Empty);
    }

    [Test]
    public void APickStillInsideItsPostRollIsStillDue()
    {
        // it ended two minutes ago, and the post-roll says to keep going for five
        var pick = Pick(Now.AddHours(-1), Now.AddMinutes(-2));

        Assert.That(
            RecordingWindow.Due([pick], TimeSpan.Zero, TimeSpan.FromMinutes(5), Now), Is.Not.Empty);
    }

    [Test]
    public void APickWhosePaddedWindowHasPassedIsNot()
    {
        var pick = Pick(Now.AddHours(-2), Now.AddMinutes(-10));

        Assert.That(RecordingWindow.Due([pick], TimeSpan.Zero, TimeSpan.FromMinutes(5), Now), Is.Empty);
    }

    [Test]
    public void APickWithNoKindOnItIsStillJudgedAsUtc()
    {
        var pick = Pick(
            DateTime.SpecifyKind(Now.AddMinutes(3), DateTimeKind.Unspecified),
            DateTime.SpecifyKind(Now.AddHours(1), DateTimeKind.Unspecified));

        Assert.That(
            RecordingWindow.Due([pick], TimeSpan.FromMinutes(5), TimeSpan.Zero, Now), Is.Not.Empty);
    }

    [Test]
    public void AProgrammeAttemptedSinceItWasAskedForIsNotStartedAgain()
    {
        var programmeId = Guid.NewGuid();
        var pick = Pick(Now, Now.AddHours(1), programmeId, plannedAt: Now.AddHours(-2));
        var attempts = new Dictionary<Guid, DateTime> { [programmeId] = Now.AddHours(-1) };

        Assert.That(RecordingWindow.AlreadyAttempted(attempts, Group(pick)), Is.True);
    }

    [Test]
    public void PickingItAgainAfterTheAttemptCountsAsAskingAgain()
    {
        var programmeId = Guid.NewGuid();
        var pick = Pick(Now, Now.AddHours(1), programmeId, plannedAt: Now.AddMinutes(-1));
        var attempts = new Dictionary<Guid, DateTime> { [programmeId] = Now.AddHours(-1) };

        Assert.That(RecordingWindow.AlreadyAttempted(attempts, Group(pick)), Is.False);
    }

    [Test]
    public void AProgrammeNeverAttemptedIsNotAlreadyAttempted()
    {
        var pick = Pick(Now, Now.AddHours(1));

        Assert.That(RecordingWindow.AlreadyAttempted(new Dictionary<Guid, DateTime>(), Group(pick)), Is.False);
    }

    [Test]
    public void TheLatestOfSeveralPicksIsWhatTheAttemptIsHeldAgainst()
    {
        // one person picked it before the attempt and another after: the second is
        // somebody asking again and the programme is started afresh
        var programmeId = Guid.NewGuid();
        var attempts = new Dictionary<Guid, DateTime> { [programmeId] = Now.AddHours(-1) };

        var group = new[]
        {
            Pick(Now, Now.AddHours(1), programmeId, plannedAt: Now.AddHours(-2), userName: "niggi"),
            Pick(Now, Now.AddHours(1), programmeId, plannedAt: Now.AddMinutes(-1), userName: "sami")
        }.GroupBy(x => x.ProgrammeId).Single();

        Assert.That(RecordingWindow.AlreadyAttempted(attempts, group), Is.False);
    }

    private static IGrouping<Guid, PlannedRecording> Group(PlannedRecording pick) =>
        new[] { pick }.GroupBy(x => x.ProgrammeId).Single();

    [Test]
    public void TheNextMomentIsAPicksPaddedStart()
    {
        var pick = Pick(Now.AddMinutes(20), Now.AddHours(1));

        var next = RecordingWindow.NextMoment(
            [], [pick], TimeSpan.FromMinutes(5), Now, Now.AddHours(1));

        Assert.That(next, Is.EqualTo(Now.AddMinutes(15)));
    }

    [Test]
    public void AnEmptyScheduleHasNoNextMoment()
    {
        Assert.That(
            RecordingWindow.NextMoment([], [], TimeSpan.Zero, Now, Now.AddHours(1)), Is.Null);
    }

    [Test]
    public void APickBeyondTheHorizonIsNotWaitedFor()
    {
        // the next pass redraws the window and finds it then
        var pick = Pick(Now.AddHours(3), Now.AddHours(4));

        Assert.That(
            RecordingWindow.NextMoment([], [pick], TimeSpan.Zero, Now, Now.AddHours(1)), Is.Null);
    }

    [Test]
    public void AMomentAlreadyPastIsNotWaitedFor()
    {
        // it would hand the caller a zero delay and the loop would spin on it
        var pick = Pick(Now.AddMinutes(-10), Now.AddHours(1));

        Assert.That(
            RecordingWindow.NextMoment([], [pick], TimeSpan.Zero, Now, Now.AddHours(1)), Is.Null);
    }

    [Test]
    public void ARunningRecordingIsWaitedForUntilItsWindowRunsOut()
    {
        var row = Row(Guid.NewGuid(), RecordingState.Recording, Now.AddMinutes(20));

        var next = RecordingWindow.NextMoment([row], [], TimeSpan.Zero, Now, Now.AddHours(1));

        Assert.That(next, Is.EqualTo(Now.AddMinutes(20) + RecordingWindow.OverrunGrace));
    }

    [Test]
    public void ARecordingNotYetStartedIsNotWaitedForOnItsEnd()
    {
        // only one that is running can outstay its window
        var row = Row(Guid.NewGuid(), RecordingState.Pending, Now.AddMinutes(20));

        Assert.That(RecordingWindow.NextMoment([row], [], TimeSpan.Zero, Now, Now.AddHours(1)), Is.Null);
    }

    [Test]
    public void APickAlreadyClaimedByARowIsNotCountedAsStillToCome()
    {
        // the pass has taken it on, and its start has been and gone
        var programmeId = Guid.NewGuid();
        var row = Row(programmeId, RecordingState.Pending, Now.AddHours(1));
        var pick = Pick(Now.AddMinutes(20), Now.AddHours(1), programmeId);

        Assert.That(RecordingWindow.NextMoment([row], [pick], TimeSpan.Zero, Now, Now.AddHours(1)), Is.Null);
    }

    [Test]
    public void TheEarliestOfSeveralMomentsIsTheOneToWaitFor()
    {
        var soon = Pick(Now.AddMinutes(10), Now.AddHours(1));
        var later = Pick(Now.AddMinutes(40), Now.AddHours(2));
        var running = Row(Guid.NewGuid(), RecordingState.Recording, Now.AddMinutes(25));

        var next = RecordingWindow.NextMoment(
            [running], [later, soon], TimeSpan.Zero, Now, Now.AddHours(1));

        Assert.That(next, Is.EqualTo(Now.AddMinutes(10)));
    }
}
