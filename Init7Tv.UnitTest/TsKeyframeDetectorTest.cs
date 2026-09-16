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
