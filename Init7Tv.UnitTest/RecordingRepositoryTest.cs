using Init7Tv.Dal;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.UnitTest;

public class RecordingRepositoryTest
{
    private static readonly DateTime Now = new(2026, 9, 20, 20, 0, 0, DateTimeKind.Utc);

    private SqliteConnection m_connection = null!;
    private Init7TvContext m_context = null!;
    private RecordingRepository m_repository = null!;

    [SetUp]
    public void SetUp()
    {
        // in-memory sqlite keeps the real provider behaviour, unlike the InMemory provider
        m_connection = new SqliteConnection("DataSource=:memory:");
        m_connection.Open();

        m_context = new Init7TvContext(
            new DbContextOptionsBuilder<Init7TvContext>().UseSqlite(m_connection).Options);
        m_context.Database.EnsureCreated();

        m_repository = new RecordingRepository(m_context);
    }

    [TearDown]
    public void TearDown()
    {
        m_context.Dispose();
        m_connection.Dispose();
    }

    private async Task<RecordingRow> Add(
        RecordingState state = RecordingState.Completed,
        string userName = "niggi",
        Guid? programmeId = null,
        string directory = "/recordings/abc",
        DateTime? scheduledStart = null,
        DateTime? createdAt = null
    )
    {
        var row = new RecordingRow
        {
            RecordingId = Guid.NewGuid(),
            ProgrammeId = programmeId ?? Guid.NewGuid(),
            UserName = userName,
            State = state,
            Directory = directory,
            ScheduledStart = scheduledStart ?? Now,
            ScheduledEnd = (scheduledStart ?? Now).AddHours(1),
            CreatedAt = createdAt ?? Now
        };

        await m_repository.AddRangeAsync([row]);

        return row;
    }

    [Test]
    public async Task AUserSeesOnlyTheirOwnRecordings()
    {
        await Add(userName: "niggi");
        await Add(userName: "sami");

        var mine = await m_repository.GetForUserAsync("niggi");

        Assert.That(mine.Select(x => x.UserName), Is.EqualTo(new[] { "niggi" }));
    }

    [Test]
    public async Task RecordingsComeBackNewestFirst()
    {
        // the page shows them in that order, and what was recorded last is what
        // somebody is most likely to be looking for
        await Add(scheduledStart: Now.AddHours(-2));
        await Add(scheduledStart: Now);
        await Add(scheduledStart: Now.AddHours(-1));

        var all = await m_repository.GetAllAsync();

        Assert.That(all.Select(x => x.ScheduledStart), Is.Ordered.Descending);
    }

    [TestCase(RecordingState.Pending)]
    [TestCase(RecordingState.Recording)]
    [TestCase(RecordingState.Finalizing)]
    public async Task AStateStillWaitingOnSomethingIsUnfinished(RecordingState state)
    {
        // the pass works from this list, so anything left out of it is never
        // looked at again: a finalizing row is how a dropped-out share is found
        await Add(state);

        Assert.That(await m_repository.GetUnfinishedAsync(), Has.Length.EqualTo(1));
    }

    [TestCase(RecordingState.Completed)]
    [TestCase(RecordingState.Interrupted)]
    [TestCase(RecordingState.Failed)]
    [TestCase(RecordingState.Skipped)]
    public async Task AStateThatIsOverIsNot(RecordingState state)
    {
        await Add(state);

        Assert.That(await m_repository.GetUnfinishedAsync(), Is.Empty);
    }

    [Test]
    public async Task UnfinishedRecordingsComeBackOldestFirst()
    {
        // the pass takes rows[0] as the group's window, so the order is load-bearing
        await Add(RecordingState.Recording, scheduledStart: Now.AddHours(1));
        await Add(RecordingState.Recording, scheduledStart: Now);

        var unfinished = await m_repository.GetUnfinishedAsync();

        Assert.That(unfinished.Select(x => x.ScheduledStart), Is.Ordered);
    }

    [Test]
    public async Task EverybodyOnACaptureIsFoundByItsDirectory()
    {
        await Add(userName: "niggi", directory: "/recordings/shared");
        await Add(userName: "sami", directory: "/recordings/shared");
        await Add(userName: "niggi", directory: "/recordings/other");

        var sharing = await m_repository.GetByDirectoryAsync("/recordings/shared");

        Assert.That(sharing.Select(x => x.UserName).Order(), Is.EqualTo(new[] { "niggi", "sami" }));
    }

    [Test]
    public async Task SeveralCapturesAreLookedUpAtOnce()
    {
        await Add(directory: "/recordings/one");
        await Add(directory: "/recordings/two");
        await Add(directory: "/recordings/three");

        var found = await m_repository.GetByDirectoriesAsync(["/recordings/one", "/recordings/three"]);

        Assert.That(found.Select(x => x.Directory).Order(), Is.EqualTo(new[] { "/recordings/one", "/recordings/three" }));
    }

    [Test]
    public async Task TheLatestAttemptIsTheOneThatCounts()
    {
        // a pick made after the attempt is somebody asking again, so it is the
        // most recent try the pass has to hold the pick against
        var programmeId = Guid.NewGuid();
        await Add(programmeId: programmeId, createdAt: Now.AddHours(-2));
        await Add(programmeId: programmeId, createdAt: Now.AddHours(-1));

        var attempts = await m_repository.GetLatestAttemptsAsync([programmeId]);

        Assert.That(attempts[programmeId], Is.EqualTo(Now.AddHours(-1)));
    }

    [Test]
    public async Task AProgrammeNeverAttemptedIsSimplyAbsent()
    {
        var attempts = await m_repository.GetLatestAttemptsAsync([Guid.NewGuid()]);

        Assert.That(attempts, Is.Empty);
    }

    [Test]
    public async Task AskingAboutNoProgrammesAsksNothing()
    {
        await Add();

        Assert.That(await m_repository.GetLatestAttemptsAsync([]), Is.Empty);
    }

    [Test]
    public async Task ARowReadThroughTheSameContextIsSavedAsItStands()
    {
        var row = await Add(RecordingState.Recording);

        var tracked = await m_repository.GetByIdAsync(row.RecordingId);
        tracked!.State = RecordingState.Completed;
        await m_repository.SaveAsync([tracked]);

        Assert.That((await m_repository.GetByIdAsync(row.RecordingId))!.State, Is.EqualTo(RecordingState.Completed));
    }

    [Test]
    public async Task ARowThatIsNoLongerTrackedIsStillSaved()
    {
        var row = await Add(RecordingState.Recording);
        m_context.ChangeTracker.Clear();

        row.State = RecordingState.Completed;
        await m_repository.SaveAsync([row]);

        m_context.ChangeTracker.Clear();
        Assert.That((await m_repository.GetByIdAsync(row.RecordingId))!.State, Is.EqualTo(RecordingState.Completed));
    }

    [Test]
    public async Task ARemovedRecordingIsGone()
    {
        var row = await Add();

        await m_repository.RemoveAsync(row);

        Assert.That(await m_repository.GetByIdAsync(row.RecordingId), Is.Null);
    }

    [Test]
    public async Task AnUnknownRecordingIsSimplyNotThere()
    {
        Assert.That(await m_repository.GetByIdAsync(Guid.NewGuid()), Is.Null);
    }
}
