using System.Globalization;
using System.Text;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// What a player or a download is handed: the segments of a recording, described rather than cut.
/// </summary>
public static class RecordingPlaylist
{
    /// <summary>
    /// Byte ranges rather than files: every segment is a stretch of a capture that already exists,
    /// so nothing is cut, copied or converted to make one.
    /// </summary>
    /// <param name="running">
    /// True while the capture is still being written, when the list is left open so a player comes
    /// back for the rest of it on its own instead of running out and having to be prodded.
    /// </param>
    public static string Text(IReadOnlyList<RecordingSegment> segments, bool running)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        // byte ranges arrived in version 4
        sb.AppendLine("#EXT-X-VERSION:4");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{RecordingEngine.KeyframeSeconds + 1}");
        sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");

        // Every segment is kept rather than a window near the end, so somebody joining an hour in
        // can still start at the beginning.
        sb.AppendLine(running ? "#EXT-X-PLAYLIST-TYPE:EVENT" : "#EXT-X-PLAYLIST-TYPE:VOD");

        var previous = (Part: -1, End: -1L);
        foreach (var segment in segments)
        {
            // Each part is its own ffmpeg run, started when the last one was interrupted, so its
            // timestamps begin again at zero. Without being told, a player carries the timeline
            // across the join and every seek past it lands somewhere else entirely.
            if (previous.Part >= 0 && segment.Part != previous.Part)
            {
                sb.AppendLine("#EXT-X-DISCONTINUITY");
            }

            sb.AppendLine($"#EXTINF:{segment.Seconds.ToString("0.000", CultureInfo.InvariantCulture)},");

            // the offset may be left out when a range carries on from the one before, which is the
            // usual case and makes the playlist far smaller once it is thousands of lines long
            sb.AppendLine(segment.Part == previous.Part && segment.Offset == previous.End
                ? $"#EXT-X-BYTERANGE:{segment.Length}"
                : $"#EXT-X-BYTERANGE:{segment.Length}@{segment.Offset}");

            sb.AppendLine($"part/{segment.Part}.ts");
            previous = (segment.Part, segment.Offset + segment.Length);
        }

        if (!running)
        {
            sb.AppendLine("#EXT-X-ENDLIST");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The stretches worth handing over, which is all of them unless the advertising is being left
    /// out. Cutting on segment boundaries is what makes it free: every one starts on a keyframe, so
    /// the pieces join without anything being decoded or encoded.
    ///
    /// A segment is judged by its middle, so one straddling the edge of a break goes wherever most
    /// of it lies rather than being counted as advertising for its last frame.
    /// </summary>
    public static RecordingSegment[] WithoutBreaks(
        IReadOnlyList<RecordingSegment> segments,
        IReadOnlyList<AdBreakMark> breaks
    )
    {
        if (breaks.Count == 0)
        {
            return segments.ToArray();
        }

        var wanted = new List<RecordingSegment>();
        var at = 0.0;

        foreach (var segment in segments)
        {
            var middle = at + segment.Seconds / 2;

            if (!breaks.Any(gap => middle >= gap.StartsAt && middle < gap.EndsAt))
            {
                wanted.Add(segment);
            }

            at += segment.Seconds;
        }

        return wanted.ToArray();
    }
}
