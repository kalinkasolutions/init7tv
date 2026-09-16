using Init7Tv.BusinessLogic.Subtitles;

namespace Init7Tv.UnitTest.Subtitles;

/// <summary>
/// Reading the WebVTT ffmpeg writes as it decodes teletext. The fixtures are
/// the shapes it actually produced off SRF 1, including the timings it gets
/// wrong without -fix_sub_duration.
/// </summary>
public class WebVttReaderTest
{
    private WebVttReader m_reader = null!;

    [SetUp]
    public void SetUp() => m_reader = new WebVttReader();

    [Test]
    public void AWholeFileIsRead()
    {
        var vtt = "WEBVTT\n\n" +
                  "00:00.400 --> 00:02.120\nDas ist das Wichtigste.\n\n" +
                  "00:02.520 --> 00:04.400\nWir schneiden sie einmal ein.\n\n";

        var cues = m_reader.Read(vtt);

        Assert.That(cues, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(cues[0].Start, Is.EqualTo(TimeSpan.FromMilliseconds(400)));
            Assert.That(cues[0].End, Is.EqualTo(TimeSpan.FromMilliseconds(2120)));
            Assert.That(cues[0].Text, Is.EqualTo("Das ist das Wichtigste."));
            Assert.That(cues[1].Text, Is.EqualTo("Wir schneiden sie einmal ein."));
        });
    }

    [Test]
    public void ACaptionOfSeveralLinesKeepsItsLineBreaks()
    {
        // teletext captions are written to fit the screen, so where they break
        // is part of the caption
        var cues = m_reader.Read("WEBVTT\n\n00:05.160 --> 00:09.400\n" +
                                 "Ein X leicht auf der Haut -\nund dann in das heisse Wasser.\n\n");

        Assert.That(cues, Has.Count.EqualTo(1));
        Assert.That(cues[0].Text, Is.EqualTo("Ein X leicht auf der Haut -\nund dann in das heisse Wasser."));
    }

    [Test]
    public void ACueSplitAcrossChunksIsHeldUntilItIsWhole()
    {
        // it comes down a pipe, so a read can stop anywhere
        var first = m_reader.Read("WEBVTT\n\n00:00.400 --> 00:0");
        var second = m_reader.Read("2.120\nDas ist das ");
        var third = m_reader.Read("Wichtigste.\n\n");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Empty);
            Assert.That(second, Is.Empty);
            Assert.That(third, Has.Count.EqualTo(1));
            Assert.That(third[0].Text, Is.EqualTo("Das ist das Wichtigste."));
        });
    }

    [Test]
    public void AChunkEndingMidWordDoesNotSplitTheLine()
    {
        m_reader.Read("WEBVTT\n\n00:01.000 --> 00:02.000\nZwiebeln, Knob");
        var cues = m_reader.Read("lauch, Weisswein\n\n");

        Assert.That(cues[0].Text, Is.EqualTo("Zwiebeln, Knoblauch, Weisswein"));
    }

    [Test]
    public void ANewTimingEndsTheCueAboveIt()
    {
        // one caption replacing another without a blank line between them
        var cues = m_reader.Read("WEBVTT\n\n" +
                                 "00:01.000 --> 00:02.000\nfirst\n" +
                                 "00:02.000 --> 00:03.000\nsecond\n\n");

        Assert.That(cues.Select(x => x.Text), Is.EqualTo(new[] { "first", "second" }));
    }

    [TestCase("00:01.440", 1.44)]
    [TestCase("00:00:01.440", 1.44)]
    [TestCase("01:02.500", 62.5)]
    [TestCase("01:00:00.000", 3600)]
    [TestCase("1193:02:47.655", 4294967.655)]
    public void BothTimeShapesFfmpegWritesAreRead(string time, double seconds)
    {
        // it writes mm:ss.mmm for short files and hh:mm:ss.mmm once past an hour,
        // and the runaway end times it produces without -fix_sub_duration are hours
        var cues = m_reader.Read($"WEBVTT\n\n{time} --> {time}\ntext\n\n");

        Assert.That(cues, Has.Count.EqualTo(1));
        Assert.That(cues[0].Start.TotalSeconds, Is.EqualTo(seconds).Within(0.001));
    }

    [Test]
    public void CueSettingsAfterTheEndTimeAreIgnored()
    {
        var cues = m_reader.Read("WEBVTT\n\n00:01.000 --> 00:02.000 align:start position:10%\ntext\n\n");

        Assert.That(cues, Has.Count.EqualTo(1));
        Assert.That(cues[0].End, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void TheHeaderIsNotACue()
    {
        var cues = m_reader.Read("WEBVTT\n\n");

        Assert.That(cues, Is.Empty);
    }

    [Test]
    public void ATimingWithNoWordsUnderItIsNotACue()
    {
        var cues = m_reader.Read("WEBVTT\n\n00:01.000 --> 00:02.000\n\n");

        Assert.That(cues, Is.Empty);
    }

    [Test]
    public void ALineThatIsNotATimingBeforeAnyCueIsIgnored()
    {
        var cues = m_reader.Read("WEBVTT\nKind: captions\nLanguage: de\n\n00:01.000 --> 00:02.000\ntext\n\n");

        Assert.That(cues, Has.Count.EqualTo(1));
        Assert.That(cues[0].Text, Is.EqualTo("text"));
    }

    [Test]
    public void TheLastCueIsReleasedOnFlush()
    {
        // the stream stops without the blank line that would have ended it
        m_reader.Read("WEBVTT\n\n00:01.000 --> 00:02.000\ntext\n");

        var cue = m_reader.Flush();

        Assert.That(cue, Is.Not.Null);
        Assert.That(cue!.Text, Is.EqualTo("text"));
    }

    [Test]
    public void FlushingTwiceDoesNotRepeatTheCue()
    {
        m_reader.Read("WEBVTT\n\n00:01.000 --> 00:02.000\ntext\n");

        Assert.That(m_reader.Flush(), Is.Not.Null);
        Assert.That(m_reader.Flush(), Is.Null);
    }

    [Test]
    public void CarriageReturnsAreNotPartOfTheWords()
    {
        var cues = m_reader.Read("WEBVTT\r\n\r\n00:01.000 --> 00:02.000\r\ntext\r\n\r\n");

        Assert.That(cues, Has.Count.EqualTo(1));
        Assert.That(cues[0].Text, Is.EqualTo("text"));
    }

    [Test]
    public void NonsenseDoesNotThrow()
    {
        const string alphabet = "abc:->.0123\n\r ";
        var random = new Random(7);

        for (var i = 0; i < 500; i++)
        {
            var junk = new string(Enumerable.Range(0, random.Next(0, 80))
                .Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

            Assert.DoesNotThrow(() => m_reader.Read(junk));
        }
    }
}
