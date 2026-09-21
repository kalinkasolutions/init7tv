using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;
using Microsoft.Extensions.Options;
using Moq;

namespace Init7Tv.UnitTest;

public class PlannedRecordingServiceTest
{
    private const string UserName = "niggi";

    private static readonly Guid SrfOne = Guid.NewGuid();

    private static readonly ChannelDto Channel = new()
    {
        ChannelId = SrfOne, DisplayName = "SRF 1 FHD", CanonicalName = "SRF1.ch"
    };

    private Mock<IPlannedRecordingRepository> m_repository = null!;
    private PlannedRecordingService m_service = null!;

    [SetUp]
    public void SetUp()
    {
        var channelService = new Mock<IChannelService>();
        channelService
            .Setup(x => x.GetChannelById(SrfOne))
            .ReturnsAsync(OperationResult<ChannelDto>.Success(Channel));
        channelService
            .Setup(x => x.GetChannelById(It.Is<Guid>(id => id != SrfOne)))
            .ReturnsAsync(OperationResult<ChannelDto>.NotFound("Channel not found"));

        m_repository = new Mock<IPlannedRecordingRepository>();
        m_repository.Setup(x => x.GetForUserAsync(It.IsAny<string>())).ReturnsAsync([]);

        m_service = new PlannedRecordingService(
            m_repository.Object,
            channelService.Object,
            new RecordingSignal(),
            Options.Create(new Init7TvOptions()));
    }

    /// The guide gives UTC. A time read back without a kind reaches the browser
    /// unmarked, is taken there as local, and a late programme lands on the
    /// wrong day of the guide.
    [Test]
    public async Task TimesComeBackMarkedAsUtcEvenWhenTheDatabaseForgets()
    {
        var lateAtNight = new DateTime(2026, 9, 19, 23, 10, 0, DateTimeKind.Unspecified);

        m_repository
            .Setup(x => x.GetForUserAsync(UserName))
            .ReturnsAsync([Stored(lateAtNight, lateAtNight.AddMinutes(5))]);

        var result = await m_service.GetAsync(UserName, isAdmin: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Single().StartsAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(result.Value.Single().EndsAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(result.Value.Single().StartsAt, Is.EqualTo(lateAtNight));
        });
    }

    /// <summary>An admin is answering for the machine rather than for themselves.</summary>
    [Test]
    public async Task AnAdminSeesEverybodysPicks()
    {
        m_repository.Setup(x => x.GetAllAsync()).ReturnsAsync([Stored(DateTime.UtcNow, DateTime.UtcNow.AddHours(1))]);

        var result = await m_service.GetAsync("somebody-else", isAdmin: true);

        Assert.That(result.Value, Has.Length.EqualTo(1));
        m_repository.Verify(x => x.GetForUserAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Somebody else having picked it says nothing about whether the viewer has, and the guide
    /// marks a programme as picked from this: two people who pick the same one share a capture, so
    /// an admin looking at everybody's picks must still be able to make their own.
    /// </summary>
    [Test]
    public async Task APickSaysWhetherItBelongsToWhoeverAskedForTheList()
    {
        m_repository.Setup(x => x.GetAllAsync()).ReturnsAsync([Stored(DateTime.UtcNow, DateTime.UtcNow.AddHours(1))]);

        var somebodyElse = await m_service.GetAsync("the-admin", isAdmin: true);
        var theirOwn = await m_service.GetAsync(UserName, isAdmin: true);

        Assert.Multiple(() =>
        {
            Assert.That(somebodyElse.Value.Single().IsMine, Is.False);
            Assert.That(theirOwn.Value.Single().IsMine, Is.True);
        });
    }

    [Test]
    public async Task PlanningKeepsTheTimeItWasGiven()
    {
        var starts = DateTime.UtcNow.AddDays(3);

        var result = await m_service.PlanAsync(UserName, Wanted(starts, starts.AddHours(1)));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.StartsAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(result.Value.StartsAt, Is.EqualTo(starts));
        });
    }

