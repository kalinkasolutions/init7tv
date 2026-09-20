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

    private static FfprobeRoot WithAudio(params (string Language, int Channels)[] tracks) =>
        WithTracks(tracks.Select(t => (t.Language, t.Channels, false)).ToArray());

    private static FfprobeRoot WithTracks(params (string Language, int Channels, bool Described)[] tracks) => new()
    {
        Streams = tracks
            .Select(t => new StreamInfo
            {
                CodecType = "audio",
                Channels = t.Channels,
                Tags = new Dictionary<string, string> { ["language"] = t.Language },
                Disposition = new Dictionary<string, int>
                {
                    ["visual_impaired"] = t.Described ? 1 : 0,
                    ["descriptions"] = t.Described ? 1 : 0
                }
            })
            .ToList()
    };

    /// <summary>
    /// Folding a 5.1 mix down puts the dialogue well below the rest, so a stereo track carrying the
    /// programme is what to record.
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

    /// <summary>
    /// What SRF zwei FHD actually carries: German 5.1, English 5.1, and a German stereo track that
    /// is the description for the visually impaired. Preferring stereo recorded the description,
    /// which is silence with a narrator over it.
    /// </summary>
    [Test]
    public void ADescribedTrackIsNeverTheChoice()
    {
        var probe = WithTracks(("deu", 6, false), ("eng", 6, false), ("deu", 2, true));

        Assert.That(probe.GetPreferredAudioStream("de"), Is.Zero);
    }

    [Test]
    public void ADescribedTrackIsPassedOverEvenWhenNothingElseSpeaksTheLanguage()
    {
        var probe = WithTracks(("deu", 2, true), ("eng", 2, false));

        Assert.That(probe.GetPreferredAudioStream("de"), Is.EqualTo(1));
    }

    /// <summary>A description is still better than nothing at all.</summary>
    [Test]
    public void ADescribedTrackIsTakenWhenItIsAllThereIs()
    {
        Assert.That(WithTracks(("deu", 2, true)).GetPreferredAudioStream("de"), Is.Zero);
    }

    /// <summary>The description is kept too: what is not recorded can never be chosen later.</summary>
    [Test]
    public void EveryTrackIsRecordedWithThePreferredOneLeading()
    {
        var probe = WithTracks(("deu", 6, false), ("eng", 6, false), ("deu", 2, true));

        Assert.That(probe.GetAudioStreamsToRecord("de"), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void ThePreferredTrackLeadsEvenWhenItIsNotTheFirst()
    {
        var probe = WithAudio(("eng", 2), ("deu", 6), ("deu", 2));

        // the stereo German one is the pick, and the rest follow in the order the channel had them
        Assert.That(probe.GetAudioStreamsToRecord("de"), Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void NoAudioIsNothingToRecord()
    {
        Assert.That(new FfprobeRoot().GetAudioStreamsToRecord("de"), Is.Empty);
    }

    /// <summary>
    /// What the SRG channels actually carry: an unnamed data stream, then the cue stream. Taking the
    /// first one copied a PID with nothing on it and no recording ever found any advertising.
    /// </summary>
    [Test]
    public void TheCueStreamIsFoundByCodecRatherThanPosition()
    {
        var probe = new FfprobeRoot
        {
            Streams =
            [
                new StreamInfo { CodecType = "video", CodecName = "hevc" },
                new StreamInfo { CodecType = "audio", CodecName = "ac3" },
                new StreamInfo { CodecType = "data", CodecName = "unknown" },
                new StreamInfo { CodecType = "data", CodecName = "scte_35" }
            ]
        };

        Assert.That(probe.GetCueStream(), Is.EqualTo(1));
    }

    [Test]
    public void AChannelWithNoCueStreamHasNothingToMap()
    {
        var probe = new FfprobeRoot
        {
            Streams =
            [
                new StreamInfo { CodecType = "video", CodecName = "hevc" },
                new StreamInfo { CodecType = "data", CodecName = "unknown" }
            ]
        };

        Assert.That(probe.GetCueStream(), Is.Null);
    }

    [Test]
    public void OneTrackIsTheOnlyChoice()
    {
        Assert.That(WithAudio(("deu", 6)).GetPreferredAudioStream("de"), Is.Zero);
        Assert.That(new FfprobeRoot().GetPreferredAudioStream("de"), Is.Zero);
    }
}
