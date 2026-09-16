using System.Globalization;
using System.Text.Json.Serialization;
using Init7Tv.BusinessLogic.Subtitles;

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
    /// Whether the source is a broadcast rate that can carry interlaced frames.
    ///
    /// Deliberately not decided from the sampled frames. The probe only sees a
    /// couple of seconds, and SAT.1 sampled during an advert, which is shot
    /// progressive, reports progressive and would then run un-deinterlaced
    /// through the interlaced programme that follows. Which individual frames
    /// get deinterlaced is the filter's job, not this one's.
    ///
    /// Nor from field_order, which is not dependable: the same multicast
    /// reports "tt" or "progressive" depending only on how long ffprobe watches.
    /// </summary>
    public bool IsInterlaced
    {
        get
        {
            return GetFrameRate is > 0 and <= 30;
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

    /// <summary>
    /// Subtitle tracks that can be turned into words. Teletext decodes to text;
    /// DVB subtitles are pictures of text and would need recognising, so they are
    /// left out rather than offered and then found empty.
    /// </summary>
    public SubtitleTrack[] GetSubtitleTracks =>
        Streams
            .Where(x => x.CodecType == "subtitle")
            .Select((stream, index) => (stream, index))
            .Where(x => x.stream.CodecName == TeletextCodec)
            .Select(x => new SubtitleTrack
            {
                SubtitleStreamIndex = x.index,
                Language = x.stream.Tags?.GetValueOrDefault("language") ?? "und"
            })
            .ToArray();

    private const string TeletextCodec = "dvb_teletext";

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