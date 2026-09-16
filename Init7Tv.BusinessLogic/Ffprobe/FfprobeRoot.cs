using System.Globalization;
using System.Text.Json.Serialization;

namespace Init7Tv.BusinessLogic.Ffprobe;

public sealed class FfprobeRoot
{
    [JsonPropertyName("streams")]
    public List<StreamInfo> Streams { get; set; } = [];

    [JsonPropertyName("frames")]
    public List<FrameInfo> Frames { get; set; } = [];

    [JsonPropertyName("format")]
    public FormatInfo Format { get; set; } = new();


    public string? GetVideoCodec => GetVideoStream?.CodecName;

    public StreamInfo? GetVideoStream => Streams.FirstOrDefault(x => x.CodecType == "video");

    public double GetFrameRate => ParseRate(GetVideoStream?.RFrameRate);

    /// <summary>
    /// Whether to deinterlace, taken from the coded frames themselves.
    ///
    /// Not from the stream level field_order: the same multicast reports "tt" or
    /// "progressive" depending only on how long ffprobe watches it. The per
    /// frame flag is unanimous on the channels tested, 289 of 289 interlaced for
    /// SAT.1 and 279 of 279 progressive for SRF zwei. Frame rate is only a
    /// fallback for when no frames were read.
    /// </summary>
    public bool IsInterlaced
    {
        get
        {
            var videoFrames = Frames.Where(x => x.MediaType == "video").ToArray();

            if (videoFrames.Length == 0)
            {
                return GetFrameRate is > 0 and <= 30;
            }

            return videoFrames.Count(x => x.InterlacedFrame == 1) * 2 > videoFrames.Length;
        }
    }

    /// <summary>
    /// Field order of the interlaced frames. yadif's parity=auto reads the
    /// stream level metadata, which on these multicasts is not dependable, so
    /// it is told explicitly.
    /// </summary>
    public bool IsTopFieldFirst
    {
        get
        {
            var interlaced = Frames.Where(x => x.MediaType == "video" && x.InterlacedFrame == 1).ToArray();

            // tff is the broadcast norm, and the right guess when nothing was read
            return interlaced.Length == 0 || interlaced.Count(x => x.TopFieldFirst == 1) * 2 >= interlaced.Length;
        }
    }

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