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

    public EpgService(
        IHttpClientWrapper httpClient,
        IMemoryCache cache
    )
    {
        m_httpClient = httpClient;
        m_cache = cache;
    }

    public async Task<OperationResult<EpgDto[]>> GetEpg(Guid channelId)
    {
        var url = BuildEpgUrl(channelId);
        var cacheKey = Hash.GetSha256(url);

        if (m_cache.TryGetValue(cacheKey, out EpgDto[]? cachedEpg) && cachedEpg != null)
        {
            return OperationResult<EpgDto[]>.Success(cachedEpg);
        }

        var epgData = await m_httpClient.GetInit7PagedResponseAsync<Init7Epg>(url);
        return OperationResult<EpgDto[]>
            .Success(m_cache.Set(cacheKey, epgData.Select(EpgMapper.Map()).ToArray(), m_cacheDuration));
    }


    private static string BuildEpgUrl(Guid channelId)
    {
        var start = DateTime.UtcNow.Date;
        var end = start.AddDays(1);
        var url = $"{EpgApiUrl}/?channel={channelId}&start__gte={start:o}&stop__lte={end:o}";
        return url;
    }
}