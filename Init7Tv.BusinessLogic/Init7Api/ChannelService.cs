using System.Threading.Channels;
using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.Dto;
using Init7Tv.Dto.Init7Api;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic.Init7Api;

public sealed class ChannelService : IChannelService
{
    private const string CacheKey = "channels";
    private const string ChannelEndpoint = "https://api.tv.init7.net/api/v4/tvchannel/";

    private readonly TimeSpan m_cacheDuration = TimeSpan.FromDays(1);
    private readonly IHttpClientWrapper m_httpClient;
    private readonly IMemoryCache m_cache;

    private readonly Init7TvChannel[] SRGFullHD =
    [
        new Init7TvChannel
        {
            CanonicalName = "SRF1FHD.ch",
            Pk = new Guid("ed7d7676-9b05-419a-99b4-5f993d238f80"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/1102_SRF1.ch.png",
            Name = "SRF 1 FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=ed7d7676-9b05-419a-99b4-5f993d238f80"
        }
    ];

    public ChannelService(
        IHttpClientWrapper httpClient,
        IMemoryCache cache
    )
    {
        m_httpClient = httpClient;
        m_cache = cache;
    }

    public async Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync()
    {
        if (m_cache.TryGetValue<OperationResult<IReadOnlyCollection<ChannelDto>>>(CacheKey, out var channelResult) &&
            channelResult != null &&
            !channelResult.HasError)
        {
            return channelResult;
        }

        var channels = await m_httpClient.GetInit7PagedResponseAsync<Init7TvChannel>(ChannelEndpoint);

        channelResult = await GetChannelDtos(SRGFullHD.Concat(channels).ToArray());
        m_cache.Set(CacheKey, channelResult, m_cacheDuration);
        return channelResult;
    }

    public async Task<OperationResult<ChannelDto>> GetChannelById(Guid channelId)
    {
        var channelsResult = await GetChannelsAsync();

        if (!channelsResult.IsSuccess)
        {
            return channelsResult.MapError<ChannelDto>();
        }

        var channel = channelsResult.Value.FirstOrDefault(x => x.ChannelId == channelId);
        if (channel == null)
        {
            return OperationResult<ChannelDto>.NotFound("Channel not found");
        }

        return OperationResult<ChannelDto>.Success(channel);
    }

    public async Task<OperationResult<ChannelDto>> GetByCanonicalName(string canonicalName)
    {
        var channelsResult = await GetChannelsAsync();

        if (!channelsResult.IsSuccess)
        {
            return channelsResult.MapError<ChannelDto>();
        }

        var channel = channelsResult.Value.FirstOrDefault(x => string.Equals(x.CanonicalName, canonicalName, StringComparison.InvariantCultureIgnoreCase));
        if (channel == null)
        {
            return OperationResult<ChannelDto>.NotFound($"Channel not found with canonical name: {canonicalName}");
        }

        return OperationResult<ChannelDto>.Success(channel);
    }

    private async Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelDtos(Init7TvChannel[] channels)
    {
        var channelDtos = new List<ChannelDto>(channels.Length);
        foreach (var channel in channels)
        {
            channelDtos.Add(new ChannelDto
            {
                ChannelId = channel.Pk,
                DisplayName = channel.Name,
                Logo = await m_httpClient.GetByteArrayAsync(channel.Logo),
                HlsUrl = channel.HlsSrc,
                MainLaunguage = channel.Language,
                CanonicalName = channel.CanonicalName,
            });
        }

        return OperationResult<IReadOnlyCollection<ChannelDto>>.Success(channelDtos.ToArray());
    }
}