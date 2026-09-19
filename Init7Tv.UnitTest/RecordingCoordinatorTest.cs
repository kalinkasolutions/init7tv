using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.UnitTest;

public class RecordingCoordinatorTest
{
    private static readonly Guid SrfOne = Guid.NewGuid();

    private static readonly ChannelDto Channel = new()
    {
        ChannelId = SrfOne, DisplayName = "SRF 1 FHD", CanonicalName = "SRF1.ch"
    };

    private string m_root = null!;
    private Mock<IRecordingRepository> m_recordings = null!;
    private Mock<IPlannedRecordingRepository> m_planned = null!;
    private Mock<IRecordingEngine> m_engine = null!;
    private List<RecordingRow> m_added = null!;
    private RecordingCoordinator m_coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"init7tv-test-{Guid.NewGuid():N}");
        m_added = [];

        m_recordings = new Mock<IRecordingRepository>();
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([]);
        m_recordings.Setup(x => x.GetLatestAttemptsAsync(It.IsAny<Guid[]>())).ReturnsAsync([]);
        m_recordings.Setup(x => x.AddRangeAsync(It.IsAny<RecordingRow[]>()))
            .Callback<RecordingRow[]>(rows => m_added.AddRange(rows))
            .Returns(Task.CompletedTask);
        m_recordings.Setup(x => x.SaveAsync(It.IsAny<RecordingRow[]>())).Returns(Task.CompletedTask);

        m_planned = new Mock<IPlannedRecordingRepository>();
        m_planned.Setup(x => x.GetInWindowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync([]);

        m_engine = new Mock<IRecordingEngine>();
        m_engine.Setup(x => x.TakeFinished()).Returns([]);
        m_engine.Setup(x => x.StartAsync(It.IsAny<RecordingRequest>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        var channelService = new Mock<IChannelService>();
        channelService.Setup(x => x.GetChannelById(SrfOne))
            .ReturnsAsync(OperationResult<ChannelDto>.Success(Channel));

        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(x => x.GetGeneralSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralAppSettingsDto>.Success(new GeneralAppSettingsDto
            {
                RecordingPreset = "veryfast",
                FfmpegLogLevel = "warning",
                RecordingPreRollMinutes = 2,
                RecordingPostRollMinutes = 5
            }));

        m_coordinator = new RecordingCoordinator(
            NullLogger<RecordingCoordinator>.Instance,
            m_recordings.Object,
            m_planned.Object,
            channelService.Object,
            appSettings.Object,
            m_engine.Object,
            Mock.Of<IRecordingService>(x => x.GetCurrentAsync() == Task.FromResult(Array.Empty<CurrentRecordingDto>())),
            Mock.Of<IRecordingEventBus>(),
            Options.Create(new Init7TvOptions { RecordingPath = m_root, MaxConcurrentRecordings = 2 }));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, recursive: true);
        }
    }

    /// <summary>A pick is picked up during its pre-roll, not on the dot.</summary>
    [Test]
    public async Task APickInsideItsPreRollStarts()
    {
        Plan(DateTime.UtcNow.AddMinutes(1), "niggi");

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Once);
        Assert.That(m_added.Single().State, Is.EqualTo(RecordingState.Recording));
    }

    [Test]
    public async Task APickStillFarOffIsLeftAlone()
    {
        Plan(DateTime.UtcNow.AddMinutes(30), "niggi");

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);
        Assert.That(m_added, Is.Empty);
    }

    /// <summary>The post-roll is over, so there is nothing left to catch.</summary>
    [Test]
    public async Task APickWhosePaddedWindowHasPassedIsLeftAlone()
    {
        var starts = DateTime.UtcNow.AddHours(-2);
        PlanAll([Planned(starts, starts.AddHours(1), "niggi")]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);
    }

    /// <summary>Two people, one programme, one encoder and one file.</summary>
    [Test]
    public async Task TwoPeoplePickingTheSameProgrammeShareOneCapture()
    {
        var starts = DateTime.UtcNow.AddMinutes(1);
        var programmeId = Guid.NewGuid();

        PlanAll([
            Planned(starts, starts.AddHours(1), "niggi", programmeId),
            Planned(starts, starts.AddHours(1), "sami", programmeId)
        ]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Once);

        Assert.That(m_added, Has.Count.EqualTo(2));
        Assert.That(m_added.Select(x => x.Directory).Distinct().Count(), Is.EqualTo(1));
        Assert.That(m_added.Select(x => x.UserName), Is.EquivalentTo(new[] { "niggi", "sami" }));
    }

    /// <summary>A pick that predates its own attempt has had its go, however that went.</summary>
    [Test]
    public async Task AProgrammeAlreadyAttemptedIsNotStartedAgain()
    {
        var programmeId = Guid.NewGuid();
        Plan(DateTime.UtcNow.AddMinutes(1), "niggi", programmeId);

        Attempted(programmeId, DateTime.UtcNow);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);
    }

    /// <summary>
    /// Pressing record again after stopping one is somebody asking afresh, so it gets a fresh go
    /// rather than being told it has already been done.
    /// </summary>
    [Test]
    public async Task PickingItAgainAfterAnAttemptStartsANewOne()
    {
        var programmeId = Guid.NewGuid();
        var starts = DateTime.UtcNow.AddMinutes(1);

        // the attempt happened, then it was picked again
        Attempted(programmeId, DateTime.UtcNow.AddMinutes(-10));
        PlanAll([Planned(starts, starts.AddHours(1), "niggi", programmeId, DateTime.UtcNow)]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Once);
    }

    /// <summary>A recording finishing frees the slot, so waiting is better than
    /// claiming the programme and never coming back to it.</summary>
    [Test]
    public async Task TooManyAtOnceWaitsWhileThereIsStillProgrammeLeft()
    {
        m_engine.SetupGet(x => x.ActiveCount).Returns(2);
        Plan(DateTime.UtcNow.AddMinutes(1), "niggi");

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);
        Assert.That(m_added, Is.Empty);
    }

    /// <summary>Once there is nothing left to catch it says why, rather than
    /// leaving nothing at all on the page.</summary>
    [Test]
    public async Task TooManyAtOnceIsSkippedAndSaysSoOnceTheWindowIsGone()
    {
        m_engine.SetupGet(x => x.ActiveCount).Returns(2);

        // still inside the five minute post-roll, but with only seconds of it left
        var ends = DateTime.UtcNow.AddMinutes(-4.5);
        PlanAll([Planned(ends.AddHours(-1), ends, "niggi")]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        var row = m_added.Single();
        Assert.That(row.State, Is.EqualTo(RecordingState.Skipped));
        Assert.That(row.ErrorMessage, Is.Not.Empty);
    }

    [Test]
    public async Task ACaptureThatWillNotStartIsMarkedFailed()
    {
        m_engine.Setup(x => x.StartAsync(It.IsAny<RecordingRequest>()))
            .ReturnsAsync(OperationResult<bool>.Error("no such multicast"));

        Plan(DateTime.UtcNow.AddMinutes(1), "niggi");

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        Assert.That(m_added.Single().State, Is.EqualTo(RecordingState.Failed));
    }

    /// <summary>The padded window is what gets stored, so a restart works out the
    /// same end time again.</summary>
    [Test]
    public async Task ThePaddedWindowIsWhatIsStored()
    {
        var starts = DateTime.UtcNow.AddMinutes(1);
        PlanAll([Planned(starts, starts.AddHours(1), "niggi")]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        var row = m_added.Single();
        Assert.That(row.ScheduledStart, Is.EqualTo(starts.AddMinutes(-2)).Within(TimeSpan.FromSeconds(1)));
        Assert.That(row.ScheduledEnd, Is.EqualTo(starts.AddHours(1).AddMinutes(5)).Within(TimeSpan.FromSeconds(1)));
    }

    // --- coming back after a restart -------------------------------------

    /// <summary>Nothing survives a restart, so a row claiming to record is one
    /// with an ffmpeg that died with the process.</summary>
    [Test]
    public async Task ARestartInsideTheWindowGoesBackForTheRest()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(20));
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);

        await m_coordinator.ReconcileAsync();

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Once);
        m_engine.Verify(x => x.FinalizeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(row.State, Is.EqualTo(RecordingState.Recording));
    }

    /// <summary>Past the window there is nothing left to catch, so keep what there is.</summary>
    [Test]
    public async Task ARestartAfterTheWindowKeepsWhatWasCaptured()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(-5));
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);
        m_engine.Setup(x => x.FinalizeAsync(row.Directory, It.IsAny<string>()))
            .ReturnsAsync(OperationResult<long>.Success(1234));

        await m_coordinator.ReconcileAsync();

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);

        Assert.That(row.State, Is.EqualTo(RecordingState.Interrupted));
        Assert.That(row.FileSizeBytes, Is.EqualTo(1234));
    }

    [Test]
    public async Task ARestartWithNothingOnDiskIsAFailure()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(-5));
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);
        m_engine.Setup(x => x.FinalizeAsync(row.Directory, It.IsAny<string>()))
            .ReturnsAsync(OperationResult<long>.NotFound("Nothing was captured"));

        await m_coordinator.ReconcileAsync();

        Assert.That(row.State, Is.EqualTo(RecordingState.Failed));
        Assert.That(row.ErrorMessage, Is.Not.Empty);
    }

    // --- captures that ended ---------------------------------------------

    [Test]
    public async Task ACaptureThatRanItsCourseIsFinishedAndComplete()
    {
        var row = Leftover(DateTime.UtcNow.AddSeconds(-10), RecordingState.Recording);
        m_recordings.Setup(x => x.GetByDirectoryAsync(row.Directory)).ReturnsAsync([row]);
        m_engine.Setup(x => x.TakeFinished()).Returns([Finished(row, exitCode: 0)]);
        m_engine.Setup(x => x.FinalizeAsync(row.Directory, It.IsAny<string>()))
            .ReturnsAsync(OperationResult<long>.Success(99));

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        Assert.That(row.State, Is.EqualTo(RecordingState.Completed));
        Assert.That(row.EndedAt, Is.Not.Null);
    }

    /// <summary>ffmpeg also exits 0 when the source simply stops, so how far it
    /// got is what decides, not the exit code.</summary>
    [Test]
    public async Task ACaptureThatDiedEarlyGoesBackForTheRest()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(20), RecordingState.Recording);
        m_recordings.Setup(x => x.GetByDirectoryAsync(row.Directory)).ReturnsAsync([row]);
        m_engine.Setup(x => x.TakeFinished()).Returns([Finished(row, exitCode: 0)]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Once);
        m_engine.Verify(x => x.FinalizeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>One we stopped on purpose is finished where it is rather than restarted.</summary>
    [Test]
    public async Task ACaptureWeStoppedIsNotStartedAgain()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(20), RecordingState.Recording);
        m_recordings.Setup(x => x.GetByDirectoryAsync(row.Directory)).ReturnsAsync([row]);
        m_engine.Setup(x => x.TakeFinished()).Returns([Finished(row, exitCode: 255, stopped: true)]);
        m_engine.Setup(x => x.FinalizeAsync(row.Directory, It.IsAny<string>()))
            .ReturnsAsync(OperationResult<long>.Success(42));

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);
        Assert.That(row.State, Is.EqualTo(RecordingState.Interrupted));
    }

    /// <summary>-t should have ended it; a process still running well past the
    /// window is one that has stopped listening.</summary>
    [Test]
    public async Task ACaptureThatOutranItsWindowIsStopped()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(-5), RecordingState.Recording);
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);

        var captureId = RecordingFiles.CaptureIdOf(row.Directory);
        m_engine.Setup(x => x.IsRunning(captureId)).Returns(true);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.Stop(captureId), Times.Once);
    }

    /// <summary>
    /// Pressing record again on something already recording takes the pick back, and that has to
    /// stop it. What was captured is kept, so it ends up a clip of however long it ran.
    /// </summary>
    [Test]
    public async Task ACaptureNobodyWantsAnyMoreIsStopped()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(20), RecordingState.Recording);
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);

        var captureId = RecordingFiles.CaptureIdOf(row.Directory);
        m_engine.Setup(x => x.IsRunning(captureId)).Returns(true);

        // nothing planned: the pick behind it has been taken back
        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.Stop(captureId), Times.Once);
    }

    /// <summary>The pick is still there, so it must be left alone.</summary>
    [Test]
    public async Task ACaptureSomebodyStillWantsKeepsRunning()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(20), RecordingState.Recording);
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);

        var captureId = RecordingFiles.CaptureIdOf(row.Directory);
        m_engine.Setup(x => x.IsRunning(captureId)).Returns(true);

        var starts = row.ScheduledStart.AddMinutes(2);
        PlanAll([Planned(starts, row.ScheduledEnd, "niggi", row.ProgrammeId)]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.Stop(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// One picks it, the other picks the same programme once it is already recording. There is one
    /// capture, so the second must join it rather than set off a second encode of the same thing.
    /// </summary>
    [Test]
    public async Task PickingSomethingAlreadyRecordingJoinsIt()
    {
        var programmeId = Guid.NewGuid();
        var starts = DateTime.UtcNow.AddMinutes(-5);

        // niggi picked it first and it has been recording since
        var running = Leftover(DateTime.UtcNow.AddHours(1), RecordingState.Recording);
        running.ProgrammeId = programmeId;
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([running]);
        m_engine.Setup(x => x.IsRunning(RecordingFiles.CaptureIdOf(running.Directory))).Returns(true);
        Attempted(programmeId, running.CreatedAt);

        PlanAll([
            Planned(starts, starts.AddHours(1), "niggi", programmeId, running.CreatedAt.AddMinutes(-1)),
            Planned(starts, starts.AddHours(1), "sami", programmeId, DateTime.UtcNow)
        ]);

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(x => x.StartAsync(It.IsAny<RecordingRequest>()), Times.Never);
        Assert.That(m_added.Select(x => x.UserName), Is.EqualTo(new[] { "sami" }),
            "the second viewer should get a row on the capture that is already running");
        Assert.That(m_added.Select(x => x.Directory), Is.All.EqualTo(running.Directory));
    }

    /// <summary>
    /// Letting go of a capture somebody else is still waiting for gives you what it held at that
    /// moment, cut from the transport stream on disk. The capture itself is left alone.
    /// </summary>
    [Test]
    public async Task LettingGoOfASharedCaptureStillGivesYouYourShare()
    {
        var row = Leftover(DateTime.UtcNow.AddHours(1), RecordingState.Finalizing);
        row.StartedAt = DateTime.UtcNow.AddMinutes(-20);
        row.EndedAt = DateTime.UtcNow;
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);

        var captureId = RecordingFiles.CaptureIdOf(row.Directory);
        m_engine.Setup(x => x.IsRunning(captureId)).Returns(true);
        m_engine
            .Setup(x => x.ForkAsync(row.Directory, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult<long>.Success(4242));

        var theirs = row.Directory;

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(
            x => x.ForkAsync(theirs, It.IsAny<string>(), It.Is<TimeSpan>(t => t.TotalMinutes > 19), It.IsAny<string>()),
            Times.Once);
        m_engine.Verify(x => x.Stop(It.IsAny<Guid>()), Times.Never);

        Assert.That(row.State, Is.EqualTo(RecordingState.Interrupted));
        Assert.That(row.FileSizeBytes, Is.EqualTo(4242));
        Assert.That(row.Directory, Is.Not.EqualTo(theirs), "their share is a file of its own");
    }

    /// <summary>A capture that has exited is finished normally, not cut in two.</summary>
    [Test]
    public async Task AFinishedCaptureIsNotMistakenForSomebodyDroppingOut()
    {
        var row = Leftover(DateTime.UtcNow.AddMinutes(-5), RecordingState.Finalizing);
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);
        m_engine.Setup(x => x.FinalizeAsync(row.Directory, It.IsAny<string>()))
            .ReturnsAsync(OperationResult<long>.Success(1));

        await m_coordinator.SweepAsync(DateTime.UtcNow);

        m_engine.Verify(
            x => x.ForkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<string>()),
            Times.Never);
    }

    // --- when to look again ----------------------------------------------

    /// <summary>The loop sleeps to this, so a pick still to come is what it sleeps to.</summary>
    [Test]
    public async Task TheNextMomentIsAPicksPaddedStart()
    {
        var now = DateTime.UtcNow;
        var starts = now.AddMinutes(30);
        PlanAll([Planned(starts, starts.AddHours(1), "niggi")]);

        var swept = await m_coordinator.SweepAsync(now);

        // two minutes of pre-roll in the mocked settings
        Assert.That(swept.NextFireAt, Is.EqualTo(starts.AddMinutes(-2)));
    }

    /// <summary>Null is not "sleep forever": the loop sleeps to the horizon and looks again.</summary>
    [Test]
    public async Task AnEmptyScheduleHasNoNextMomentAndAHorizonToSleepTo()
    {
        var now = DateTime.UtcNow;

        var swept = await m_coordinator.SweepAsync(now);

        Assert.That(swept.NextFireAt, Is.Null);
        Assert.That(swept.Horizon, Is.GreaterThan(now));
    }

    /// <summary>Beyond the horizon is the next pass's business, not this one's.</summary>
    [Test]
    public async Task APickBeyondTheHorizonIsNotWaitedFor()
    {
        var now = DateTime.UtcNow;
        var starts = now.AddDays(2);
        PlanAll([Planned(starts, starts.AddHours(1), "niggi")]);

        var swept = await m_coordinator.SweepAsync(now);

        Assert.That(swept.NextFireAt, Is.Null);
    }

    /// <summary>A recording under way has to be looked at again when its window runs out.</summary>
    [Test]
    public async Task ARunningRecordingIsWaitedForUntilItsWindowRunsOut()
    {
        var now = DateTime.UtcNow;
        var row = Leftover(now.AddMinutes(10));
        m_recordings.Setup(x => x.GetUnfinishedAsync()).ReturnsAsync([row]);

        var swept = await m_coordinator.SweepAsync(now);

        // the grace after -t should have ended it by itself
        Assert.That(swept.NextFireAt, Is.EqualTo(row.ScheduledEnd.AddMinutes(1)));
    }

    /// <summary>A pick taken on by this very pass must not be waited for all over again.</summary>
    [Test]
    public async Task APickJustStartedIsNotCountedAsStillToCome()
    {
        var now = DateTime.UtcNow;
        var starts = now.AddMinutes(1);

        // short enough that the moment it has to be stopped is inside the horizon
        PlanAll([Planned(starts, starts.AddMinutes(20), "niggi")]);

        m_recordings
            .Setup(x => x.GetUnfinishedAsync())
            .ReturnsAsync(() => m_added.Where(x => x.State == RecordingState.Recording).ToArray());

        var swept = await m_coordinator.SweepAsync(now);

        Assert.That(m_added.Single().State, Is.EqualTo(RecordingState.Recording));
        Assert.That(swept.NextFireAt, Is.EqualTo(m_added.Single().ScheduledEnd.AddMinutes(1)));
    }

    // --- fixtures ---------------------------------------------------------

    private void Plan(DateTime starts, string userName, Guid? programmeId = null) =>
        PlanAll([Planned(starts, starts.AddHours(1), userName, programmeId)]);

    private void Attempted(Guid programmeId, DateTime at) =>
        m_recordings
            .Setup(x => x.GetLatestAttemptsAsync(It.IsAny<Guid[]>()))
            .ReturnsAsync(new Dictionary<Guid, DateTime> { [programmeId] = at });

    private void PlanAll(PlannedRecording[] plans) =>
        m_planned.Setup(x => x.GetInWindowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(plans);

    private static PlannedRecording Planned(
        DateTime starts,
        DateTime ends,
        string userName,
        Guid? programmeId = null,
        DateTime? plannedAt = null
    ) => new()
    {
        ProgrammeId = programmeId ?? Guid.NewGuid(),
        UserName = userName,
        ChannelId = SrfOne,
        ChannelName = Channel.DisplayName,
        CanonicalName = Channel.CanonicalName,
        Title = "Tagesschau",
        StartsAt = starts,
        EndsAt = ends,
        PlannedAt = plannedAt ?? DateTime.UtcNow.AddHours(-1)
    };

    private RecordingRow Leftover(DateTime scheduledEnd, RecordingState state = RecordingState.Recording) => new()
    {
        RecordingId = Guid.NewGuid(),
        ProgrammeId = Guid.NewGuid(),
        UserName = "niggi",
        ChannelId = SrfOne,
        ChannelName = Channel.DisplayName,
        CanonicalName = Channel.CanonicalName,
        Title = "Tagesschau",
        ScheduledStart = scheduledEnd.AddHours(-1),
        ScheduledEnd = scheduledEnd,
        State = state,
        Directory = RecordingFiles.DirectoryFor(m_root, Guid.NewGuid()),
        CreatedAt = DateTime.UtcNow
    };

    private static FinishedCapture Finished(RecordingRow row, int exitCode, bool stopped = false) => new()
    {
        CaptureId = RecordingFiles.CaptureIdOf(row.Directory),
        ExitCode = exitCode,
        Stopped = stopped,
        StartedAt = row.ScheduledStart,
        EndedAt = DateTime.UtcNow
    };
}