    /// A browser sending local time means the same instant, not the same clock face.
    [Test]
    public async Task ALocalTimeIsStoredAsTheInstantItMeans()
    {
        var starts = DateTime.Now.AddDays(3);
        PlannedRecording? stored = null;
        m_repository.Setup(x => x.AddAsync(It.IsAny<PlannedRecording>()))
            .Callback((PlannedRecording x) => stored = x);

        await m_service.PlanAsync(UserName, Wanted(starts, starts.AddHours(1)));

        Assert.Multiple(() =>
        {
            Assert.That(stored!.StartsAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(stored.StartsAt, Is.EqualTo(starts.ToUniversalTime()));
        });
    }

    [Test]
    public async Task TheChannelIsTakenFromTheListRatherThanTheBrowser()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var wanted = Wanted(starts, starts.AddHours(1)) with
        {
            ChannelName = "something else", CanonicalName = "nonsense"
        };

        var result = await m_service.PlanAsync(UserName, wanted);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.ChannelName, Is.EqualTo("SRF 1 FHD"));
            Assert.That(result.Value.CanonicalName, Is.EqualTo("SRF1.ch"));
        });
    }

    [Test]
    public async Task AProgrammeThatIsOverCannotBeRecorded()
    {
        var starts = DateTime.UtcNow.AddHours(-2);

        var result = await m_service.PlanAsync(UserName, Wanted(starts, starts.AddHours(1)));

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Invalid));
    }

    [Test]
    public async Task AProgrammeWithoutAnIdIsRefused()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var wanted = Wanted(starts, starts.AddHours(1)) with { ProgrammeId = Guid.Empty };

        var result = await m_service.PlanAsync(UserName, wanted);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Invalid));
    }

    [Test]
    public async Task AnUnknownChannelIsRefused()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var wanted = Wanted(starts, starts.AddHours(1)) with { ChannelId = Guid.NewGuid() };

        var result = await m_service.PlanAsync(UserName, wanted);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.NotFound));
    }

    /// An admin is shown everybody's picks, so they have to be able to drop one that is not theirs:
    /// a list that cannot be acted on is worse than no list at all.
    [Test]
    public async Task AnAdminCanDropSomebodyElsesPick()
    {
        var programmeId = Guid.NewGuid();
        m_repository.Setup(x => x.RemoveAsync(UserName, programmeId)).ReturnsAsync(true);

        var result = await m_service.CancelAsync("the-admin", isAdmin: true, programmeId, owner: UserName);

        Assert.That(result.IsSuccess, Is.True);
        m_repository.Verify(x => x.RemoveAsync(UserName, programmeId), Times.Once);
    }

    /// Not found rather than forbidden, so somebody else's picks cannot be found by asking.
    [Test]
    public async Task AnybodyElseCannotDropAPickThatIsNotTheirs()
    {
        var programmeId = Guid.NewGuid();

        var result = await m_service.CancelAsync(UserName, isAdmin: false, programmeId, owner: "somebody-else");

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.NotFound));
        m_repository.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    public async Task DroppingYourOwnPickStillWorks()
    {
        var programmeId = Guid.NewGuid();
        m_repository.Setup(x => x.RemoveAsync(UserName, programmeId)).ReturnsAsync(true);

        var result = await m_service.CancelAsync(UserName, isAdmin: false, programmeId, owner: UserName);

        Assert.That(result.IsSuccess, Is.True);
    }

    private static PlannedRecordingDto Wanted(DateTime starts, DateTime ends) => new()
    {
        ProgrammeId = Guid.NewGuid(),
        ChannelId = SrfOne,
        ChannelName = "SRF 1 FHD",
        CanonicalName = "SRF1.ch",
        Title = "Meteo",
        StartsAt = starts,
        EndsAt = ends
    };

    private static PlannedRecording Stored(DateTime starts, DateTime ends) => new()
    {
        ProgrammeId = Guid.NewGuid(),
        UserName = UserName,
        ChannelId = SrfOne,
        ChannelName = "SRF 1 FHD",
        CanonicalName = "SRF1.ch",
        Title = "Meteo",
        StartsAt = starts,
        EndsAt = ends,
        PlannedAt = DateTime.UtcNow
    };

    [Test]
    public async Task RecordingNowStartsFromNowWithNoEndInMind()
    {
        PlannedRecording? stored = null;
        m_repository.Setup(x => x.AddAsync(It.IsAny<PlannedRecording>()))
            .Callback<PlannedRecording>(x => stored = x)
            .Returns(Task.CompletedTask);

        var result = await m_service.RecordNowAsync("niggi", SrfOne);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(stored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.OpenEnded, Is.True);
            Assert.That(stored.StartsAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(5)));
            Assert.That(stored.UserName, Is.EqualTo("niggi"));
        });
    }

    /// <summary>
    /// Far enough out that it is never why one ends. It still has to be a real time, because the
    /// scheduling arithmetic adds a post-roll to it.
    /// </summary>
    [Test]
    public async Task ARecordingWithNoEndStillCarriesABackstop()
    {
        PlannedRecording? stored = null;
        m_repository.Setup(x => x.AddAsync(It.IsAny<PlannedRecording>()))
            .Callback<PlannedRecording>(x => stored = x)
            .Returns(Task.CompletedTask);

        await m_service.RecordNowAsync("niggi", SrfOne);

        Assert.That(stored!.EndsAt - stored.StartsAt, Is.EqualTo(new Init7TvOptions().OpenEndedBackstop));
        Assert.That(stored.EndsAt, Is.LessThan(DateTime.MaxValue.AddDays(-1)), "and nothing that can overflow");
    }

    [Test]
    public async Task TheChannelIsWhatTheRecordingIsCalled()
    {
        PlannedRecording? stored = null;
        m_repository.Setup(x => x.AddAsync(It.IsAny<PlannedRecording>()))
            .Callback<PlannedRecording>(x => stored = x)
            .Returns(Task.CompletedTask);

        await m_service.RecordNowAsync("niggi", SrfOne);

        Assert.That(stored!.Title, Is.EqualTo(Channel.DisplayName));
    }

    /// <summary>
    /// Two people recording the same channel share one capture, exactly as two people picking the
    /// same programme do. Anything else would encode it twice.
    /// </summary>
    [Test]
    public async Task TwoPeopleRecordingTheSameChannelNameTheSameThing()
    {
        var ids = new List<Guid>();
        m_repository.Setup(x => x.AddAsync(It.IsAny<PlannedRecording>()))
            .Callback<PlannedRecording>(x => ids.Add(x.ProgrammeId))
            .Returns(Task.CompletedTask);

        await m_service.RecordNowAsync("niggi", SrfOne);
        await m_service.RecordNowAsync("sami", SrfOne);

        Assert.That(ids, Has.Count.EqualTo(2));
        Assert.That(ids[0], Is.EqualTo(ids[1]));
        Assert.That(ids[0], Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task AnUnknownChannelCannotBeRecorded()
    {
        var result = await m_service.RecordNowAsync("niggi", Guid.NewGuid());

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.NotFound));
        m_repository.Verify(x => x.AddAsync(It.IsAny<PlannedRecording>()), Times.Never);
    }
}
