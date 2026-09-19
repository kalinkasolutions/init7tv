using Init7Tv.BusinessLogic.Ffprobe;

namespace Init7Tv.UnitTest;

public class FfprobeRootTest
{
    private static FfprobeRoot Video(string frameRate, string? fieldOrder = null) => new()
    {
        Streams = [new StreamInfo { CodecType = "video", RFrameRate = frameRate, FieldOrder = fieldOrder }]
    };

    private static FfprobeRoot WithFrames(string frameRate, params int[] interlacedFlags)
    {
        var probe = Video(frameRate);
        probe.Frames = interlacedFlags
            .Select(f => new FrameInfo { MediaType = "video", InterlacedFrame = f })
            .ToList();
        return probe;
    }

    [TestCase("25/1", true, TestName = "Interlaced_25fpsBroadcastCarries50Fields")]
    [TestCase("30000/1001", true, TestName = "Interlaced_2997fpsIsAlsoFieldBased")]
    [TestCase("50/1", false, TestName = "Interlaced_50fpsIsProgressive")]
    [TestCase("60/1", false, TestName = "Interlaced_60fpsIsProgressive")]
    public void IsInterlaced_IsDecidedOnFrameRate(string frameRate, bool expected)
    {
        Assert.That(Video(frameRate).IsInterlaced, Is.EqualTo(expected));
    }

    [Test]
    public void IsInterlaced_IgnoresFieldOrder()
    {
        // the same multicast reports either of these depending on probe length,
        // so the decision must not hinge on it
        Assert.Multiple(() =>
        {
            Assert.That(Video("25/1", "progressive").IsInterlaced, Is.True);
            Assert.That(Video("25/1", "tt").IsInterlaced, Is.True);
            Assert.That(Video("50/1", "tt").IsInterlaced, Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0/0")]
    [TestCase("nonsense")]
    public void IsInterlaced_IsFalseWhenTheRateIsUnusable(string? frameRate)
    {
        Assert.That(Video(frameRate!).IsInterlaced, Is.False);
    }

    [Test]
    public void IsInterlaced_IsNotDecidedByTheSampledContent()
    {
        // the probe sees about two seconds. SAT.1 sampled during an advert, which
        // is shot progressive, must not make the whole stream run un-deinterlaced
        // through the interlaced programme that follows
        Assert.Multiple(() =>
        {
            Assert.That(WithFrames("25/1", 0, 0, 0, 0).IsInterlaced, Is.True);
            Assert.That(WithFrames("25/1", 1, 1, 1, 1).IsInterlaced, Is.True);
            Assert.That(WithFrames("50/1", 1, 1, 1, 1).IsInterlaced, Is.False);
        });
    }

    [Test]
    public void IsInterlaced_IsFalseWithoutAVideoStream()
    {
        Assert.That(new FfprobeRoot().IsInterlaced, Is.False);
    }

    private static FfprobeRoot WithAudio(params (string Language, int Channels)[] tracks) => new()
    {
        Streams = tracks
            .Select(t => new StreamInfo
            {
                CodecType = "audio",
                Channels = t.Channels,
                Tags = new Dictionary<string, string> { ["language"] = t.Language }
            })
            .ToList()
    };

    /// <summary>
    /// These channels carry a 5.1 mix first and a stereo mix of the same language after it. Folding
    /// the 5.1 down puts the dialogue well below the rest, so the stereo one is what to record.
    /// </summary>
    [Test]
    public void AStereoTrackIsPreferredOverSurroundInTheSameLanguage()
    {
        var probe = WithAudio(("deu", 6), ("eng", 2), ("deu", 2));

        Assert.That(probe.GetPreferredAudioStream("de"), Is.EqualTo(2));
    }

    [Test]
    public void TheLanguageIsPreferredOverTheChannelCount()
    {
        var probe = WithAudio(("eng", 2), ("deu", 6));

        Assert.That(probe.GetPreferredAudioStream("de"), Is.EqualTo(1));
    }

    [Test]
    public void WithNothingInThatLanguageAStereoTrackIsStillPreferred()
    {
        var probe = WithAudio(("fra", 6), ("eng", 2));

        Assert.That(probe.GetPreferredAudioStream("de"), Is.EqualTo(1));
    }

    [Test]
    public void OneTrackIsTheOnlyChoice()
    {
        Assert.That(WithAudio(("deu", 6)).GetPreferredAudioStream("de"), Is.Zero);
        Assert.That(new FfprobeRoot().GetPreferredAudioStream("de"), Is.Zero);
    }
}
