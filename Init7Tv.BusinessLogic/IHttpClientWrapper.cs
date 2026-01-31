namespace Init7Tv.BusinessLogic;

public interface IHttpClientWrapper
{
    Task<string> GetStringAsync(string url);
    Task<byte[]> GetByteArrayAsync(string url);
    Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption options);
    Task<T?> GetJsonAsync<T>(string url);
}