using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.Dto;
using Init7Tv.Dto.Init7Api;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic.Init7Api;

public sealed class EpgService : IEpgService
{
    private const string EpgApiUrl = "https://api.tv.init7.net/api/v4/epg";

    private readonly TimeSpan m_cacheDuration = TimeSpan.FromDays(1);
    private readonly IHttpClientWrapper m_httpClient;
    private readonly IMemoryCache m_cache;
    private readonly IChannelService m_channelService;

    public EpgService(
        IHttpClientWrapper httpClient,
        IMemoryCache cache,
        IChannelService channelService
    )
    {
        m_httpClient = httpClient;
        m_cache = cache;
        m_channelService = channelService;
    }

    public async Task<OperationResult<EpgDto[]>> GetEpg(string canonicalName, bool tomorrow)
    {
        var channelResult = await m_channelService.GetByCanonicalName(canonicalName);
        if (!channelResult.IsSuccess)
        {
            return channelResult.MapError<EpgDto[]>();
        }

        var url = BuildEpgUrl(channelResult.Value.ChannelId, tomorrow);
        var cacheKey = Hash.GetSha256(url);

        if (m_cache.TryGetValue(cacheKey, out EpgDto[]? cachedEpg) && cachedEpg != null)
        {
            return OperationResult<EpgDto[]>.Success(cachedEpg);
        }

        var epgData = await m_httpClient.GetInit7PagedResponseAsync<Init7Epg>(url);
        return OperationResult<EpgDto[]>.Success(m_cache.Set(cacheKey, epgData.ToDto(), m_cacheDuration));
    }

    private static string BuildEpgUrl(Guid channelId, bool tomorrow)
    {
        var start = DateTime.UtcNow.Date;
        var end = start.AddDays(1);
        if (tomorrow)
        {
            start = start.AddDays(1);
            end = start.AddDays(1);
        }

        var url = $"{EpgApiUrl}/?channel={channelId}&start__gte={start:o}&stop__lte={end:o}";
        return url;
    }
}