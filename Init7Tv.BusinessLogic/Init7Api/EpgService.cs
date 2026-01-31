using Init7Tv.BusinessLogic.HttpClientWrapper;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic.Init7Api;

public sealed class EpgService : IEpgService
{
    private const string CacheKey = "epg";
    private const string ChannelEndpoint = "https://api.tv.init7.net/api/v4/tvchannel/";
    
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
}