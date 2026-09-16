using Init7Tv.BusinessLogic.Ffprobe;

namespace Init7Tv.UnitTest.Subtitles;

/// <summary>Which of a channel's subtitle streams can be offered to a viewer.</summary>
public class SubtitleTrackTest
{
    /// <summary>SRF 1: index page 100 and German captions on 777.</summary>
    private const string OnePage = "00000000: 0900 1777                                ....";

    /// <summary>arte D: index 100, German captions on 150, French on 888.</summary>
    private const string TwoPages = "00000000: 0900 1150 2888                           ...P(.";

    private static StreamInfo Stream(string type, string codec, string? language = null,
        string? extraData = null) => new()
    {
        CodecType = type,
        CodecName = codec,
        ExtraData = extraData,
        Tags = language == null ? null : new Dictionary<string, string> { ["language"] = language }
    };

    [Test]
    public void TeletextIsOffered()
    {
        // it decodes to words, which is what a browser can show
        var probe = new FfprobeRoot
        {
            Streams = [Stream("video", "h264"), Stream("audio", "ac3", "deu"),
                       Stream("subtitle", "dvb_teletext", "deu,deu", OnePage)]
        };

        Assert.That(probe.GetSubtitleTracks, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(probe.GetSubtitleTracks[0].Language, Is.EqualTo("deu"));
            Assert.That(probe.GetSubtitleTracks[0].Page, Is.EqualTo(777));
        });
    }

    [Test]
    public void EachSubtitlePageIsItsOwnTrack()
    {
        // arte D carries German and French in one teletext stream. Offering the
        // stream rather than its pages gives one track of two languages mixed.
        var probe = new FfprobeRoot
        {
            Streams = [Stream("video", "h264"), Stream("subtitle", "dvb_teletext", "deu,deu,fra", TwoPages)]
        };

        var tracks = probe.GetSubtitleTracks;

        Assert.That(tracks, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(tracks[0].Language, Is.EqualTo("deu"));
            Assert.That(tracks[0].Page, Is.EqualTo(150));
            Assert.That(tracks[0].HearingImpaired, Is.False);
            Assert.That(tracks[1].Language, Is.EqualTo("fra"));
            Assert.That(tracks[1].Page, Is.EqualTo(888));
            Assert.That(tracks[1].HearingImpaired, Is.True);
            Assert.That(tracks[1].Label, Is.EqualTo("fra (hard of hearing)"));
            Assert.That(tracks.Select(x => x.SubtitleStreamIndex), Is.All.EqualTo(0),
                "both come out of the same stream");
        });
    }

    [Test]
    public void TheIndexPageIsNotOfferedAsSubtitles()
    {
        // page 100 is the teletext service, which is not captions
        var probe = new FfprobeRoot
        {
            Streams = [Stream("subtitle", "dvb_teletext", "deu,deu", OnePage)]
        };

        Assert.That(probe.GetSubtitleTracks.Select(x => x.Page), Does.Not.Contain(100));
    }

    [Test]
    public void ATeletextStreamWithNoPagesReadableIsNotOffered()
    {
        // without the extradata there is no way to tell captions from the service
        var probe = new FfprobeRoot { Streams = [Stream("subtitle", "dvb_teletext", "deu,deu")] };

        Assert.That(probe.GetSubtitleTracks, Is.Empty);
    }

    [Test]
    public void BitmapSubtitlesAreNotOffered()
    {
        // dvb_subtitle is pictures of text; offering it would give an empty track
        var probe = new FfprobeRoot
        {
            Streams = [Stream("video", "h264"), Stream("subtitle", "dvb_subtitle", "fra", TwoPages)]
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
                Stream("subtitle", "dvb_teletext", "fra,fra", OnePage)
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
    public void AChannelWithNoSubtitlesHasNone()
    {
        var probe = new FfprobeRoot { Streams = [Stream("video", "h264"), Stream("audio", "ac3", "deu")] };

        Assert.That(probe.GetSubtitleTracks, Is.Empty);
    }
}
