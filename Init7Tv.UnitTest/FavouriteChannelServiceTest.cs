using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;
using Moq;

namespace Init7Tv.UnitTest;

public class FavouriteChannelServiceTest
{
    private const string UserName = "niggi";

    private static readonly Guid SrfOne = Guid.NewGuid();
    private static readonly Guid SrfTwo = Guid.NewGuid();

    private static readonly ChannelDto[] Channels =
    [
        new() { ChannelId = SrfOne, DisplayName = "SRF 1", CanonicalName = "SRF1.ch" },
        new() { ChannelId = SrfTwo, DisplayName = "SRF zwei", CanonicalName = "SRFzwei.ch" }
    ];

    private Mock<IChannelService> m_channelService = null!;
    private Mock<IFavouriteChannelRepository> m_repository = null!;
    private FavouriteChannelService m_service = null!;

    [SetUp]
    public void SetUp()
    {
        m_channelService = new Mock<IChannelService>();
        m_channelService
            .Setup(x => x.GetChannelsAsync())
            .ReturnsAsync(OperationResult<IReadOnlyCollection<ChannelDto>>.Success(Channels));
        m_channelService
            .Setup(x => x.GetChannelById(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) =>
            {
                var channel = Channels.FirstOrDefault(x => x.ChannelId == id);
                return channel == null
                    ? OperationResult<ChannelDto>.NotFound("Channel not found")
                    : OperationResult<ChannelDto>.Success(channel);
            });

        m_repository = new Mock<IFavouriteChannelRepository>();
        m_repository.Setup(x => x.GetChannelIdsAsync(It.IsAny<string>())).ReturnsAsync([]);

        m_service = new FavouriteChannelService(m_channelService.Object, m_repository.Object);
    }

    [Test]
    public async Task ChannelsAreMarkedWithTheViewersStars()
    {
        m_repository.Setup(x => x.GetChannelIdsAsync(UserName)).ReturnsAsync([SrfTwo]);

        var result = await m_service.GetChannelsAsync(UserName);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Single(x => x.ChannelId == SrfTwo).IsFavourite, Is.True);
            Assert.That(result.Value.Single(x => x.ChannelId == SrfOne).IsFavourite, Is.False);
        });
    }

    [Test]
    public async Task TheCachedChannelListIsNotMarkedForEveryone()
    {
        // it is shared between viewers, so marking it in place would give one
        // viewer's stars to the next
        m_repository.Setup(x => x.GetChannelIdsAsync(UserName)).ReturnsAsync([SrfTwo]);

        await m_service.GetChannelsAsync(UserName);

        Assert.That(Channels.Any(x => x.IsFavourite), Is.False);
    }

    [Test]
    public async Task TheListKeepsTheOrderItCameIn()
    {
        // the browser puts the starred ones on top; doing it here as well would
        // be a second copy of the same rule
        m_repository.Setup(x => x.GetChannelIdsAsync(UserName)).ReturnsAsync([SrfTwo]);

        var result = await m_service.GetChannelsAsync(UserName);

        Assert.That(result.Value.Select(x => x.ChannelId), Is.EqualTo(new[] { SrfOne, SrfTwo }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task StarringAChannelIsStored(bool isFavourite)
    {
        var result = await m_service.SetFavouriteAsync(UserName, SrfOne, isFavourite);

        Assert.That(result.IsSuccess, Is.True);
        m_repository.Verify(x => x.SetAsync(UserName, SrfOne, isFavourite), Times.Once);
    }

    [Test]
    public async Task AChannelThatDoesNotExistIsNotStored()
    {
        var result = await m_service.SetFavouriteAsync(UserName, Guid.NewGuid(), true);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.NotFound));
        m_repository.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task AFailingChannelListIsPassedOn()
    {
        m_channelService
            .Setup(x => x.GetChannelsAsync())
            .ReturnsAsync(OperationResult<IReadOnlyCollection<ChannelDto>>.BadGateway("Init7 is down"));

        var result = await m_service.GetChannelsAsync(UserName);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.BadGateway));
    }
}
