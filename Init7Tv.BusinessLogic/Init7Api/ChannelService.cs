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

        channelResult = await GetChannelDtos(channels);
        m_cache.Set(CacheKey, channelResult, m_cacheDuration);
        return channelResult;
    }

    public async Task<OperationResult<ChannelDto>> GetChannelById(Guid channelId)
    {
        var channelsResult = await GetChannelsAsync();

        if (!channelsResult.IsSuccess)
        {
            return OperationResult<ChannelDto>.Error("Channel list was empty or unavailable");
        }

        var channel = channelsResult.Value.FirstOrDefault(x => x.ChannelId == channelId);
        if (channel == null)
        {
            return OperationResult<ChannelDto>.NotFound("Channel not found");
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