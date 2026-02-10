using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.BusinessLogic.Init7Api;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace Init7Tv.UnitTest;

public class ChannelServiceTest
{
    [Test]
    public async Task ParseChannelTest()
    {
        var channelList = await File.ReadAllTextAsync("./Data/ChannelFile.txt");
        var bytes = new byte[] { 1, 2, 3, 4 };

        Mock<IHttpClientWrapper> httpClientMock = new();
        httpClientMock.Setup(f => f.GetStringAsync("https://api.init7.net/tvchannels.m3u?rp=true")).ReturnsAsync(channelList);
        httpClientMock.Setup(f => f.GetByteArrayAsync(It.IsAny<string>())).ReturnsAsync(bytes);

        var service = new ChannelService(httpClientMock.Object, new MemoryCache(new MemoryCacheOptions()));

        var result = await service.GetChannelsAsync();

        Assert.That(result.HasError, Is.False);

        var channels = result.Value;
        Assert.That(channels, Has.Count.EqualTo(101));

        var firstChannel = channels.First();
        Assert.That(firstChannel, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(firstChannel.DisplayName, Is.EqualTo("SRF 1"));
            Assert.That(firstChannel.Logo, Is.EqualTo(bytes));
            Assert.That(firstChannel.CanonicalName, Is.EqualTo("SRF1.ch"));
            Assert.That(firstChannel.MainLaunguage, Is.EqualTo("de"));
            Assert.That(firstChannel.HlsUrl, Is.EqualTo("https://api.tv.init7.net/api/live/?channel=b87abb69-d5ed-44c5-8cab-0f7be4ef51b1"));
        });

        httpClientMock.VerifyAll();
    }
}