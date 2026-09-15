using System.Net.Http.Json;
using Init7Tv.Dto.Init7Api;

namespace Init7Tv.BusinessLogic.HttpClientWrapper;

public sealed class HttpClientWrapper : IHttpClientWrapper
{
    private readonly HttpClient m_httpClient;

    public HttpClientWrapper(HttpClient httpClient)
    {
        m_httpClient = httpClient;
    }

    public Task<byte[]> GetByteArrayAsync(string url) => m_httpClient.GetByteArrayAsync(url);
    public Task<T?> GetJsonAsync<T>(string url) => m_httpClient.GetFromJsonAsync<T>(url);

    public async Task<T[]> GetInit7PagedResponseAsync<T>(string url)
    {
        var results = new List<T>();
        var nextUrl = url;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            var response = await GetJsonAsync<Init7PagedResponse<T>>(nextUrl);
            if (response == null)
            {
                break;
            }

            if (response.Results != null)
            {
                results.AddRange(response.Results);
            }

            nextUrl = response.Next;
        }

        return results.ToArray();
    }
}