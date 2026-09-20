using Init7Tv.BusinessLogic.StreamManager;
using static Init7Tv.UnitTest.TsPackets;

namespace Init7Tv.UnitTest;

public class TsKeyframeDetectorTest
{
    private static TsKeyframeDetector Primed()
    {
        var detector = new TsKeyframeDetector();
        detector.IsKeyframeStart(Pat());
        detector.IsKeyframeStart(Pmt());
        return detector;
    }

    [Test]
    public void VideoKeyframe_IsASegmentBoundary()
    {
        var detector = Primed();

        Assert.That(detector.IsKeyframeStart(Keyframe()), Is.True);
    }

    [Test]
    public void VideoPacketWithoutRandomAccess_IsNot()
    {
        var detector = Primed();

        Assert.That(detector.IsKeyframeStart(Video()), Is.False);
    }

    [Test]
    public void VideoPacketThatIsNotAPayloadStart_IsNot()
    {
        var detector = Primed();
        var continuation = Packet(VideoPid, payloadStart: false, adaptationField: true, randomAccess: true);

        Assert.That(detector.IsKeyframeStart(continuation), Is.False);
    }

    [Test]
    public void AudioRandomAccessPoint_IsNot()
    {
        // every audio frame is a random access point, cutting on those would
        // produce segments that start without a keyframe
        var detector = Primed();
        var audio = Packet(AudioPid, payloadStart: true, adaptationField: true, randomAccess: true);

        Assert.That(detector.IsKeyframeStart(audio), Is.False);
    }

    [Test]
    public void BeforeThePmtIsSeen_NothingIsABoundary()
    {
        var detector = new TsKeyframeDetector();

        Assert.That(detector.IsKeyframeStart(Keyframe()), Is.False);
    }

    [Test]
    public void ProgramTables_AreRememberedSoASegmentCanLeadWithThem()
    {
        var detector = new TsKeyframeDetector();
        Assert.That(detector.ProgramTables, Is.Empty, "nothing to offer before a PAT has been seen");

        detector.IsKeyframeStart(Pat());
        Assert.That(detector.ProgramTables, Is.Empty, "a PAT alone does not describe the streams");

        detector.IsKeyframeStart(Pmt());

        // a player that demuxes each segment on its own needs both, ahead of any
        // media, or it discards everything until the next set arrives
        Assert.That(detector.ProgramTables, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(detector.ProgramTables[0][0], Is.EqualTo(TsKeyframeDetector.SyncByte));
            Assert.That(detector.ProgramTables[0], Has.Length.EqualTo(Size));
            Assert.That(detector.ProgramTables[1], Has.Length.EqualTo(Size));
            Assert.That(PidOf(detector.ProgramTables[0]), Is.EqualTo(0x0000), "the PAT comes first");
            Assert.That(PidOf(detector.ProgramTables[1]), Is.EqualTo(PmtPid));
        });
    }

    [Test]
    public void ProgramTables_FollowTheLatestVersion()
    {
        var detector = Primed();
        var before = detector.ProgramTables[0];

        var updated = Pat();
        updated[7] = 0x42;                       // a different table body
        detector.IsKeyframeStart(updated);

        Assert.That(detector.ProgramTables[0], Is.Not.EqualTo(before));
    }

    [Test]
    public void APacketWithoutASyncByte_IsNot()
    {
        var detector = Primed();
        var broken = Keyframe();
        broken[0] = 0x00;

        Assert.That(detector.IsKeyframeStart(broken), Is.False);
    }

    [Test]
    public void ATransportErrorPacket_IsNot()
    {
        var detector = Primed();
        var errored = Keyframe();
        errored[1] |= 0x80;

        Assert.That(detector.IsKeyframeStart(errored), Is.False);
    }
}
