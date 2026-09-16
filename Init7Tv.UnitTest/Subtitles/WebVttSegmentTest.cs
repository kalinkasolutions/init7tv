using Init7Tv.BusinessLogic.Subtitles;

namespace Init7Tv.UnitTest.Subtitles;

/// <summary>The subtitle segment that sits beside a media segment.</summary>
public class WebVttSegmentTest
{
    private static WebVttCue Cue(double start, double end, string text = "text") => new()
    {
        Start = TimeSpan.FromSeconds(start),
        End = TimeSpan.FromSeconds(end),
        Text = text
    };

    private static string Build(IEnumerable<WebVttCue> cues, double start, double end) =>
        WebVttSegment.Build(cues, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    [Test]
    public void EverySegmentSaysWhereItsTimingsSit()
    {
        // a player reads each segment on its own and has nothing else to go on
        var body = Build([], 0, 2);

        Assert.That(body, Does.StartWith("WEBVTT\n"));
        Assert.That(body, Does.Contain("X-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:0"));
    }

    [Test]
    public void ASegmentWithNothingToSayIsStillValid()
    {
        // most segments have no caption in them, and they still have to parse
        var body = Build([Cue(10, 12)], 0, 2);

        Assert.That(body, Does.Not.Contain("-->"));
        Assert.That(body, Does.StartWith("WEBVTT"));
    }

    [Test]
    public void OnlyTheCuesInTheWindowAreWritten()
    {
        var cues = new[] { Cue(0.5, 1.5, "before"), Cue(2.5, 3.5, "inside"), Cue(9, 10, "after") };

        var body = Build(cues, 2, 4);

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("inside"));
            Assert.That(body, Does.Not.Contain("before"));
            Assert.That(body, Does.Not.Contain("after"));
        });
    }

    [Test]
    public void ACaptionSpanningTheBoundaryIsInBothSegments()
    {
        // otherwise it disappears mid sentence for whoever joined at the later one
        var cue = Cue(1.5, 2.5, "spanning");

        Assert.Multiple(() =>
        {
            Assert.That(Build([cue], 0, 2), Does.Contain("spanning"));
            Assert.That(Build([cue], 2, 4), Does.Contain("spanning"));
        });
    }

    [Test]
    public void ACueEndingExactlyOnTheBoundaryBelongsToTheEarlierSegment()
    {
        var cue = Cue(1.0, 2.0, "ends at two");

        Assert.Multiple(() =>
        {
            Assert.That(Build([cue], 0, 2), Does.Contain("ends at two"));
            Assert.That(Build([cue], 2, 4), Does.Not.Contain("ends at two"));
        });
    }

    [Test]
    public void TimesAreWrittenAsTheFormatRequires()
    {
        var body = Build([Cue(3661.5, 3662.25)], 3600, 3700);

        Assert.That(body, Does.Contain("01:01:01.500 --> 01:01:02.250"));
    }

    [Test]
    public void CuesComeOutInOrder()
    {
        var body = Build([Cue(3, 4, "second"), Cue(2, 3, "first")], 0, 10);

        Assert.That(body.IndexOf("first", StringComparison.Ordinal),
            Is.LessThan(body.IndexOf("second", StringComparison.Ordinal)));
    }

    [Test]
    public void TheLineBreaksInsideACaptionSurvive()
    {
        var body = Build([Cue(1, 2, "first line\nsecond line")], 0, 4);

        Assert.That(body, Does.Contain("first line\nsecond line"));
    }

    [Test]
    public void ACueIsSeparatedFromTheHeaderAndFromTheNextOne()
    {
        // without the blank lines the whole thing is one unreadable block
        var body = Build([Cue(1, 2, "one"), Cue(3, 4, "two")], 0, 10);

        Assert.That(body, Does.Contain("MPEGTS:0\n\n00:00:01.000"));
        Assert.That(body, Does.Contain("one\n\n00:00:03.000"));
    }
}
