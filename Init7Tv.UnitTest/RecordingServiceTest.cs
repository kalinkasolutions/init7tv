using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.UnitTest;

public class RecordingServiceTest
{
    private const string Owner = "niggi";
    private const string Somebody = "sami";

    private string m_root = null!;
    private Mock<IRecordingRepository> m_repository = null!;
    private Mock<IPlannedRecordingRepository> m_planned = null!;
    private Mock<IRecordingEngine> m_engine = null!;
    private RecordingService m_service = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"init7tv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);

        m_repository = new Mock<IRecordingRepository>();
        m_repository.Setup(x => x.GetByDirectoryAsync(It.IsAny<string>())).ReturnsAsync([]);
        m_repository.Setup(x => x.RemoveAsync(It.IsAny<RecordingRow>())).Returns(Task.CompletedTask);

        m_engine = new Mock<IRecordingEngine>();
        m_planned = new Mock<IPlannedRecordingRepository>();

        m_service = ServiceWith(new RecordingSegmentCache());
    }

    private RecordingService ServiceWith(IRecordingSegmentCache segments) => new(
        m_repository.Object,
        m_planned.Object,
        new RecordingSignal(),
        m_engine.Object,
        Mock.Of<IChannelService>(),
        segments,
        NullLogger<RecordingService>.Instance,
        Options.Create(new Init7TvOptions { RecordingPath = m_root }));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, recursive: true);
        }
    }

    /// <summary>Saying "not yours" would say it exists, and there is nothing to gain by it.</summary>
    [Test]
    public async Task SomebodyElsesRecordingIsSimplyNotThere()
    {
        var recording = Completed();

        var file = await m_service.GetDownloadAsync(recording.RecordingId, withoutAds: false, Somebody, isAdmin: false);
        var deleted = await m_service.DeleteAsync(recording.RecordingId, Somebody, isAdmin: false);

        Assert.That(file.ResultCode, Is.EqualTo(ResultCode.NotFound));
        Assert.That(deleted.ResultCode, Is.EqualTo(ResultCode.NotFound));
    }

    [Test]
    public async Task AnAdminReachesAnybodysRecording()
    {
        var recording = Completed();

        var file = await m_service.GetDownloadAsync(recording.RecordingId, withoutAds: false, Somebody, isAdmin: true);

        Assert.That(file.IsSuccess, Is.True);
        Assert.That(file.Value.Parts, Is.Not.Empty);
    }

    [Test]
    public async Task ARecordingStillRunningHasNothingToPlay()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;

        var file = await m_service.GetDownloadAsync(recording.RecordingId, withoutAds: false, Owner, isAdmin: false);

        Assert.That(file.IsSuccess, Is.True, "a capture under way is still worth handing over");
    }

    /// <summary>A row edited by hand must not be able to read anywhere it likes.</summary>
    [Test]
    public async Task ARecordingPointingOutsideTheRootIsRefused()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"init7tv-elsewhere-{Guid.NewGuid():N}");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllBytes(RecordingFiles.CapturePath(elsewhere, 1), new byte[4096]);

        try
        {
            var recording = Completed();
            recording.Directory = elsewhere;

            var file = await m_service.GetDownloadAsync(
                recording.RecordingId, withoutAds: false, Owner, isAdmin: false);

            Assert.That(file.ResultCode, Is.EqualTo(ResultCode.NotFound));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>Two people share one file, so it only goes when the last of them does.</summary>
    [Test]
    public async Task DeletingOneOfTwoRowsLeavesTheFileAlone()
    {
        var recording = Completed();
        m_repository
            .Setup(x => x.GetByDirectoryAsync(recording.Directory))
            .ReturnsAsync([Completed(Somebody, recording.Directory)]);

        await m_service.DeleteAsync(recording.RecordingId, Owner, isAdmin: false);

        Assert.That(Directory.Exists(recording.Directory), Is.True);
    }

    [Test]
    public async Task DeletingTheLastRowTakesTheFileWithIt()
    {
        var recording = Completed();

        await m_service.DeleteAsync(recording.RecordingId, Owner, isAdmin: false);

        m_repository.Verify(x => x.RemoveAsync(recording), Times.Once);
        Assert.That(Directory.Exists(recording.Directory), Is.False);
    }

    /// <summary>
    /// Otherwise the encode runs on for the rest of the programme against a directory nothing points
    /// at, holding a core and a slot, and not even freeing the disk until it exits.
    /// </summary>
    [Test]
    public async Task DeletingOneStillRecordingStopsTheCaptureToo()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;

        await m_service.DeleteAsync(recording.RecordingId, Owner, isAdmin: false);

        m_engine.Verify(x => x.Stop(RecordingFiles.CaptureIdOf(recording.Directory)), Times.Once);
    }

    /// <summary>
    /// The index is keyed by the directory and outlives the files unless it is told: an index for a
    /// recording that has been deleted is held for as long as the process runs and can never be
    /// asked for again.
    /// </summary>
    [Test]
    public async Task DeletingOneDropsTheSegmentIndexWithIt()
    {
        var segments = new Mock<IRecordingSegmentCache>();
        var recording = Completed();

        await ServiceWith(segments.Object).DeleteAsync(recording.RecordingId, Owner, isAdmin: false);

        segments.Verify(x => x.Forget(recording.Directory), Times.Once);
    }

    /// <summary>The other person still wants it, so their capture must keep running.</summary>
    [Test]
    public async Task DeletingOneOfTwoRowsLeavesTheCaptureRunning()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;
        m_repository
            .Setup(x => x.GetByDirectoryAsync(recording.Directory))
            .ReturnsAsync([Completed(Somebody, recording.Directory)]);

        await m_service.DeleteAsync(recording.RecordingId, Owner, isAdmin: false);

        m_engine.Verify(x => x.Stop(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>The title is only ever a file name here, and it comes from an api
    /// that puts slashes and colons in them.</summary>
    [Test]
    public async Task TheDownloadNameCannotEscapeIntoAPath()
    {
        var recording = Completed();
        recording.Title = "Tatort: Der Fall 1/2";

        var file = await m_service.GetDownloadAsync(recording.RecordingId, withoutAds: false, Owner, isAdmin: false);

        Assert.That(file.Value.DownloadName, Does.EndWith(".mp4"));
        Assert.That(file.Value.DownloadName, Does.Not.Contain(Path.DirectorySeparatorChar));
        Assert.That(file.Value.DownloadName, Does.Not.Contain('/'));
    }

    /// <summary>
    /// Files can go without the row hearing about it. One that says otherwise offers a play button
    /// that opens an empty player and a download that answers with an error page.
    /// </summary>
    [Test]
    public async Task ARecordingWhoseFileHasGoneSaysSoRatherThanOfferingIt()
    {
        var recording = Completed();
        File.Delete(RecordingFiles.CapturePath(recording.Directory, 1));

        m_repository.Setup(x => x.GetForUserAsync(Owner)).ReturnsAsync([recording]);

        var listed = (await m_service.GetAsync(Owner, isAdmin: false)).Value.Single();

        Assert.Multiple(() =>
        {
            Assert.That(listed.IsPlayable, Is.False);
            Assert.That(listed.State, Is.EqualTo("Missing"));
            Assert.That(listed.FileSizeBytes, Is.Zero, "a size nothing backs up is worse than none");
            Assert.That(listed.ErrorMessage, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ARecordingWhoseFileIsThereIsOfferedNormally()
    {
        var recording = Completed();
        m_repository.Setup(x => x.GetForUserAsync(Owner)).ReturnsAsync([recording]);

        var listed = (await m_service.GetAsync(Owner, isAdmin: false)).Value.Single();

        Assert.That(listed.IsPlayable, Is.True);
        Assert.That(listed.State, Is.EqualTo("Completed"));
    }

    /// <summary>
    /// The page offers a download without the advertising only when there is some to leave out.
    /// Counted when the recording finished, so listing costs nothing.
    /// </summary>
    [Test]
    public async Task WhatWasAnnouncedIsCarriedToThePage()
    {
        var recording = Completed();
        recording.AdBreakCount = 3;
        m_repository.Setup(x => x.GetForUserAsync(Owner)).ReturnsAsync([recording]);

        var listed = (await m_service.GetAsync(Owner, isAdmin: false)).Value.Single();

        Assert.That(listed.AdBreakCount, Is.EqualTo(3));
    }

    [Test]
    public async Task ARecordingWhoseFileHasGoneOffersNothingToCutEither()
    {
        var recording = Completed();
        recording.AdBreakCount = 3;
        File.Delete(RecordingFiles.CapturePath(recording.Directory, 1));

        m_repository.Setup(x => x.GetForUserAsync(Owner)).ReturnsAsync([recording]);

        var listed = (await m_service.GetAsync(Owner, isAdmin: false)).Value.Single();

        Assert.That(listed.AdBreakCount, Is.Zero);
    }

    [Test]
    public async Task AnAdminSeesEverybodysRecordings()
    {
        m_repository.Setup(x => x.GetAllAsync()).ReturnsAsync([Completed(), Completed(Somebody)]);

        var listed = await m_service.GetAsync(Owner, isAdmin: true);

        Assert.That(listed.Value, Has.Length.EqualTo(2));
        m_repository.Verify(x => x.GetForUserAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Otherwise the pick is still inside its window with no attempt against it, and the next pass
    /// starts the recording over.
    /// </summary>
    [Test]
    public async Task DeletingAlsoTakesThePickBackSoItDoesNotStartAgain()
    {
        var recording = Completed();

        await m_service.DeleteAsync(recording.RecordingId, Owner, isAdmin: false);

        m_planned.Verify(x => x.RemoveAsync(Owner, recording.ProgrammeId), Times.Once);
    }

    /// <summary>Stopping is one viewer letting go, and must not take anybody else's pick with it.</summary>
    [Test]
    public async Task StoppingOnlyTakesBackYourOwnPick()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;

        // somebody else still wants it
        m_planned.Setup(x => x.AnyForProgrammeAsync(recording.ProgrammeId)).ReturnsAsync(true);

        await m_service.StopAsync(recording.RecordingId, Owner, isAdmin: false);

        m_planned.Verify(x => x.RemoveAsync(Owner, recording.ProgrammeId), Times.Once);
        m_planned.Verify(x => x.RemoveAsync(It.Is<string>(u => u != Owner), It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>With somebody else still waiting it cannot simply end, so their share is cut instead.</summary>
    [Test]
    public async Task StoppingAShareLeavesItFinalizingForThePassToCut()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;
        m_planned.Setup(x => x.AnyForProgrammeAsync(recording.ProgrammeId)).ReturnsAsync(true);

        await m_service.StopAsync(recording.RecordingId, Owner, isAdmin: false);

        Assert.That(recording.State, Is.EqualTo(RecordingState.Finalizing));
        Assert.That(recording.EndedAt, Is.Not.Null);
    }

    /// <summary>The last one out ends the capture, which the pass does when no pick is left.</summary>
    [Test]
    public async Task StoppingTheLastShareLeavesItRecordingForThePassToEnd()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;
        m_planned.Setup(x => x.AnyForProgrammeAsync(recording.ProgrammeId)).ReturnsAsync(false);

        await m_service.StopAsync(recording.RecordingId, Owner, isAdmin: false);

        Assert.That(recording.State, Is.EqualTo(RecordingState.Recording));
    }

    /// <summary>Writes the capture too, because everything here depends on it being there.</summary>
    private RecordingRow Completed(string userName = Owner, string? directory = null)
    {
        directory ??= RecordingFiles.DirectoryFor(m_root, Guid.NewGuid());
        Directory.CreateDirectory(directory);

        // a recording is kept as the transport stream it was captured as
        File.WriteAllBytes(RecordingFiles.CapturePath(directory, 1), new byte[4096]);

        var recording = new RecordingRow
        {
            RecordingId = Guid.NewGuid(),
            ProgrammeId = Guid.NewGuid(),
            UserName = userName,
            ChannelName = "SRF 1 FHD",
            Title = "Tagesschau",
            State = RecordingState.Completed,
            Directory = directory,
            FileSizeBytes = 22,
            ScheduledStart = DateTime.UtcNow.AddHours(-1),
            ScheduledEnd = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        m_repository.Setup(x => x.GetByIdAsync(recording.RecordingId)).ReturnsAsync(recording);

        return recording;
    }
}
