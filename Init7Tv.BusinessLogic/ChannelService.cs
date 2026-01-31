using System.Text.RegularExpressions;
using Init7Tv.Dto;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic;

public partial class ChannelService : IChannelService
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

        var channels = await m_httpClient.GetJsonAsync<PagedResponse<Init7TvChannel>>(ChannelEndpoint);
        if (channels == null || channels.Results.Length == 0)
        {
            return OperationResult<IReadOnlyCollection<ChannelDto>>.BadGateway("Channel list was empty or unavailable");
        }

        channelResult = await GetChannelDtos(channels.Results);
        m_cache.Set(CacheKey, channelResult, m_cacheDuration);
        return channelResult;
    }

    private async Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelDtos(Init7TvChannel[] channels)
    {
        var channelDtos = new List<ChannelDto>(channels.Length);
        foreach (var channel in channels)
        {
            channelDtos.Add(new ChannelDto
            {
                Pk = channel.Pk,
                DisplayName = channel.Name,
                Logo = await m_httpClient.GetByteArrayAsync(channel.Logo),
                HlsUrl = channel.HlsSrc,
                Language = channel.Language,
                CannonicalName = channel.CanonicalName,
            });
        }

        return OperationResult<IReadOnlyCollection<ChannelDto>>.Success(channelDtos.ToArray());
    }
}