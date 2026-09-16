using Init7Tv.BusinessLogic.StreamManager;

namespace Init7Tv.UnitTest;

public class TsKeyframeDetectorTest
{
    private const int PacketSize = TsKeyframeDetector.PacketSize;
    private const int PmtPid = 0x1000;
    private const int VideoPid = 0x0100;
    private const int AudioPid = 0x0101;

    private static byte[] Packet(int pid, bool payloadStart, bool adaptationField, bool randomAccess)
    {
        var p = new byte[PacketSize];
        Array.Fill(p, (byte)0xFF);

        p[0] = TsKeyframeDetector.SyncByte;
        p[1] = (byte)((payloadStart ? 0x40 : 0x00) | ((pid >> 8) & 0x1F));
        p[2] = (byte)(pid & 0xFF);
        p[3] = (byte)(adaptationField ? 0x30 : 0x10);

        if (adaptationField)
        {
            p[4] = 1;
            p[5] = (byte)(randomAccess ? 0x40 : 0x00);
        }

        return p;
    }

    /// <summary>A video packet whose payload is a PES header carrying a PTS.</summary>
    private static byte[] VideoWithPts(ulong pts, bool randomAccess = true)
    {
        var p = Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: randomAccess);
        var i = 4 + 1 + p[4];               // past the adaptation field

        p[i++] = 0x00; p[i++] = 0x00; p[i++] = 0x01;
        p[i++] = 0xE0;                      // stream_id, video
        p[i++] = 0x00; p[i++] = 0x00;       // PES packet length
        p[i++] = 0x80;
        p[i++] = 0x80;                      // PTS present
        p[i++] = 0x05;                      // PES header data length

        p[i++] = (byte)(0x21 | (((pts >> 30) & 0x07) << 1));
        p[i++] = (byte)((pts >> 22) & 0xFF);
        p[i++] = (byte)(0x01 | (((pts >> 15) & 0x7F) << 1));
        p[i++] = (byte)((pts >> 7) & 0xFF);
        p[i] = (byte)(0x01 | ((pts & 0x7F) << 1));
        return p;
    }

    private static byte[] Pat()
    {
        var p = Packet(0x0000, payloadStart: true, adaptationField: false, randomAccess: false);
        var i = 4;
        p[i++] = 0x00;                              // pointer_field
        p[i++] = 0x00;                              // table_id: PAT
        p[i++] = 0xB0;
        p[i++] = 0x0D;                              // section_length 13
        p[i++] = 0x00; p[i++] = 0x01;               // transport_stream_id
        p[i++] = 0xC1; p[i++] = 0x00; p[i++] = 0x00;
        p[i++] = 0x00; p[i++] = 0x01;               // program_number 1
        p[i++] = (byte)(0xE0 | (PmtPid >> 8));
        p[i] = PmtPid & 0xFF;
        return p;
    }

    private static byte[] Pmt()
    {
        var p = Packet(PmtPid, payloadStart: true, adaptationField: false, randomAccess: false);
        var i = 4;
        p[i++] = 0x00;                              // pointer_field
        p[i++] = 0x02;                              // table_id: PMT
        p[i++] = 0xB0;
        p[i++] = 0x17;                              // section_length
        p[i++] = 0x00; p[i++] = 0x01;
        p[i++] = 0xC1; p[i++] = 0x00; p[i++] = 0x00;
        p[i++] = (byte)(0xE0 | (VideoPid >> 8));
        p[i++] = VideoPid & 0xFF;                   // PCR pid
        p[i++] = 0xF0; p[i++] = 0x00;               // program_info_length 0

        p[i++] = 0x0F;                              // AAC audio, listed first on purpose
        p[i++] = (byte)(0xE0 | (AudioPid >> 8));
        p[i++] = AudioPid & 0xFF;
        p[i++] = 0xF0; p[i++] = 0x00;

        p[i++] = 0x1B;                              // H.264 video
        p[i++] = (byte)(0xE0 | (VideoPid >> 8));
        p[i++] = VideoPid & 0xFF;
        p[i++] = 0xF0; p[i] = 0x00;
        return p;
    }

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
        var keyframe = Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: true);

        Assert.That(detector.IsKeyframeStart(keyframe), Is.True);
    }

    [Test]
    public void VideoPacketWithoutRandomAccess_IsNot()
    {
        var detector = Primed();
        var frame = Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: false);

        Assert.That(detector.IsKeyframeStart(frame), Is.False);
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
        var keyframe = Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: true);

        Assert.That(detector.IsKeyframeStart(keyframe), Is.False);
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
            Assert.That(detector.ProgramTables[0], Has.Length.EqualTo(TsKeyframeDetector.PacketSize));
            Assert.That(detector.ProgramTables[1], Has.Length.EqualTo(TsKeyframeDetector.PacketSize));
            Assert.That(Pid(detector.ProgramTables[0]), Is.EqualTo(0x0000), "the PAT comes first");
            Assert.That(Pid(detector.ProgramTables[1]), Is.EqualTo(PmtPid));
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

    [TestCase(0UL)]
    [TestCase(90_000UL)]
    [TestCase(8_589_934_591UL)]        // the largest a 33 bit clock holds
    [TestCase(2_476_907_793UL)]        // a real splice point off the air
    public void TheKeyframesPresentationTimeIsRead(ulong pts)
    {
        // subtitles are timed on this same clock, and a segment has to say where
        // it sits for a caption to land on the right pictures
        var detector = Primed();

        Assert.That(detector.IsKeyframeStart(VideoWithPts(pts)), Is.True);
        Assert.That(detector.LastKeyframePts, Is.EqualTo(pts));
    }

    [Test]
    public void BeforeAnyKeyframeThereIsNoPresentationTime()
    {
        Assert.That(new TsKeyframeDetector().LastKeyframePts, Is.Null);
    }

    [Test]
    public void AKeyframeWithoutAPtsLeavesTheLastOneAlone()
    {
        // not every packet carries a PES header, and a missing one is not a reason
        // to place the next segment at zero
        var detector = Primed();
        detector.IsKeyframeStart(VideoWithPts(12345));

        detector.IsKeyframeStart(Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: true));

        Assert.That(detector.LastKeyframePts, Is.EqualTo(12345));
    }

    [Test]
    public void APacketThatIsNotAKeyframeDoesNotMoveThePresentationTime()
    {
        var detector = Primed();
        detector.IsKeyframeStart(VideoWithPts(1000));

        detector.IsKeyframeStart(VideoWithPts(9999, randomAccess: false));

        Assert.That(detector.LastKeyframePts, Is.EqualTo(1000));
    }

    private static int Pid(byte[] packet) => ((packet[1] & 0x1F) << 8) | packet[2];

    [Test]
    public void APacketWithoutASyncByte_IsNot()
    {
        var detector = Primed();
        var broken = Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: true);
        broken[0] = 0x00;

        Assert.That(detector.IsKeyframeStart(broken), Is.False);
    }

    [Test]
    public void ATransportErrorPacket_IsNot()
    {
        var detector = Primed();
        var errored = Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: true);
        errored[1] |= 0x80;

        Assert.That(detector.IsKeyframeStart(errored), Is.False);
    }
}
