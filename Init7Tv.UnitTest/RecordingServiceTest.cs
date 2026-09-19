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

        m_service = new RecordingService(
            m_repository.Object,
            m_engine.Object,
            Mock.Of<IChannelService>(),
            NullLogger<RecordingService>.Instance,
            Options.Create(new Init7TvOptions { RecordingPath = m_root }));
    }

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

        var file = await m_service.GetFileAsync(recording.RecordingId, Somebody, isAdmin: false);
        var deleted = await m_service.DeleteAsync(recording.RecordingId, Somebody, isAdmin: false);

        Assert.That(file.ResultCode, Is.EqualTo(ResultCode.NotFound));
        Assert.That(deleted.ResultCode, Is.EqualTo(ResultCode.NotFound));
    }

    [Test]
    public async Task AnAdminReachesAnybodysRecording()
    {
        var recording = Completed();

        var file = await m_service.GetFileAsync(recording.RecordingId, Somebody, isAdmin: true);

        Assert.That(file.IsSuccess, Is.True);
        Assert.That(file.Value.Path, Is.EqualTo(RecordingFiles.FinalPath(recording.Directory)));
    }

    [Test]
    public async Task ARecordingStillRunningHasNothingToPlay()
    {
        var recording = Completed();
        recording.State = RecordingState.Recording;

        var file = await m_service.GetFileAsync(recording.RecordingId, Owner, isAdmin: false);

        Assert.That(file.ResultCode, Is.EqualTo(ResultCode.Invalid));
    }

    /// <summary>A row edited by hand must not be able to read anywhere it likes.</summary>
    [Test]
    public async Task ARecordingPointingOutsideTheRootIsRefused()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"init7tv-elsewhere-{Guid.NewGuid():N}");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(RecordingFiles.FinalPath(elsewhere), "not yours");

        try
        {
            var recording = Completed();
            recording.Directory = elsewhere;

            var file = await m_service.GetFileAsync(recording.RecordingId, Owner, isAdmin: false);

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

        var file = await m_service.GetFileAsync(recording.RecordingId, Owner, isAdmin: false);

        Assert.That(file.Value.DownloadName, Does.EndWith(".mp4"));
        Assert.That(file.Value.DownloadName, Does.Not.Contain(Path.DirectorySeparatorChar));
        Assert.That(file.Value.DownloadName, Does.Not.Contain('/'));
    }

    /// <summary>A row can say completed before the size is written back, and a
    /// play button on a file of nothing only produces an error.</summary>
    [Test]
    public async Task ARecordingWithNoFileYetIsNotOfferedForPlaying()
    {
        var recording = Completed();
        recording.FileSizeBytes = 0;

        m_repository.Setup(x => x.GetForUserAsync(Owner)).ReturnsAsync([recording]);

        var listed = await m_service.GetAsync(Owner, isAdmin: false);

        Assert.That(listed.Value.Single().IsPlayable, Is.False);
    }

    [Test]
    public async Task AnAdminSeesEverybodysRecordings()
    {
        m_repository.Setup(x => x.GetAllAsync()).ReturnsAsync([Completed(), Completed(Somebody)]);

        var listed = await m_service.GetAsync(Owner, isAdmin: true);

        Assert.That(listed.Value, Has.Length.EqualTo(2));
        m_repository.Verify(x => x.GetForUserAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>Writes the file too, because everything here depends on it being there.</summary>
    private RecordingRow Completed(string userName = Owner, string? directory = null)
    {
        directory ??= RecordingFiles.DirectoryFor(m_root, Guid.NewGuid());
        Directory.CreateDirectory(directory);
        File.WriteAllText(RecordingFiles.FinalPath(directory), "pretend this is an mp4");

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
