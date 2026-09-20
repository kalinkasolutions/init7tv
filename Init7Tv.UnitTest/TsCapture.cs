using Init7Tv.BusinessLogic.Recording;
using Init7Tv.UnitTest.Scte35;

namespace Init7Tv.UnitTest;

/// <summary>A capture on disk of the shape ffmpeg writes one, built a keyframe at a time.</summary>
public static class TsCapture
{
    public const int CuePid = 0x0105;

    /// <summary>The stream clock, on which a keyframe every four seconds is 4 * Hz apart.</summary>
    public const ulong Hz = 90000;

    /// <summary>
    /// Writes one part: tables, then keyframes four seconds apart, with a cue dropped in after one
    /// of them announcing a break some way further on.
    /// </summary>
    public static void Write(
        string directory,
        int part,
        int keyframes,
        ulong firstPts,
        (int After, byte[] Cue)? cue = null
    )
    {
        var bytes = new List<byte>();

        for (var i = 0; i < keyframes; i++)
        {
            bytes.AddRange(Scte35TestStream.Tables(0x1000, CuePid));
            bytes.AddRange(Keyframe(firstPts + (ulong)i * 4 * Hz));

            if (cue is { } announcement && announcement.After == i)
            {
                bytes.AddRange(Scte35TestStream.Packet(CuePid, true, announcement.Cue));
            }
        }

        File.WriteAllBytes(RecordingFiles.CapturePath(directory, part + 1), bytes.ToArray());
    }

    /// <summary>A video packet that starts a picture and carries the timestamp of it.</summary>
    public static byte[] Keyframe(ulong pts)
    {
        var packet = new byte[188];
        Array.Fill(packet, (byte)0xFF);

        packet[0] = 0x47;
        packet[1] = (byte)(0x40 | ((Scte35TestStream.VideoPid >> 8) & 0x1F));
        packet[2] = (byte)(Scte35TestStream.VideoPid & 0xFF);
        packet[3] = 0x30;                       // adaptation field and payload
        packet[4] = 1;                          // adaptation field length
        packet[5] = 0x40;                       // random access indicator

        var at = 6;
        packet[at] = 0x00; packet[at + 1] = 0x00; packet[at + 2] = 0x01; packet[at + 3] = 0xE0;
        packet[at + 4] = 0x00; packet[at + 5] = 0x00;
        packet[at + 6] = 0x80;
        packet[at + 7] = 0x80;                  // a timestamp follows
        packet[at + 8] = 5;

        var pts33 = pts & ((1UL << 33) - 1);
        packet[at + 9] = (byte)(0x21 | ((pts33 >> 29) & 0x0E));
        packet[at + 10] = (byte)((pts33 >> 22) & 0xFF);
        packet[at + 11] = (byte)(0x01 | ((pts33 >> 14) & 0xFE));
        packet[at + 12] = (byte)((pts33 >> 7) & 0xFF);
        packet[at + 13] = (byte)(0x01 | ((pts33 << 1) & 0xFE));

        return packet;
    }
}
