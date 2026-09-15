using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.Dto;

namespace Init7Tv.UnitTest;

/// <summary>
/// The audio index the player sends back is fed straight to ffmpeg's -map 0:a:N,
/// so it has to be a position among the audio streams and nothing else.
/// </summary>
public class StreamMapperTest
{
    private static StreamInfo Audio(string? language) => new()
    {
        CodecType = "audio",
        Tags = language == null ? null : new Dictionary<string, string> { ["language"] = language }
    };

    private static StreamInfo Video() => new() { CodecType = "video" };

    private static StreamInfo Subtitle() => new() { CodecType = "subtitle" };

    private static TvStream StreamOf(int audioStreamIndex, params StreamInfo[] streams) => new()
    {
        StreamId = "test",
        AudioStreamIndex = audioStreamIndex,
        StreamInfo = new FfprobeRoot { Streams = streams.ToList() },
        Channel = new ChannelDto()
    };

    [Test]
    public void ToDto_NumbersAudioTracksFromZero()
    {
        var stream = StreamOf(0, Video(), Audio("ger"), Audio("fre"), Audio("ita"));

        Assert.That(stream.ToDto().Languages.Select(x => (x.Language, x.AudioStreamIndex)), Is.EqualTo(new[]
        {
            ("ger", 0), ("fre", 1), ("ita", 2)
        }));
    }

    [Test]
    public void ToDto_IsNotThrownOffByAStreamBeforeTheVideo()
    {
        var stream = StreamOf(0, Subtitle(), Video(), Audio("ger"), Audio("fre"));

        Assert.That(stream.ToDto().Languages.Select(x => x.AudioStreamIndex), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void ToDto_HidesUntaggedTracksButKeepsTheNumberingAligned()
    {
        var stream = StreamOf(0, Video(), Audio("ger"), Audio(null), Audio("ita"));

        var languages = stream.ToDto().Languages.ToArray();

        Assert.That(languages, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(languages[0].AudioStreamIndex, Is.EqualTo(0));
            // ita is still the third audio track, so ffmpeg needs 2 and not 1
            Assert.That(languages[1].AudioStreamIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void GetStreamedLanguage_ResolvesAgainstTheSameNumbering()
    {
        var stream = StreamOf(2, Video(), Audio("ger"), Audio(null), Audio("ita"));

        Assert.That(stream.GetStreamedLanguage, Is.EqualTo("ita"));
    }

    [Test]
    public void GetStreamedLanguage_FallsBackInsteadOfThrowingOnAnOutOfRangeTrack()
    {
        var stream = StreamOf(7, Video(), Audio("ger"));

        Assert.That(stream.GetStreamedLanguage, Is.EqualTo("unknown"));
    }

    [Test]
    public void GetStreamedLanguage_FallsBackWhenTheSelectedTrackHasNoLanguage()
    {
        var stream = StreamOf(1, Video(), Audio("ger"), Audio(null));

        Assert.That(stream.GetStreamedLanguage, Is.EqualTo("unknown"));
    }
}
