using System.Globalization;
using System.Text;

namespace Init7Tv.BusinessLogic.StreamManager;

/// <summary>The live playlist: a sliding window over the segments still held in memory.</summary>
public static class HlsPlaylist
{
    public static string Live(
        string streamId,
        IReadOnlyList<TvSegment> segments,
        int mediaSequenceId,
        int fallbackTargetDuration
    )
    {
        // a player holds a segment against the target duration, so it has to cover the longest
        var targetDuration = segments.Count == 0
            ? fallbackTargetDuration
            : (int)Math.Ceiling(segments.Max(x => x.Duration.TotalSeconds));

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{targetDuration}");
        sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:{mediaSequenceId}");

        foreach (var segment in segments)
        {
            sb.AppendLine($"#EXTINF:{segment.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"/api/streaming/segment/{streamId}/{segment.Name}");
        }

        return sb.ToString();
    }
}
