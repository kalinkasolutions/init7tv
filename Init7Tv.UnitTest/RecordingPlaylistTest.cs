using System.Globalization;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dto;

namespace Init7Tv.UnitTest;

public class RecordingPlaylistTest
{
    private static RecordingSegment Segment(int part, long offset, long length, double seconds = 4) =>
        new(part, offset, length, seconds);

    private static AdBreakMark Break(double from, double to) => new() { StartsAt = from, EndsAt = to };

    private static string[] LinesOf(string playlist) =>
        playlist.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();

    [Test]
    public void AFinishedRecordingIsClosedSoAPlayerStopsAsking()
    {
        var playlist = RecordingPlaylist.Text([Segment(0, 0, 100)], running: false);

        Assert.Multiple(() =>
        {
            Assert.That(playlist, Does.Contain("#EXT-X-PLAYLIST-TYPE:VOD"));
            Assert.That(playlist, Does.Contain("#EXT-X-ENDLIST"));
        });
    }

    [Test]
    public void ARecordingStillBeingWrittenIsLeftOpen()
    {
        // an open list is what has a player come back for the rest on its own
        var playlist = RecordingPlaylist.Text([Segment(0, 0, 100)], running: true);

        Assert.Multiple(() =>
        {
            Assert.That(playlist, Does.Contain("#EXT-X-PLAYLIST-TYPE:EVENT"));
            Assert.That(playlist, Does.Not.Contain("#EXT-X-ENDLIST"));
        });
    }

    [Test]
    public void ARangeThatCarriesOnFromTheLastLeavesOutTheOffset()
    {
        // thousands of lines of "@offset" that says only "straight after the last one"
        var lines = LinesOf(RecordingPlaylist.Text(
            [Segment(0, 0, 100), Segment(0, 100, 150)], running: false));

        Assert.Multiple(() =>
        {
            Assert.That(lines, Does.Contain("#EXT-X-BYTERANGE:100@0"));
            Assert.That(lines, Does.Contain("#EXT-X-BYTERANGE:150"));
        });
    }

    [Test]
    public void ARangeThatDoesNotCarryOnSaysWhereItStarts()
    {
        var lines = LinesOf(RecordingPlaylist.Text(
            [Segment(0, 0, 100), Segment(0, 500, 150)], running: false));

        Assert.That(lines, Does.Contain("#EXT-X-BYTERANGE:150@500"));
    }

    [Test]
    public void ThePartAJoinFallsOnIsMarkedAsADiscontinuity()
    {
        // each part is its own ffmpeg run, and its timestamps begin again at zero
        var lines = LinesOf(RecordingPlaylist.Text(
            [Segment(0, 0, 100), Segment(1, 0, 100)], running: false));

        // it has to lead the segment it applies to, not trail the one before
        Assert.That(lines, Does.Contain("#EXT-X-DISCONTINUITY"));
        Assert.That(lines[Array.IndexOf(lines, "#EXT-X-DISCONTINUITY") + 1], Does.StartWith("#EXTINF:"));
        Assert.That(lines[Array.IndexOf(lines, "#EXT-X-DISCONTINUITY") - 1], Is.EqualTo("part/0.ts"));
    }

    [Test]
    public void ThereIsNoDiscontinuityInFrontOfTheFirstSegment()
    {
        var playlist = RecordingPlaylist.Text([Segment(1, 0, 100)], running: false);

        Assert.That(playlist, Does.Not.Contain("#EXT-X-DISCONTINUITY"));
    }

    [Test]
    public void TheSameOffsetInAnotherPartIsNotTakenForACarryOn()
    {
        // both parts start at zero, and only the part number tells them apart
        var lines = LinesOf(RecordingPlaylist.Text(
            [Segment(0, 0, 100), Segment(1, 100, 100)], running: false));

        Assert.That(lines, Does.Contain("#EXT-X-BYTERANGE:100@100"),
            "a range in another part always says where it starts");
    }

    [Test]
    public void ALengthIsWrittenTheSameWhateverTheMachineCallsADecimalPoint()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-CH");

        try
        {
            var playlist = RecordingPlaylist.Text([Segment(0, 0, 100, seconds: 3.84)], running: false);

            Assert.That(playlist, Does.Contain("#EXTINF:3.840,"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    [Test]
    public void WithNoBreaksEverySegmentIsWanted()
    {
        RecordingSegment[] segments = [Segment(0, 0, 100), Segment(0, 100, 100)];

        Assert.That(RecordingPlaylist.WithoutBreaks(segments, []), Is.EqualTo(segments));
    }

    [Test]
    public void ASegmentInsideABreakIsLeftOut()
    {
        // four seconds each, so the second and third fall between 4 and 12
        RecordingSegment[] segments =
            [Segment(0, 0, 10), Segment(0, 10, 20), Segment(0, 30, 30), Segment(0, 60, 40)];

        var wanted = RecordingPlaylist.WithoutBreaks(segments, [Break(4, 12)]);

        Assert.That(wanted, Is.EqualTo(new[] { segments[0], segments[3] }));
    }

    [Test]
    public void ASegmentStraddlingTheEdgeGoesWhereMostOfItLies()
    {
        // one four second segment, its middle at 2: a break starting at 3 has
        // only its last second and the whole of it is kept
        RecordingSegment[] segments = [Segment(0, 0, 10)];

        Assert.That(RecordingPlaylist.WithoutBreaks(segments, [Break(3, 20)]), Is.EqualTo(segments));
    }

    [Test]
    public void ABreakThatEndsExactlyOnASegmentsMiddleDoesNotClaimIt()
    {
        // the end is exclusive, or a segment would be dropped by both the break
        // that ends on it and the one that starts there
        RecordingSegment[] segments = [Segment(0, 0, 10)];

        Assert.That(RecordingPlaylist.WithoutBreaks(segments, [Break(0, 2)]), Is.EqualTo(segments));
    }

    [Test]
    public void SeveralBreaksAreAllLeftOut()
    {
        RecordingSegment[] segments = Enumerable.Range(0, 6)
            .Select(i => Segment(0, i * 10, 10))
            .ToArray();

        var wanted = RecordingPlaylist.WithoutBreaks(segments, [Break(0, 4), Break(12, 20)]);

        Assert.That(wanted, Is.EqualTo(new[] { segments[1], segments[2], segments[5] }));
    }

    [Test]
    public void ABreakIsMeasuredAgainstTheRecordingRatherThanThePart()
    {
        // the running total carries across the join, so a break eight seconds in
        // lands in the second part when the first holds two segments
        RecordingSegment[] segments =
            [Segment(0, 0, 10), Segment(0, 10, 10), Segment(1, 0, 10), Segment(1, 10, 10)];

        var wanted = RecordingPlaylist.WithoutBreaks(segments, [Break(8, 12)]);

        Assert.That(wanted, Is.EqualTo(new[] { segments[0], segments[1], segments[3] }));
    }
}
