using System.Text.Json.Serialization;

namespace Init7Tv.BusinessLogic.Ffprobe;

public sealed class FormatInfo
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("nb_streams")]
    public int NbStreams { get; set; }

    [JsonPropertyName("nb_programs")]
    public int NbPrograms { get; set; }

    [JsonPropertyName("nb_stream_groups")]
    public int NbStreamGroups { get; set; }

    [JsonPropertyName("format_name")]
    public string FormatName { get; set; } = string.Empty;

    [JsonPropertyName("format_long_name")]
    public string FormatLongName { get; set; } = string.Empty;

    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;

    [JsonPropertyName("probe_score")]
    public int ProbeScore { get; set; }
}