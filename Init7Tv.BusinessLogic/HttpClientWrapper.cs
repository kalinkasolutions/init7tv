using System.Net.Http.Json;

namespace Init7Tv.BusinessLogic;

public class HttpClientWrapper : IHttpClientWrapper
{
    private readonly HttpClient m_httpClient;

    public HttpClientWrapper(HttpClient httpClient)
    {
        m_httpClient = httpClient;
    }

    public Task<string> GetStringAsync(string url) => m_httpClient.GetStringAsync(url);
    public Task<byte[]> GetByteArrayAsync(string url) => m_httpClient.GetByteArrayAsync(url);
    public Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption options) => m_httpClient.GetAsync(url, options);
    public Task<T?> GetJsonAsync<T>(string url) => m_httpClient.GetFromJsonAsync<T>(url);
}