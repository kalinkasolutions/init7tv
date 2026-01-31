using System.Text.Json.Serialization;

namespace Init7Tv.Dto.Init7Api;

public sealed class Init7TimeSlotDto
{
    [JsonPropertyName("lower")]
    public DateTime Lower { get; set; }

    [JsonPropertyName("upper")]
    public DateTime Upper { get; set; }

    [JsonPropertyName("bounds")]
    public string Bounds { get; set; } = null!;
}