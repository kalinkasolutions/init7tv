using System.Globalization;
using System.Text;

namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>
/// Writes the subtitle segment that goes beside a media segment.
///
/// A player reads each one on its own, so it has to say where its timings sit
/// in the stream. X-TIMESTAMP-MAP does that: the cues are written on the same
/// clock as the pictures, which holds because one ffmpeg produces both outputs
/// and times them from the same input.
/// </summary>
public static class WebVttSegment
{
    public static string Build(IEnumerable<WebVttCue> cues, TimeSpan start, TimeSpan end)
    {
        var body = new StringBuilder();
        body.Append("WEBVTT\n");
        body.Append("X-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:0\n");

        foreach (var cue in cues.Where(x => Overlaps(x, start, end)).OrderBy(x => x.Start))
        {
            body.Append('\n');
            body.Append(Format(cue.Start));
            body.Append(" --> ");
            body.Append(Format(cue.End));
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
