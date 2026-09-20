namespace Init7Tv.BusinessLogic.HttpClientWrapper;

public interface IHttpClientWrapper
{
    Task<byte[]> GetByteArrayAsync(string url);
    Task<T?> GetJsonAsync<T>(string url);
    Task<T[]> GetInit7PagedResponseAsync<T>(string url);
}