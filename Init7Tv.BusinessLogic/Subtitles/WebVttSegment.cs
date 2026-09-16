using System.Globalization;
using System.Text;

namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>
/// Writes the subtitle segment that goes beside a media segment.
///
/// A player reads each one on its own, so the segment has to say where its
/// timings sit in the stream. X-TIMESTAMP-MAP does that: the cues are counted
/// from the start of this segment, and the map names the presentation time that
/// start has. A map of zero would make every caption arrive as early as the
/// player joined the stream, because the player takes the first picture it saw
/// as its own zero.
/// </summary>
public static class WebVttSegment
{
    public static string Build(IEnumerable<WebVttCue> cues, TimeSpan start, TimeSpan end, ulong startPts)
    {
        var body = new StringBuilder();
        body.Append("WEBVTT\n");
        body.Append($"X-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:{startPts}\n");

        foreach (var cue in cues.Where(x => Overlaps(x, start, end)).OrderBy(x => x.Start))
        {
            // a caption already on screen when the segment opens begins at its
            // start: the alternative is a negative time, which cannot be written
            var from = cue.Start < start ? TimeSpan.Zero : cue.Start - start;

            body.Append('\n');
            body.Append(Format(from));
            body.Append(" --> ");
            body.Append(Format(cue.End - start));
            body.Append('\n');
            body.Append(cue.Text);
            body.Append('\n');
        }

        return body.ToString();
    }

    /// <summary>
    /// A caption that spans a boundary belongs in both segments, or it would
    /// vanish half way through for anyone who joined at the later one.
    /// </summary>
    private static bool Overlaps(WebVttCue cue, TimeSpan start, TimeSpan end) =>
        cue.End > start && cue.Start < end;

    private static string Format(TimeSpan time) =>
        time.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
