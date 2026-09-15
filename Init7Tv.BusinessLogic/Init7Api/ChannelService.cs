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

    private static readonly Dictionary<string, int> ChannelPriority = new()
    {
        ["SRFinfo.ch"] = 0,
        ["SRF1.ch"] = 1,
        ["SRFzwei.ch"] = 2
    };

    private readonly TimeSpan m_cacheDuration = TimeSpan.FromDays(1);
    private readonly IHttpClientWrapper m_httpClient;
    private readonly IMemoryCache m_cache;

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
        if (m_cache.TryGetValue<IReadOnlyCollection<ChannelDto>>(CacheKey, out var channelResult) &&
            channelResult != null)
        {
            return OperationResult<IReadOnlyCollection<ChannelDto>>.Success(channelResult);
        }

        var channels = await m_httpClient.GetInit7PagedResponseAsync<Init7TvChannel>(ChannelEndpoint);
        channelResult = await GetChannelDtos(FullHdSrg.FullHdChannels.Concat(channels).ToArray());

        channelResult = channelResult
            .OrderBy(x => ChannelPriority.TryGetValue(x.CanonicalName, out var p) ? p : int.MaxValue)
            .ToArray();

        m_cache.Set(CacheKey, channelResult, m_cacheDuration);

        return OperationResult<IReadOnlyCollection<ChannelDto>>.Success(channelResult);
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

        var channel = channelsResult
            .Value
            .FirstOrDefault(x => string.Equals(x.CanonicalName, canonicalName, StringComparison.InvariantCultureIgnoreCase) && !x.ManuallyAdded);

        if (channel == null)
        {
            return OperationResult<ChannelDto>.NotFound($"Channel not found with canonical name: {canonicalName}");
        }

        return OperationResult<ChannelDto>.Success(channel);
    }

    private async Task<IReadOnlyCollection<ChannelDto>> GetChannelDtos(Init7TvChannel[] channels)
    {
        var channelDtos = new List<ChannelDto>(channels.Length);
        foreach (var channel in channels)
        {
            channelDtos.Add(new ChannelDto
            {
                ChannelId = channel.Pk,
                DisplayName = channel.Name,
                Logo = await m_httpClient.GetByteArrayAsync(channel.Logo),
                HlsSource = channel.HlsSrc,
                MainLanguage = channel.Language,
                CanonicalName = channel.CanonicalName,
                ManuallyAdded = channel.ManuallyAdded,
                UdpSource = channel.Src
            });
        }

        return channelDtos.ToArray();
    }
}