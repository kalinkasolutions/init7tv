namespace Init7Tv.Dto.Init7Api;

public sealed class Init7PagedResponse<T>
{
    public int Count { get; set; }
    public string? Next { get; set; }
    public string? Previous { get; set; }
    public T[] Results { get; set; } = [];
}