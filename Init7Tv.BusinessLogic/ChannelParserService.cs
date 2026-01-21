using System.Text.RegularExpressions;
using Init7Tv.Dto;
using Microsoft.Extensions.Caching.Memory;

namespace Init7Tv.BusinessLogic;

public partial class ChannelParserService : IChannelParserService
{
    private const string ChannelEndpoint = "https://api.init7.net/tvchannels.m3u?rp=true";
    private readonly TimeSpan m_cacheDuration = TimeSpan.FromDays(1);
    private readonly IHttpClientWrapper m_httpClient;
    private readonly IMemoryCache m_cache;

    public ChannelParserService(
        IHttpClientWrapper httpClient,
        IMemoryCache cache
    )
    {
        m_httpClient = httpClient;
        m_cache = cache;
    }

    [GeneratedRegex(@"([\w-]+)=""(.*?)""")]
    private static partial Regex AttributesRegex();

    public async Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync()
    {
        if (m_cache.TryGetValue<OperationResult<IReadOnlyCollection<ChannelDto>>>("channels", out var channelResult) &&
            channelResult != null &&
            !channelResult.HasError)
        {
            return channelResult;
        }

        var channelList = await FetchChannelsAsync();
        if (string.IsNullOrEmpty(channelList))
        {
            return OperationResult<IReadOnlyCollection<ChannelDto>>.BadGateway("Channel list was empty or unavailable");
        }

        channelResult = await ParseChannels(channelList);
        m_cache.Set("channels", channelResult, m_cacheDuration);
        return channelResult;
    }

    private Task<string> FetchChannelsAsync()
    {
        return m_httpClient.GetStringAsync(ChannelEndpoint);
    }

    private async Task<OperationResult<IReadOnlyCollection<ChannelDto>>> ParseChannels(string channelList)
    {
        // skip 1 to avoid #EXTM3U line
        using var channelsEnumerator = channelList.Split("\n").Skip(1).AsEnumerable().GetEnumerator();
        var result = new List<ChannelDto>();
        while (channelsEnumerator.MoveNext() && !string.IsNullOrEmpty(channelsEnumerator.Current))
        {
            var metaLine = channelsEnumerator.Current.Trim();
            var metaData = metaLine[..metaLine.IndexOf(',')].Trim();
            var dict = AttributesRegex().Matches(metaData)
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

            var dto = new ChannelDto
            {
                DisplayName = metaLine[(metaLine.IndexOf(',') + 1)..].Trim(),
                TvName = dict.GetValueOrDefault("tvg-name") ?? string.Empty,
                Language = dict.GetValueOrDefault("group-title") ?? string.Empty
            };

            if (dict.TryGetValue("tvg-logo", out var logo) && logo != string.Empty)
            {
                dto.Logo = await m_httpClient.GetByteArrayAsync(logo);
            }

            // advance to hls url line
            channelsEnumerator.MoveNext();
            dto.HlsUrl = channelsEnumerator.Current;
            result.Add(dto);
        }

        return OperationResult<IReadOnlyCollection<ChannelDto>>.Success(result.ToArray());
    }
}