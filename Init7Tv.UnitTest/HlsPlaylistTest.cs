using System.Globalization;
using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.StreamManager;

namespace Init7Tv.UnitTest;

public class HlsPlaylistTest
{
    private const string StreamId = "abc123";

    private static TvSegment Segment(string name, double seconds) =>
        new(name, TimeSpan.FromSeconds(seconds));

    private static string[] LinesOf(string playlist) =>
        playlist.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();

    [Test]
    public void AnEmptyPlaylistStillDescribesItself()
    {
        // a player asking before the first segment is cut needs a target duration
        // to hold the next reload against, and there is nothing to take one from
        var lines = LinesOf(HlsPlaylist.Live(StreamId, [], mediaSequenceId: 0, fallbackTargetDuration: 2));

        Assert.That(lines, Is.EqualTo(new[]
        {
            "#EXTM3U",
            "#EXT-X-VERSION:6",
            "#EXT-X-TARGETDURATION:2",
            "#EXT-X-MEDIA-SEQUENCE:0"
        }));
    }

    [Test]
    public void EverySegmentIsListedWithItsOwnLength()
    {
        var lines = LinesOf(HlsPlaylist.Live(
            StreamId, [Segment("seg0.ts", 2), Segment("seg1.ts", 4)], mediaSequenceId: 7, fallbackTargetDuration: 2));

        Assert.That(lines, Is.EqualTo(new[]
        {
            "#EXTM3U",
            "#EXT-X-VERSION:6",
            "#EXT-X-TARGETDURATION:4",
            "#EXT-X-MEDIA-SEQUENCE:7",
            "#EXTINF:2.000,",
            $"/api/streaming/segment/{StreamId}/seg0.ts",
            "#EXTINF:4.000,",
            $"/api/streaming/segment/{StreamId}/seg1.ts"
        }));
    }

    [Test]
    public void TheTargetDurationCoversTheLongestSegment()
    {
        // a segment longer than the target is a spec violation players complain about
        var playlist = HlsPlaylist.Live(
            StreamId, [Segment("seg0.ts", 2), Segment("seg1.ts", 4.2)], mediaSequenceId: 0, fallbackTargetDuration: 2);

        Assert.That(playlist, Does.Contain("#EXT-X-TARGETDURATION:5"));
    }

    [Test]
    public void ALengthIsWrittenTheSameWhateverTheMachineCallsADecimalPoint()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-CH");

        try
        {
            var playlist = HlsPlaylist.Live(
                StreamId, [Segment("seg0.ts", 1.5)], mediaSequenceId: 0, fallbackTargetDuration: 2);

            Assert.That(playlist, Does.Contain("#EXTINF:1.500,"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    [Test]
    public void TheMediaSequenceSaysHowManySegmentsHaveRolledOff()
    {
        // it is what tells a player reloading that the window has moved rather
        // than that the segments it is holding were replaced
        var playlist = HlsPlaylist.Live(
            StreamId, [Segment("seg42.ts", 2)], mediaSequenceId: 42, fallbackTargetDuration: 2);

        Assert.That(playlist, Does.Contain("#EXT-X-MEDIA-SEQUENCE:42"));
    }
}
