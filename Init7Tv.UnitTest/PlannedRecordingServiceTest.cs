using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;
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

        m_service = new PlannedRecordingService(m_repository.Object, channelService.Object, new RecordingSignal());
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
}
