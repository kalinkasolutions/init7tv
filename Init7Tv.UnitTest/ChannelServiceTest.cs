using System.Text.Json;
using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Dto.Init7Api;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace Init7Tv.UnitTest;

public class ChannelServiceTest
{
    private static readonly byte[] Logo = [1, 2, 3, 4];

    private Mock<IHttpClientWrapper> m_httpClient = null!;
    private ChannelService m_service = null!;

    [SetUp]
    public async Task SetUp()
    {
        var json = await File.ReadAllTextAsync("./Data/Channels.json");
        var channels = JsonSerializer.Deserialize<Init7TvChannel[]>(json)!;

        m_httpClient = new Mock<IHttpClientWrapper>();
        m_httpClient
            .Setup(f => f.GetInit7PagedResponseAsync<Init7TvChannel>(It.IsAny<string>()))
            .ReturnsAsync(channels);
        m_httpClient
            .Setup(f => f.GetByteArrayAsync(It.IsAny<string>()))
            .ReturnsAsync(Logo);

        m_service = new ChannelService(m_httpClient.Object, new MemoryCache(new MemoryCacheOptions()));
    }

    [Test]
    public async Task GetChannels_MapsApiChannels()
    {
        var result = await m_service.GetChannelsAsync();

        Assert.That(result.HasError, Is.False);

        var arte = result.Value.Single(x => x.CanonicalName == "Arte.fr");
        Assert.Multiple(() =>
        {
            Assert.That(arte.DisplayName, Is.EqualTo("Arte"));
            Assert.That(arte.MainLanguage, Is.EqualTo("fr"));
            Assert.That(arte.Logo, Is.EqualTo(Logo));
            Assert.That(arte.UdpSource, Is.EqualTo("udp://@233.50.230.60:5000"));
            Assert.That(arte.HlsSource, Is.EqualTo("https://api.tv.init7.net/api/live/?channel=3f2d1c4b-5a6e-4f7a-8b9c-0d1e2f3a4b5c"));
            Assert.That(arte.ManuallyAdded, Is.False);
        });
    }

    [Test]
    public async Task GetChannels_AddsTheManualFullHdChannels()
    {
        var result = await m_service.GetChannelsAsync();

        Assert.That(result.Value, Has.Count.EqualTo(3 + FullHdSrg.FullHdChannels.Length));
        Assert.That(result.Value.Where(x => x.ManuallyAdded), Has.Exactly(FullHdSrg.FullHdChannels.Length).Items);
    }

    [Test]
    public async Task GetChannels_PutsThePrioritisedChannelsFirst()
    {
        var result = await m_service.GetChannelsAsync();

        // a prioritised channel and its FHD twin share a canonical name, so they sort adjacent
        var leading = result.Value.Take(5).Select(x => x.DisplayName);
        Assert.That(leading, Is.EqualTo(new[]
        {
            "SRF info FHD",
            "SRF 1 FHD", "SRF 1",
            "SRF zwei FHD", "SRF zwei"
        }));
    }

    [Test]
    public async Task GetChannels_IsCachedAfterTheFirstCall()
    {
        await m_service.GetChannelsAsync();
        await m_service.GetChannelsAsync();

        m_httpClient.Verify(f => f.GetInit7PagedResponseAsync<Init7TvChannel>(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task GetChannelById_FindsTheChannel()
    {
        var result = await m_service.GetChannelById(new Guid("3f2d1c4b-5a6e-4f7a-8b9c-0d1e2f3a4b5c"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.DisplayName, Is.EqualTo("Arte"));
    }

    [Test]
    public async Task GetChannelById_ReportsNotFoundForAnUnknownId()
    {
        var result = await m_service.GetChannelById(Guid.NewGuid());

        Assert.That(result.ResultCode, Is.EqualTo(Shared.ResultCode.NotFound));
    }

    [Test]
    public async Task GetByCanonicalName_IgnoresTheManuallyAddedDuplicates()
    {
        var result = await m_service.GetByCanonicalName("SRF1.ch");

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            // the FHD entry shares the canonical name, the EPG only exists for the api one
            Assert.That(result.Value.ManuallyAdded, Is.False);
            Assert.That(result.Value.DisplayName, Is.EqualTo("SRF 1"));
        });
    }

    [Test]
    public async Task GetByCanonicalName_IsCaseInsensitive()
    {
        var result = await m_service.GetByCanonicalName("srf1.CH");

        Assert.That(result.IsSuccess, Is.True);
    }
}
