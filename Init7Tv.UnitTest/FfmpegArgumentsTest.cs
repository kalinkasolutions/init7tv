using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.StreamManager;
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

    [Test]
    public void OutputIsAlwaysMpegtsOnStdout()
    {
        foreach (var args in new[] { Build(true, true), Build(false, true), Build(true, false), Build(false, false) })
        {
            Assert.Multiple(() =>
            {
                Assert.That(ValueOf(args, "-f"), Is.EqualTo("mpegts"));
                Assert.That(args[^1], Is.EqualTo("pipe:1"));
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
        foreach (var args in new[] { Build(true, true), Build(false, false), BuildRecording() })
        {
            Assert.That(args, Is.All.Not.Null);
            Assert.That(args.Any(string.IsNullOrWhiteSpace), Is.False);
        }
    }

    // --- recording -------------------------------------------------------

    private const string CapturePath = "/var/srv/recordings/abc/capture-1.ts";

    private static string[] BuildRecording(bool interlaced = true, bool multicast = true, params int[] audioStreams) =>
        FfmpegArguments.BuildRecording(
            Channel, audioStreams.Length == 0 ? [0] : audioStreams, "veryfast", "warning", Probe(interlaced),
            multicast, keyframeSeconds: 4, TimeSpan.FromMinutes(65), CapturePath);

    /// <summary>
    /// A recording is kept for weeks and the choice cannot be revisited, so it carries every track
    /// the channel offered rather than the one that looked best on the day.
    /// </summary>
    [Test]
    public void Recording_CarriesEveryAudioTrack()
    {
        var args = BuildRecording(audioStreams: [1, 0, 2]);

        var mapped = args
            .Select((arg, i) => (arg, i))
            .Where(x => x.arg == "-map")
            .Select(x => args[x.i + 1])
            .Where(x => x.StartsWith("0:a:"))
            .ToArray();

        // in the order given, because a player with no way to choose takes the first
        Assert.That(mapped, Is.EqualTo(new[] { "0:a:1", "0:a:0", "0:a:2" }));
    }

    /// <summary>Watching is one track, the one the viewer asked for: they can ask again.</summary>
    [Test]
    public void Live_CarriesOnlyTheChosenTrack()
    {
        var args = Build(audioStreamIndex: 2);

        Assert.That(args.Count(x => x.StartsWith("0:a:")), Is.EqualTo(1));
        Assert.That(args, Does.Contain("0:a:2"));
    }

    [Test]
    public void Recording_WritesAFileRatherThanThePipe()
    {
        var args = BuildRecording();

        Assert.Multiple(() =>
        {
            Assert.That(args[^1], Is.EqualTo(CapturePath));
            Assert.That(args, Does.Not.Contain("pipe:1"));
            Assert.That(ValueOf(args, "-f"), Is.EqualTo("mpegts"));
        });
    }

    /// <summary>Without these ffmpeg asks on stdin whether to overwrite and waits for ever.</summary>
    [Test]
    public void Recording_NeverWaitsOnStdin()
    {
        var args = BuildRecording();

        Assert.That(args, Does.Contain("-nostdin"));
        Assert.That(args, Does.Contain("-y"));
    }

    /// <summary>-t is what ends a recording; the scheduler killing it is the fallback.</summary>
    [Test]
    public void Recording_EndsItselfAfterTheWindow()
    {
        Assert.That(ValueOf(BuildRecording(), "-t"), Is.EqualTo("3900"));
    }

    [Test]
    public void Recording_UsesItsOwnPresetAndKeyframeSpacing()
    {
        var args = BuildRecording();

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-preset"), Is.EqualTo("veryfast"));
            Assert.That(ValueOf(args, "-force_key_frames"), Is.EqualTo("expr:gte(t,n_forced*4)"));
            // twice the measured rate over the interval, so -g can never fire first
            Assert.That(ValueOf(args, "-g"), Is.EqualTo("200"));
        });
    }

    /// <summary>The live path is what the rest of this file pins, so it must not
    /// pick anything up from the recording one.</summary>
    [Test]
    public void Recording_ReadsTheSameSourceAsTheLiveStream()
    {
        Assert.That(ValueOf(BuildRecording(multicast: true), "-i"),
            Is.EqualTo(ValueOf(Build(multicast: true), "-i")));
        Assert.That(ValueOf(BuildRecording(multicast: false), "-i"),
            Is.EqualTo(ValueOf(Build(multicast: false), "-i")));
    }

    [Test]
    public void Recording_DeinterlacesOnlyWhenTheSourceIsInterlaced()
    {
        Assert.That(BuildRecording(interlaced: true), Does.Contain("-vf"));
        Assert.That(BuildRecording(interlaced: false), Does.Not.Contain("-vf"));
    }

    /// <summary>
    /// Read from the source separately they would have to be lined up against a transcode that
    /// starts whenever it starts; carried in beside the pictures they are already on the same clock.
    /// </summary>
    [Test]
    public void Recording_CarriesTheAdvertisingCuesIntoTheCapture()
    {
        var args = BuildRecording();

        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("-copy_unknown"));
            Assert.That(args, Does.Contain("0:d:0?"), "optional, so a channel carrying none still records");
            Assert.That(ValueOf(args, "-c:d"), Is.EqualTo("copy"), "copied, never decoded");
        });
    }

    [Test]
    public void Download_CopiesRatherThanEncodes()
    {
        var args = FfmpegArguments.BuildDownload("error");

        Assert.Multiple(() =>
        {
            Assert.That(ValueOf(args, "-c"), Is.EqualTo("copy"));
            Assert.That(args, Does.Not.Contain("libx264"));
            Assert.That(ValueOf(args, "-bsf:a"), Is.EqualTo("aac_adtstoasc"));
            Assert.That(args[^1], Is.EqualTo("pipe:1"), "written as it is sent rather than kept");
            Assert.That(ValueOf(args, "-movflags"), Does.Contain("frag_keyframe"),
                "an ordinary mp4 cannot be written to something that cannot be seeked back into");
        });
    }
}
