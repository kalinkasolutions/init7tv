using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// Builds the transport stream around a cue message: the PAT and PMT that say
/// where the cue PID is, and the packets carrying it.
/// </summary>
internal static class Scte35TestStream
{
    public const int PacketSize = Scte35CueExtractor.PacketSize;
    public const int VideoPid = 0x0100;

    public static byte[] Packet(int pid, bool payloadStart, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[PacketSize];
        Array.Fill(packet, (byte)0xFF);

        packet[0] = Scte35CueExtractor.SyncByte;
        packet[1] = (byte)((payloadStart ? 0x40 : 0x00) | ((pid >> 8) & 0x1F));
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = 0x10;

        var offset = 4;
        if (payloadStart)
        {
            packet[offset++] = 0x00;    // pointer_field
        }

        payload[..Math.Min(payload.Length, PacketSize - offset)].CopyTo(packet.AsSpan(offset));
        return packet;
    }

    public static byte[] Pat(params (int ProgramNumber, int PmtPid)[] programs)
    {
        var body = new List<byte>
        {
            0x00,                                   // table_id
            0x00, 0x00,                             // section_length, filled in below
            0x00, 0x01,                             // transport_stream_id
            0xC1, 0x00, 0x00
        };

        foreach (var (number, pid) in programs)
        {
            body.Add((byte)(number >> 8));
            body.Add((byte)(number & 0xFF));
            body.Add((byte)(0xE0 | (pid >> 8)));
            body.Add((byte)(pid & 0xFF));
        }

        return Section(body);
    }

    /// <summary>A PMT with the cue stream listed at stream_type 0x86, as 9.9.1 requires.</summary>
    /// <param name="cueStreamType">
    /// 0x86 as a broadcaster sends it, or 0x06 as ffmpeg declares it after copying one into a
    /// capture.
    /// </param>
    public static byte[] Pmt(
        int cuePid,
        bool includeCueStream = true,
        int programNumber = 1,
        byte cueStreamType = 0x86
    )
    {
        var body = new List<byte>
        {
            0x02,                                   // table_id
            0x00, 0x00,                             // section_length
            (byte)(programNumber >> 8), (byte)(programNumber & 0xFF),
            0xC1, 0x00, 0x00,
            (byte)(0xE0 | (VideoPid >> 8)), (byte)(VideoPid & 0xFF),
            0xF0, 0x04,                             // program_info_length
            0x05, 0x02, 0x43, 0x55                  // a registration descriptor, deliberately not CUEI
        };

        body.AddRange([0x1B, (byte)(0xE0 | (VideoPid >> 8)), (byte)(VideoPid & 0xFF), 0xF0, 0x00]);

        if (includeCueStream)
        {
            body.AddRange([cueStreamType, (byte)(0xE0 | (cuePid >> 8)), (byte)(cuePid & 0xFF), 0xF0, 0x00]);
        }

        return Section(body);
    }

    /// <summary>The PAT and PMT packets for a single programme stream.</summary>
    public static byte[] Tables(int pmtPid, int cuePid) => Concat(
        Packet(0x0000, true, Pat((1, pmtPid))),
        Packet(pmtPid, true, Pmt(cuePid)));

    public static byte[] CuePacket(int cuePid, byte[] section) => Packet(cuePid, true, section);

    /// <summary>Tables followed by one packet per cue message.</summary>
    public static byte[] Build(int pmtPid, int cuePid, params byte[][] sections) =>
        Concat(new[] { Tables(pmtPid, cuePid) }
            .Concat(sections.Select(x => CuePacket(cuePid, x)))
            .ToArray());

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    private static byte[] Section(List<byte> body)
    {
        var sectionLength = body.Count - 3 + 4;
        body[1] = (byte)(0xB0 | ((sectionLength >> 8) & 0x0F));
        body[2] = (byte)(sectionLength & 0xFF);

        var crc = Scte35Crc.Checksum(body.ToArray());
        body.Add((byte)(crc >> 24));
        body.Add((byte)(crc >> 16));
        body.Add((byte)(crc >> 8));
        body.Add((byte)crc);
        return body.ToArray();
    }
}
