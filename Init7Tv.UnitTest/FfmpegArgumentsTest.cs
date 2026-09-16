using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.BusinessLogic.Subtitles;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;

namespace Init7Tv.UnitTest;

/// <summary>
/// Pins the ffmpeg command line. Every channel goes through this one builder, so
/// a change made to fix one of them is easy to make while breaking the rest.
/// </summary>
public class FfmpegArgumentsTest
{
    private const int SegmentSeconds = 2;

    private static readonly ChannelDto Channel = new()
    {
        ChannelId = Guid.NewGuid(),
        DisplayName = "SAT.1 (Schweiz)",
        CanonicalName = "SAT1.de",
        UdpSource = "udp://@233.50.230.9:5000",
        HlsSource = "https://api.tv.init7.net/api/v4/live/?channel=abc",
        MainLanguage = "de",
        Logo = []
    };

    private static readonly GeneralAppSettingsDto Settings = new()
    {
        BaseDomain = "https://tv.example.org",
        FfmpegPreset = "ultrafast",
        FfmpegLogLevel = "warning"
    };

    private static FfprobeRoot Probe(bool interlaced, string frameRate = "", bool topFieldFirst = true) => new()
    {
        Streams =
        [
            new StreamInfo
            {
                CodecType = "video",
                RFrameRate = frameRate != "" ? frameRate : (interlaced ? "25/1" : "50/1")
            }
        ],
        Frames =
        [
            new FrameInfo
            {
                MediaType = "video",
                InterlacedFrame = interlaced ? 1 : 0,
                TopFieldFirst = topFieldFirst ? 1 : 0
            }
        ]
    };

    private static string[] Build(bool interlaced = true, bool multicast = true, int audioStreamIndex = 0) =>
        FfmpegArguments.Build(Channel, audioStreamIndex, Settings, Probe(interlaced), multicast, SegmentSeconds);

    /// <summary>Value that follows a flag, so order-insensitive assertions stay readable.</summary>
    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    [Test]
    public void Multicast_ReadsTheUdpSourceWithItsBuffering()
    {
        var args = Build(multicast: true);

        Assert.That(ValueOf(args, "-i"), Is.EqualTo("udp://@233.50.230.9:5000?fifo_size=1000000&overrun_nonfatal=1"));
        Assert.That(args, Does.Not.Contain(Channel.HlsSource));
    }

    [Test]
    public void Hls_ReadsTheHlsSourceAndDoesNotApplyUdpOnlyOptions()
    {
        var args = Build(multicast: false);

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-i"), Is.EqualTo(Channel.HlsSource));
            Assert.That(args, Does.Not.Contain("-fflags"));
            Assert.That(args, Does.Not.Contain("low_delay"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LowDelayIsNeverSet_ItReordersBFrameSourcesWrongly(bool multicast)
    {
        // these sources are MPEG-2 with B frames. low_delay makes the decoder
        // assume no reorder delay, so frames come out in decode order and the
        // motion goes forward, back, forward.
        var args = Build(multicast: multicast);

        Assert.That(args, Does.Not.Contain("low_delay"));
        Assert.That(args.Any(a => a.Contains("low_delay")), Is.False);
    }

    [Test]
    public void InterlacedSource_IsDeinterlaced()
    {
        var args = Build(interlaced: true);

        Assert.That(ValueOf(args, "-vf"), Is.EqualTo("yadif=mode=send_field:parity=0"));
    }

    [Test]
    public void FieldOrderComesFromTheProbe_NotFromParityAuto()
    {
        // parity=auto reads stream metadata that these multicasts report
        // inconsistently, so the probed frames decide instead
        var tff = FfmpegArguments.Build(Channel, 0, Settings, Probe(true, topFieldFirst: true), true, SegmentSeconds);
        var bff = FfmpegArguments.Build(Channel, 0, Settings, Probe(true, topFieldFirst: false), true, SegmentSeconds);

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(tff, "-vf"), Does.Contain("parity=0"));
            Assert.That(ValueOf(bff, "-vf"), Does.Contain("parity=1"));
            Assert.That(ValueOf(tff, "-vf"), Does.Not.Contain("auto"));
        });
    }

