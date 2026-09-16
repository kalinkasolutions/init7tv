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
    public void IsInterlaced_PrefersTheCodedFramesOverTheFrameRate()
    {
        // a 50fps source whose frames say interlaced is interlaced
        Assert.That(WithFrames("50/1", 1, 1, 1, 1).IsInterlaced, Is.True);
        // and a 25fps source whose frames say progressive is not
        Assert.That(WithFrames("25/1", 0, 0, 0, 0).IsInterlaced, Is.False);
    }

    [Test]
    public void IsInterlaced_TakesTheMajorityWhenFramesDisagree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WithFrames("25/1", 1, 1, 1, 0).IsInterlaced, Is.True);
            Assert.That(WithFrames("25/1", 1, 0, 0, 0).IsInterlaced, Is.False);
        });
    }

    [Test]
    public void IsInterlaced_IgnoresAudioFrames()
    {
        var probe = Video("25/1");
        probe.Frames =
        [
            new FrameInfo { MediaType = "audio", InterlacedFrame = 0 },
            new FrameInfo { MediaType = "audio", InterlacedFrame = 0 },
            new FrameInfo { MediaType = "video", InterlacedFrame = 1 }
        ];

        Assert.That(probe.IsInterlaced, Is.True);
    }

    [Test]
    public void IsInterlaced_FallsBackToFrameRateWhenNoFramesWereRead()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Video("25/1").IsInterlaced, Is.True);
            Assert.That(Video("50/1").IsInterlaced, Is.False);
        });
    }

    [Test]
    public void IsInterlaced_IsFalseWithoutAVideoStream()
    {
        Assert.That(new FfprobeRoot().IsInterlaced, Is.False);
    }
}
