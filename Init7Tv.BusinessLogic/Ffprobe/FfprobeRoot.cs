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

    /// <summary>
    /// Which audio track to record, using ffmpeg's <c>-map 0:a:N</c> numbering.
    ///
    /// A track that is already stereo is preferred over a surround one in the same language, because
    /// the alternative is asking ffmpeg to fold 5.1 down to two channels, and its default fold puts
    /// the centre channel, which is where the dialogue is, well below the rest.
    ///
    /// The stereo track further down the same language is not always a stereo mix of the programme,
    /// though: on the SRG channels it is the description for the visually impaired, which is mostly
    /// silence with a narrator over it. Preferring stereo picked that one and recorded a programme
    /// that sounds like it has no audio, so a described or commentary track is never the choice
    /// however well it otherwise fits.
    /// </summary>
    public int GetPreferredAudioStream(string? language)
    {
        var audioStreams = GetAudioStreams;
        if (audioStreams.Length == 0)
        {
            return 0;
        }

        var numbered = audioStreams.Select((stream, index) => (stream, index)).ToArray();

        // a described track is worse than the wrong language, so this comes before either
        var programme = numbered.Where(x => !IsAlternativeCommentary(x.stream)).ToArray();
        if (programme.Length == 0)
        {
            programme = numbered;
        }

        var spoken = programme.Where(x => Matches(x.stream, language)).ToArray();

        // nothing in that language, so the choice is between what there is
        var candidates = spoken.Length > 0 ? spoken : programme;

        var stereo = candidates.FirstOrDefault(x => x.stream.Channels == 2);

        return stereo.stream != null ? stereo.index : candidates[0].index;
    }

    /// <summary>
    /// Which data stream carries the advertising cues, in ffmpeg's <c>-map 0:d:N</c> numbering, or
    /// null when the channel announces none.
    ///
    /// Found by codec rather than taken as the first one. These channels carry another data stream
    /// in front of it, so mapping 0:d:0 copied a PID with nothing on it: the capture ended up with a
    /// cue track holding no packets, and every recording reported no advertising at all.
    /// </summary>
    public int? GetCueStream()
    {
        var data = Streams.Where(x => x.CodecType == "data").ToArray();
        var cues = Array.FindIndex(data, x => x.CodecName == CueCodec);

        return cues < 0 ? null : cues;
    }

    /// <summary>What ffprobe calls a SCTE 35 cue stream.</summary>
    private const string CueCodec = "scte_35";

    /// <summary>
    /// Every audio track, in the order a recording should carry them, using ffmpeg's
    /// <c>-map 0:a:N</c> numbering.
    ///
    /// All of them, because the choice is made once and for ever: a recording is kept for weeks and
    /// whoever watches it later may want the other language, or the description. The preferred one
    /// leads, since a player with no way to choose takes the first.
    /// </summary>
    public int[] GetAudioStreamsToRecord(string? language)
    {
        var count = GetAudioStreams.Length;
        if (count == 0)
        {
            return [];
        }

        var preferred = GetPreferredAudioStream(language);

        return [preferred, .. Enumerable.Range(0, count).Where(index => index != preferred)];
    }

    /// <summary>
    /// Whether the track is something other than the programme's own sound: a description for the
    /// visually impaired, or a commentary.
    /// </summary>
    private static bool IsAlternativeCommentary(StreamInfo stream)
    {
        return Flagged(stream, "visual_impaired")
               || Flagged(stream, "descriptions")
               || Flagged(stream, "comment");
    }

    private static bool Flagged(StreamInfo stream, string name) =>
        stream.Disposition?.GetValueOrDefault(name) == 1;

    private static bool Matches(StreamInfo stream, string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return false;
        }

        var tagged = stream.Tags?.GetValueOrDefault("language");

        // the channel list says "de" where the stream says "deu"
        return tagged != null
               && (tagged.Equals(language, StringComparison.OrdinalIgnoreCase)
                   || tagged.StartsWith(language, StringComparison.OrdinalIgnoreCase)
                   || language.StartsWith(tagged, StringComparison.OrdinalIgnoreCase));
    }
}