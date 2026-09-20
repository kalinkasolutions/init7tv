using Init7Tv.BusinessLogic.StreamManager;
using static Init7Tv.UnitTest.TsPackets;

namespace Init7Tv.UnitTest;

public class HlsSegmenterTest
{
    private HlsSegmenter m_segmenter = null!;

    [SetUp]
    public void SetUp()
    {
        m_segmenter = new HlsSegmenter();
    }

    /// <summary>Feeds a whole stream in one read, as the buffer usually holds it.</summary>
    private SegmentCuts Feed(params byte[][] packets)
    {
        var stream = Stream(packets);
        return m_segmenter.Consume(stream, stream.Length);
    }

    private static byte[][] PacketsOf(SegmentCut cut) =>
        Enumerable.Range(0, cut.Bytes.Length / Size)
            .Select(i => cut.Bytes[(i * Size)..((i + 1) * Size)])
            .ToArray();

    [Test]
    public void NothingIsCutUntilASecondKeyframeArrives()
    {
        // the first keyframe opens a segment; it is only finished by the next one
        var cuts = Feed(Pat(), Pmt(), Keyframe(), Video(), Video());

        Assert.That(cuts.Segments, Is.Empty);
    }

    [Test]
    public void EachKeyframeFinishesTheSegmentBeforeIt()
    {
        var cuts = Feed(Pat(), Pmt(), Keyframe(), Video(), Keyframe(), Video(), Keyframe());

        Assert.That(cuts.Segments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ASegmentLeadsWithTheProgramTables()
    {
        var cuts = Feed(Pat(), Pmt(), Keyframe(), Video(), Keyframe());
        var packets = PacketsOf(cuts.Segments[0]);

        // a player that demuxes each segment on its own cannot tell what the
        // streams are without them, whatever else the segment holds
        Assert.Multiple(() =>
        {
            Assert.That(PidOf(packets[0]), Is.EqualTo(0x0000));
            Assert.That(PidOf(packets[1]), Is.EqualTo(PmtPid));
            Assert.That(PidOf(packets[2]), Is.EqualTo(VideoPid));
        });
    }

    [Test]
    public void TablesArrivingMidSegmentAreNotDroppedIntoIt()
    {
        // ffmpeg emits them periodically; only the copy at the head of a segment
        // is put there on purpose, the rest ride along where they fell
        var cuts = Feed(Pat(), Pmt(), Keyframe(), Video(), Pat(), Pmt(), Video(), Keyframe());
        var packets = PacketsOf(cuts.Segments[0]);

        Assert.That(packets.Select(PidOf), Is.EqualTo(new[]
        {
            0x0000, PmtPid,             // the set put in front
            VideoPid,
            VideoPid,
            0x0000, PmtPid,             // the set that simply arrived
            VideoPid
        }));
    }

    [Test]
    public void AnythingBeforeTheFirstKeyframeIsThrownAway()
    {
        // it cannot be decoded on its own, and a player handed it would show nothing
        var cuts = Feed(Pat(), Pmt(), Video(), Audio(), Keyframe(), Keyframe());
        var packets = PacketsOf(cuts.Segments[0]);

        Assert.That(packets, Has.Length.EqualTo(3), "the tables and the keyframe, and nothing before them");
    }

    [Test]
    public void ASegmentCountsTheKeyframesItHolds()
    {
        // the count is what the playlist turns into a duration
        var cuts = Feed(Pat(), Pmt(), Keyframe(), Video(), Keyframe());

        Assert.That(cuts.Segments[0].Keyframes, Is.EqualTo(1));
    }

    [Test]
    public void APacketSplitAcrossTwoReadsIsCarriedOver()
    {
        var stream = Stream(Pat(), Pmt(), Keyframe(), Video(), Keyframe());
        var buffer = new byte[stream.Length];

        // the second read ends half way through the closing keyframe
        var half = stream.Length - Size / 2;
        stream.AsSpan(0, half).CopyTo(buffer);

        var first = m_segmenter.Consume(buffer, half);
        Assert.Multiple(() =>
        {
            Assert.That(first.Segments, Is.Empty, "the keyframe that closes it is only half here");
            Assert.That(first.Carried, Is.EqualTo(Size / 2));
        });

        // the rest arrives behind what was carried, as the read loop appends it
        stream.AsSpan(half).CopyTo(buffer.AsSpan(first.Carried));
        var second = m_segmenter.Consume(buffer, first.Carried + (stream.Length - half));

        Assert.Multiple(() =>
        {
            Assert.That(second.Segments, Has.Count.EqualTo(1));
            Assert.That(second.Carried, Is.Zero);
            Assert.That(PacketsOf(second.Segments[0]), Has.Length.EqualTo(4), "PAT, PMT, keyframe, video");
        });
    }

    [Test]
    public void ASegmentIsTheSameHoweverTheReadsFellAcrossIt()
    {
        var stream = Stream(Pat(), Pmt(), Keyframe(), Video(), Audio(), Video(), Keyframe());

        var whole = new HlsSegmenter().Consume(stream.ToArray(), stream.Length).Segments[0];
        var dribbled = Dribble(stream, bytesAtATime: 37);

        Assert.That(dribbled.Bytes, Is.EqualTo(whole.Bytes));
    }

    /// <summary>Feeds the stream in awkward little reads, none of them a whole packet.</summary>
    private static SegmentCut Dribble(byte[] stream, int bytesAtATime)
    {
        var segmenter = new HlsSegmenter();
        var buffer = new byte[Size * 4];
        var carried = 0;

        for (var at = 0; at < stream.Length; at += bytesAtATime)
        {
            var take = Math.Min(bytesAtATime, stream.Length - at);
            stream.AsSpan(at, take).CopyTo(buffer.AsSpan(carried));

            var cuts = segmenter.Consume(buffer, carried + take);
            carried = cuts.Carried;

            if (cuts.Segments.Count > 0)
            {
                return cuts.Segments[0];
            }
        }

        Assert.Fail("the stream never completed a segment");
        return default;
    }

    [Test]
    public void AStreamThatLostSyncIsPickedUpAgainAtTheNextSyncByte()
    {
        // a hiccup leaves a stray byte in front of an otherwise good packet, and
        // stepping a whole packet on from it would stay out of step for ever
        var good = Stream(Pat(), Pmt(), Keyframe(), Video(), Keyframe());
        var stream = new byte[] { 0x00 }.Concat(good).ToArray();

        var cuts = m_segmenter.Consume(stream, stream.Length);

        Assert.That(cuts.Segments, Has.Count.EqualTo(1));
    }

    [Test]
    public void AReadTooShortToHoldAPacketIsSimplyCarried()
    {
        var buffer = new byte[Size];
        var cuts = m_segmenter.Consume(buffer, Size - 1);

        Assert.Multiple(() =>
        {
            Assert.That(cuts.Segments, Is.Empty);
            Assert.That(cuts.Carried, Is.EqualTo(Size - 1));
        });
    }
}
