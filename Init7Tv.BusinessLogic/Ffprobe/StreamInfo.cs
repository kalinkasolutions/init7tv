using System.Text.Json.Serialization;

namespace Init7Tv.BusinessLogic.Ffprobe;

public class StreamInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("codec_name")]
    public string CodecName { get; set; } = string.Empty;

    [JsonPropertyName("codec_long_name")]
    public string CodecLongName { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("codec_type")]
    public string CodecType { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("r_frame_rate")]
    public string RFrameRate { get; set; } = string.Empty;

    [JsonPropertyName("avg_frame_rate")]
    public string AvgFrameRate { get; set; } = string.Empty;
    
    [JsonPropertyName("pix_fmt")]
    public string PixelFormat { get; set; } = string.Empty;

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("sample_rate")]
    public string? SampleRate { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("disposition")]
    public Dictionary<string, int>? Disposition { get; set; }
}
