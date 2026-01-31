using System.Text.Json.Serialization;

namespace Init7Tv.Dto.Init7Api;

public sealed class Init7Epg
{
    [JsonPropertyName("pk")]
    public Guid Pk { get; set; }

    [JsonPropertyName("timeslot")]
    public Init7TimeSlotDto Timeslot { get; set; } = null!;

    [JsonPropertyName("channel")]
    public Guid Channel { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("sub_title")]
    public string SubTitle { get; set; } = string.Empty;

    [JsonPropertyName("desc")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("categories")]
    public string[] Categories { get; set; } = [];

    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }
}