    [Test]
    public void ProgressiveSource_IsNotFilteredAtAll()
    {
        // filtering progressive video softens it and buys nothing
        var args = Build(interlaced: false);

        Assert.That(args, Does.Not.Contain("-vf"));
        Assert.That(args.Any(a => a.Contains("yadif")), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void KeyframesAreForcedAtTheSegmentLength_WhateverTheSource(bool interlaced)
    {
        // segments are cut on keyframes, so this has to track SegmentSeconds and
        // must not depend on frame rate: -g alone is a frame count and drifts
        var args = Build(interlaced);

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-force_key_frames"), Is.EqualTo($"expr:gte(t,n_forced*{SegmentSeconds})"));
            Assert.That(ValueOf(args, "-x264-params"), Is.EqualTo("scenecut=0"), "an extra keyframe would split a segment early");
            Assert.That(int.Parse(ValueOf(args, "-g")!), Is.GreaterThan(SegmentSeconds),
                "-g must be far enough out that it never fires before the forced keyframe");
        });
    }

    [TestCase("25/1", 100)]
    [TestCase("50/1", 200)]
    [TestCase("30000/1001", 120)]
    public void TheGopIsDerivedFromTheMeasuredFrameRate(string frameRate, int expected)
    {
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true, frameRate), true, SegmentSeconds);

        Assert.That(int.Parse(ValueOf(args, "-g")!), Is.EqualTo(expected));
    }

    [Test]
    public void TheGopFallsBackWhenTheFrameRateIsUnknown()
    {
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true, "0/0"), true, SegmentSeconds);

        Assert.That(int.Parse(ValueOf(args, "-g")!), Is.GreaterThan(SegmentSeconds * 60));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void TheSelectedAudioTrackIsMapped(int audioStreamIndex)
    {
        var args = Build(audioStreamIndex: audioStreamIndex);

        Assert.That(args, Does.Contain($"0:a:{audioStreamIndex}"));
    }

    private static readonly SubtitleTrack Teletext = new() { SubtitleStreamIndex = 1, Language = "deu" };

    [Test]
    public void WithNoSubtitleChosen_NothingAboutSubtitlesIsPassed()
    {
        var args = Build();

        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Not.Contain("-txt_page"));
            Assert.That(args, Does.Not.Contain("-fix_sub_duration"));
            Assert.That(args, Does.Not.Contain("-c:s"));
            Assert.That(args[^1], Is.EqualTo("pipe:1"), "the transport stream is the only output");
        });
    }

    [Test]
    public void AChosenSubtitle_IsWrittenAsWebvttBesideTheStream()
    {
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, "/tmp/subs.vtt");

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-c:s"), Is.EqualTo("webvtt"));
            Assert.That(args, Does.Contain("0:s:1"), "the track is picked by its position among subtitles");
            Assert.That(args[^1], Is.EqualTo("/tmp/subs.vtt"));
            Assert.That(args.Count(x => x == "-f"), Is.EqualTo(2), "one format for each output");
        });
    }

    [Test]
    public void TheTransportStreamIsStillTheFirstOutput()
    {
        // the segmenter reads stdout, and a second output must not displace it
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, "/tmp/subs.vtt");

        Assert.That(Array.IndexOf(args, "pipe:1"),
            Is.LessThan(Array.IndexOf(args, "/tmp/subs.vtt")));
    }

    [Test]
    public void OnlyTheSubtitlePagesAreDecoded_AsText()
    {
        // teletext carries the whole service; decoding all of it gives pages of
        // sports results where the captions should be
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, "/tmp/subs.vtt");

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-txt_page"), Is.EqualTo("subtitle"));
            Assert.That(ValueOf(args, "-txt_format"), Is.EqualTo("text"));
        });
    }

    [Test]
    public void TheSubtitlePipeIsWrittenToEvenThoughItExists()
    {
        // it is created before ffmpeg starts, and ffmpeg will not write over
        // something already there unless told to
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, "/tmp/subs.vtt");

        Assert.That(args, Does.Contain("-y"));
    }

    [Test]
    public void WithoutSubtitles_NothingIsOverwritten()
    {
        // stdout is the only output then, and -y would be a licence to clobber
        Assert.That(Build(), Does.Not.Contain("-y"));
    }

    [Test]
    public void CaptionsAreGivenAnEnd()
    {
        // without this ffmpeg ends every caption hours later and they never clear
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, "/tmp/subs.vtt");

        Assert.That(args, Does.Contain("-fix_sub_duration"));
    }

    [Test]
    public void TheTeletextOptionsComeBeforeTheInput()
    {
        // they configure the decoder, which is chosen when the input is opened
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, "/tmp/subs.vtt");
        var input = Array.IndexOf(args, "-i");

        Assert.Multiple(() =>
        {
            Assert.That(Array.IndexOf(args, "-txt_page"), Is.LessThan(input));
            Assert.That(Array.IndexOf(args, "-txt_format"), Is.LessThan(input));
            Assert.That(Array.IndexOf(args, "-fix_sub_duration"), Is.LessThan(input));
        });
    }

    [Test]
    public void ASubtitleWithNowhereToGo_IsNotMapped()
    {
        // the path is what ffmpeg writes to, and without one the output is invalid
        var args = FfmpegArguments.Build(Channel, 0, Settings, Probe(true), true, SegmentSeconds,
            Teletext, null);

        Assert.That(args, Does.Not.Contain("-c:s"));
        Assert.That(args[^1], Is.EqualTo("pipe:1"));
    }

    [Test]
    public void OutputIsAlwaysMpegtsOnStdout()
    {
        foreach (var args in new[] { Build(true, true), Build(false, true), Build(true, false), Build(false, false) })
        {
            Assert.Multiple(() =>
            {
                Assert.That(ValueOf(args, "-f"), Is.EqualTo("mpegts"));
                Assert.That(args[^1], Is.EqualTo("pipe:1"));   // no subtitle chosen here
                Assert.That(ValueOf(args, "-c:v"), Is.EqualTo("libx264"));
                Assert.That(ValueOf(args, "-pix_fmt"), Is.EqualTo("yuv420p"));
            });
        }
    }

    [Test]
    public void AudioIsAlwaysNormalisedToStereoAac()
    {
        var args = Build();

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-c:a"), Is.EqualTo("aac"));
            Assert.That(ValueOf(args, "-ac"), Is.EqualTo("2"));
            Assert.That(ValueOf(args, "-ar"), Is.EqualTo("48000"));
        });
    }

    [Test]
    public void TheConfiguredPresetAndLogLevelAreUsed()
    {
        var args = FfmpegArguments.Build(
            Channel, 0,
            new GeneralAppSettingsDto { BaseDomain = "x", FfmpegPreset = "veryfast", FfmpegLogLevel = "debug" },
            Probe(true), true, SegmentSeconds);

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-preset"), Is.EqualTo("veryfast"));
            Assert.That(ValueOf(args, "-loglevel"), Is.EqualTo("debug"));
        });
    }

    [Test]
    public void InputOptionsComeBeforeTheInput()
    {
        // ffmpeg applies an option to whichever file follows it, so anything
        // meant for the input has to precede -i
        var args = Build(multicast: true);
        var input = Array.IndexOf(args, "-i");

        Assert.Multiple(() =>
        {
            Assert.That(Array.IndexOf(args, "-fflags"), Is.LessThan(input));
            Assert.That(Array.IndexOf(args, "-analyzeduration"), Is.LessThan(input));
            Assert.That(Array.IndexOf(args, "-probesize"), Is.LessThan(input));
            Assert.That(Array.IndexOf(args, "-c:v"), Is.GreaterThan(input));
            Assert.That(Array.IndexOf(args, "-map"), Is.GreaterThan(input));
        });
    }

    [Test]
    public void EveryArgumentIsASingleToken()
    {
        // they are passed through ArgumentList, so a value containing a space
        // would otherwise have to be quoted and could split
        foreach (var args in new[] { Build(true, true), Build(false, false) })
        {
            Assert.That(args, Is.All.Not.Null);
            Assert.That(args.Any(string.IsNullOrWhiteSpace), Is.False);
        }
    }
}
