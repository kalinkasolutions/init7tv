using Init7Tv.Dal;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.UnitTest;

public class PlannedRecordingRepositoryTest
{
    private static readonly DateTime Now = new(2026, 9, 20, 20, 0, 0, DateTimeKind.Utc);

    private SqliteConnection m_connection = null!;
    private Init7TvContext m_context = null!;
    private PlannedRecordingRepository m_repository = null!;

    [SetUp]
    public void SetUp()
    {
        // in-memory sqlite keeps the real provider behaviour, unlike the InMemory provider
        m_connection = new SqliteConnection("DataSource=:memory:");
        m_connection.Open();

        m_context = new Init7TvContext(
            new DbContextOptionsBuilder<Init7TvContext>().UseSqlite(m_connection).Options);
        m_context.Database.EnsureCreated();

        m_repository = new PlannedRecordingRepository(m_context);
    }

    [TearDown]
    public void TearDown()
    {
        m_context.Dispose();
        m_connection.Dispose();
    }

    private async Task<PlannedRecording> Add(
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        string userName = "niggi",
        Guid? programmeId = null
    )
    {
        var pick = new PlannedRecording
        {
            ProgrammeId = programmeId ?? Guid.NewGuid(),
            UserName = userName,
            StartsAt = startsAt ?? Now,
            EndsAt = endsAt ?? (startsAt ?? Now).AddHours(1),
            PlannedAt = Now.AddHours(-1),
            Title = "Tagesschau"
        };

        await m_repository.AddAsync(pick);

        return pick;
    }

    [Test]
    public async Task AUserSeesOnlyTheirOwnPicks()
    {
        await Add(userName: "niggi");
        await Add(userName: "sami");

        Assert.That((await m_repository.GetForUserAsync("niggi")).Select(x => x.UserName),
            Is.EqualTo(new[] { "niggi" }));
    }

    [Test]
    public async Task PicksComeBackInTheOrderTheyAir()
    {
        await Add(startsAt: Now.AddHours(2));
        await Add(startsAt: Now);
        await Add(startsAt: Now.AddHours(1));

        Assert.That((await m_repository.GetAllAsync()).Select(x => x.StartsAt), Is.Ordered);
    }

    [Test]
    public async Task APickRunningWhenTheWindowOpensIsInIt()
    {
        // it started before the window but has not finished, which is exactly the
        // case the pass has to catch after a restart
        await Add(startsAt: Now.AddHours(-1), endsAt: Now.AddHours(1));

        Assert.That(await m_repository.GetInWindowAsync(Now, Now.AddHours(2)), Is.Not.Empty);
    }

    [Test]
    public async Task APickStartingInsideTheWindowIsInIt()
    {
        await Add(startsAt: Now.AddMinutes(30), endsAt: Now.AddHours(2));

        Assert.That(await m_repository.GetInWindowAsync(Now, Now.AddHours(1)), Is.Not.Empty);
    }

    [Test]
    public async Task APickThatEndedBeforeTheWindowIsNot()
    {
        await Add(startsAt: Now.AddHours(-3), endsAt: Now.AddHours(-2));

        Assert.That(await m_repository.GetInWindowAsync(Now, Now.AddHours(1)), Is.Empty);
    }

    [Test]
    public async Task APickEndingExactlyAsTheWindowOpensIsNot()
    {
        // there is nothing of it left to record
        await Add(startsAt: Now.AddHours(-1), endsAt: Now);

        Assert.That(await m_repository.GetInWindowAsync(Now, Now.AddHours(1)), Is.Empty);
    }

    [Test]
    public async Task APickStartingAfterTheWindowIsNot()
    {
        await Add(startsAt: Now.AddHours(3), endsAt: Now.AddHours(4));

        Assert.That(await m_repository.GetInWindowAsync(Now, Now.AddHours(1)), Is.Empty);
    }

    [Test]
    public async Task APickStartingExactlyAsTheWindowClosesIsStillInIt()
    {
        // the pass that reads it is the one that has to act on it
        await Add(startsAt: Now.AddHours(1), endsAt: Now.AddHours(2));

        Assert.That(await m_repository.GetInWindowAsync(Now, Now.AddHours(1)), Is.Not.Empty);
    }

    [Test]
    public async Task AskingForTheSameProgrammeTwiceIsOneRequest()
    {
        // it is not an error, and it is certainly not a second recording
        var programmeId = Guid.NewGuid();
        await Add(programmeId: programmeId);
        await Add(programmeId: programmeId);

        Assert.That(await m_repository.GetAllAsync(), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task TwoPeopleAskingForTheSameProgrammeAreTwoPicks()
    {
        var programmeId = Guid.NewGuid();
        await Add(programmeId: programmeId, userName: "niggi");
        await Add(programmeId: programmeId, userName: "sami");

        Assert.That(await m_repository.GetAllAsync(), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task SomebodyStillWaitingForAProgrammeKeepsItAlive()
    {
        // what tells the pass a shared capture is not this viewer's to end
        var programmeId = Guid.NewGuid();
        await Add(programmeId: programmeId, userName: "niggi");
        await Add(programmeId: programmeId, userName: "sami");

        await m_repository.RemoveAsync("niggi", programmeId);

        Assert.That(await m_repository.AnyForProgrammeAsync(programmeId), Is.True);
    }

    [Test]
    public async Task OnceTheLastPersonHasLetGoNobodyIsWaiting()
    {
        var programmeId = Guid.NewGuid();
        await Add(programmeId: programmeId);

        await m_repository.RemoveAsync("niggi", programmeId);

        Assert.That(await m_repository.AnyForProgrammeAsync(programmeId), Is.False);
    }

    [Test]
    public async Task DroppingAPickSaysWhetherThereWasOne()
    {
        var pick = await Add();

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await m_repository.RemoveAsync("niggi", pick.ProgrammeId), Is.True);
            Assert.That(await m_repository.RemoveAsync("niggi", pick.ProgrammeId), Is.False, "already gone");
            Assert.That(await m_repository.RemoveAsync("sami", pick.ProgrammeId), Is.False, "never theirs");
        });
    }
}
