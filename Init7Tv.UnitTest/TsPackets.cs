using Init7Tv.BusinessLogic.StreamManager;

namespace Init7Tv.UnitTest;

/// <summary>Transport stream packets built by hand, so a test can describe a stream a byte at a time.</summary>
public static class TsPackets
{
    public const int Size = TsKeyframeDetector.PacketSize;
    public const int PmtPid = 0x1000;
    public const int VideoPid = 0x0100;
    public const int AudioPid = 0x0101;

    public static byte[] Packet(int pid, bool payloadStart, bool adaptationField, bool randomAccess)
    {
        var p = new byte[Size];
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

    public static byte[] Pat()
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

    public static byte[] Pmt()
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

    public static byte[] Keyframe() =>
        Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: true);

    public static byte[] Video() =>
        Packet(VideoPid, payloadStart: true, adaptationField: true, randomAccess: false);

    public static byte[] Audio() =>
        Packet(AudioPid, payloadStart: true, adaptationField: true, randomAccess: false);

    public static int PidOf(byte[] packet) => ((packet[1] & 0x1F) << 8) | packet[2];

    /// <summary>The packets laid end to end, as they would arrive from ffmpeg.</summary>
    public static byte[] Stream(params byte[][] packets) => packets.SelectMany(x => x).ToArray();
}
