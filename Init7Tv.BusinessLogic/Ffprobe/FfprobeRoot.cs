using System.Globalization;
using System.Text.Json.Serialization;

namespace Init7Tv.BusinessLogic.Ffprobe;

public sealed class FfprobeRoot
{
    [JsonPropertyName("streams")]
    public List<StreamInfo> Streams { get; set; } = [];

    [JsonPropertyName("format")]
    public FormatInfo Format { get; set; } = new();


    public string? GetVideoCodec => GetVideoStream?.CodecName;

    public StreamInfo? GetVideoStream => Streams.FirstOrDefault(x => x.CodecType == "video");

    public double GetFrameRate => ParseRate(GetVideoStream?.RFrameRate);

    /// <summary>
    /// Whether to deinterlace. Decided on frame rate rather than field_order,
    /// because field_order is not dependable: the same multicast reports "tt"
    /// or "progressive" purely depending on how long ffprobe watches it.
    /// Broadcast frame rate does not wobble, and in DVB 50fps is progressive
    /// while 25fps carries 50 interlaced fields.
    /// </summary>
    public bool IsInterlaced => GetFrameRate is > 0 and <= 30;

    private static double ParseRate(string? rate)
    {
        if (string.IsNullOrWhiteSpace(rate))
        {
            return 0;
        }

        var parts = rate.Split('/');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var numerator)
            || !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var denominator)
            || denominator == 0)
        {
            return 0;
        }

        return numerator / denominator;
    }

    public string[] GetLanguages =>
        GetAudioStreams
            .Select(x => x.Tags)
            .Where(t => t != null && t.TryGetValue("language", out var _))
            .Select(t => t!["language"])
            .ToArray();

    private StreamInfo[] GetAudioStreams => Streams.Where(x => x.CodecType == "audio").ToArray();

    /// <summary>Language of the nth audio stream, using ffmpeg's <c>-map 0:a:N</c> numbering.</summary>
    public string? GetAudioLanguage(int audioStreamIndex)
    {
        var audioStreams = GetAudioStreams;
        if (audioStreamIndex < 0 || audioStreamIndex >= audioStreams.Length)
        {
            return null;
        }

        return audioStreams[audioStreamIndex].Tags?.GetValueOrDefault("language");
    }
}