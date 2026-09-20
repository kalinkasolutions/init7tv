using System.Text.Json.Serialization;

namespace Init7Tv.BusinessLogic.Ffprobe;

public sealed class FrameInfo
{
    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>1 when the coded frame is interlaced. Read per frame, unlike the
    /// stream level field_order, which is not dependable.</summary>
    [JsonPropertyName("interlaced_frame")]
    public int InterlacedFrame { get; set; }

    [JsonPropertyName("top_field_first")]
    public int TopFieldFirst { get; set; }
}
