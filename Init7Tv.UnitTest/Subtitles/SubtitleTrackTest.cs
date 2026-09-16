using Init7Tv.BusinessLogic.Ffprobe;

namespace Init7Tv.UnitTest.Subtitles;

/// <summary>Which of a channel's subtitle streams can be offered to a viewer.</summary>
public class SubtitleTrackTest
{
    private static StreamInfo Stream(string type, string codec, string? language = null) => new()
    {
        CodecType = type,
        CodecName = codec,
        Tags = language == null ? null : new Dictionary<string, string> { ["language"] = language }
    };

    [Test]
    public void TeletextIsOffered()
    {
        // it decodes to words, which is what a browser can show
        var probe = new FfprobeRoot
        {
            Streams = [Stream("video", "h264"), Stream("audio", "ac3", "deu"),
                       Stream("subtitle", "dvb_teletext", "deu")]
        };

        Assert.That(probe.GetSubtitleTracks, Has.Length.EqualTo(1));
        Assert.That(probe.GetSubtitleTracks[0].Language, Is.EqualTo("deu"));
    }

    [Test]
    public void BitmapSubtitlesAreNotOffered()
    {
        // dvb_subtitle is pictures of text; offering it would give an empty track
        var probe = new FfprobeRoot
        {
            Streams = [Stream("video", "h264"), Stream("subtitle", "dvb_subtitle", "fra")]
        };

        Assert.That(probe.GetSubtitleTracks, Is.Empty);
    }

    [Test]
    public void TheIndexIsCountedAmongSubtitleStreamsOnly()
    {
        // it goes into ffmpeg as -map 0:s:N, which numbers subtitles from zero
        // whatever the streams around them are
        var probe = new FfprobeRoot
        {
            Streams =
            [
                Stream("video", "h264"),
                Stream("audio", "ac3", "deu"),
                Stream("audio", "ac3", "eng"),
                Stream("subtitle", "dvb_subtitle", "deu"),
                Stream("subtitle", "dvb_teletext", "fra")
            ]
        };

        Assert.That(probe.GetSubtitleTracks, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(probe.GetSubtitleTracks[0].SubtitleStreamIndex, Is.EqualTo(1));
            Assert.That(probe.GetSubtitleTracks[0].Language, Is.EqualTo("fra"));
        });
    }

    [Test]
    public void ATrackWithNoLanguageTagIsStillOffered()
    {
        var probe = new FfprobeRoot { Streams = [Stream("subtitle", "dvb_teletext")] };

        Assert.That(probe.GetSubtitleTracks[0].Language, Is.EqualTo("und"));
    }

    [Test]
    public void AChannelWithNoSubtitlesHasNone()
    {
        var probe = new FfprobeRoot { Streams = [Stream("video", "h264"), Stream("audio", "ac3", "deu")] };

        Assert.That(probe.GetSubtitleTracks, Is.Empty);
    }
}
