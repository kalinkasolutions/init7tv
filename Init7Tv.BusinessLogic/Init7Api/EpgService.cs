using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.Dto.Init7Api;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic.Init7Api;

public sealed class EpgService : IEpgService
{
    private const string CacheKey = "epg";
    private const string EpgApiUrl = "https://api.tv.init7.net/api/v4/epg/";

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

    public void GetEpg(Guid channelId)
    {
        var url = $"{EpgApiUrl}/?channel={channelId}&start__gte={DateTime.Today}";
        var cacheKey = Hash.GetSha256(url);

        // if (m_cache.TryGetValue<>(cacheKey, out var epg))
        // {
        //     return;
        // }

        var response = m_httpClient.GetJsonAsync<Init7PagedResponse<Init7Epg>>($"{EpgApiUrl}/?channel={channelId}");
    }
}