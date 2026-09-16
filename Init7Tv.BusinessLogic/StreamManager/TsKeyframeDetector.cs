namespace Init7Tv.BusinessLogic.StreamManager;

/// <summary>
/// Finds the transport stream packets an HLS segment may start on.
///
/// A segment has to begin with a keyframe or a player cannot decode it on its
/// own, which matters whenever a viewer joins or the buffer is disturbed. The
/// video PID is read from the PAT and PMT rather than assumed, and keyframes
/// are recognised by the random access indicator ffmpeg sets on them.
/// </summary>
public sealed class TsKeyframeDetector
{
    public const int PacketSize = 188;
    public const byte SyncByte = 0x47;

    private const int PatPid = 0x0000;
    private const byte H264StreamType = 0x1B;

    private int m_pmtPid = -1;
    private int m_videoPid = -1;

    public bool IsKeyframeStart(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != PacketSize || packet[0] != SyncByte)
        {
            return false;
        }

        // transport_error_indicator
        if ((packet[1] & 0x80) != 0)
        {
            return false;
        }

        var pid = ((packet[1] & 0x1F) << 8) | packet[2];
        var payloadStart = (packet[1] & 0x40) != 0;

        if (pid == PatPid)
        {
            ReadPat(packet, payloadStart);
            return false;
        }

        if (pid == m_pmtPid)
        {
            ReadPmt(packet, payloadStart);
            return false;
        }

        return pid == m_videoPid && payloadStart && HasRandomAccessIndicator(packet);
    }

    private static bool HasRandomAccessIndicator(ReadOnlySpan<byte> packet)
    {
        var adaptationFieldControl = (packet[3] >> 4) & 0x3;
        if (adaptationFieldControl is not (2 or 3))
        {
            return false;
        }

        var adaptationFieldLength = packet[4];
        return adaptationFieldLength > 0 && (packet[5] & 0x40) != 0;
    }

    private void ReadPat(ReadOnlySpan<byte> packet, bool payloadStart)
    {
        var section = GetSection(packet, payloadStart);

        // table_id 0x00, then 8 bytes of header before the program entries
        if (section.Length < 12 || section[0] != 0x00)
        {
            return;
        }

        var sectionLength = ((section[1] & 0x0F) << 8) | section[2];
        var end = Math.Min(3 + sectionLength - 4, section.Length);

        for (var i = 8; i + 4 <= end; i += 4)
        {
            var programNumber = (section[i] << 8) | section[i + 1];
            if (programNumber == 0)
            {
                // network PID, not a program
                continue;
            }

            m_pmtPid = ((section[i + 2] & 0x1F) << 8) | section[i + 3];
            return;
        }
    }

    private void ReadPmt(ReadOnlySpan<byte> packet, bool payloadStart)
    {
        var section = GetSection(packet, payloadStart);

        if (section.Length < 16 || section[0] != 0x02)
        {
            return;
        }

        var sectionLength = ((section[1] & 0x0F) << 8) | section[2];
        var end = Math.Min(3 + sectionLength - 4, section.Length);

        var programInfoLength = ((section[10] & 0x0F) << 8) | section[11];
        var offset = 12 + programInfoLength;

        while (offset + 5 <= end)
        {
            var streamType = section[offset];
            var elementaryPid = ((section[offset + 1] & 0x1F) << 8) | section[offset + 2];
            var esInfoLength = ((section[offset + 3] & 0x0F) << 8) | section[offset + 4];

            if (streamType == H264StreamType)
            {
                m_videoPid = elementaryPid;
                return;
            }

            offset += 5 + esInfoLength;
        }
    }

    /// <summary>Payload of a PSI packet, past the adaptation field and pointer_field.</summary>
    private static ReadOnlySpan<byte> GetSection(ReadOnlySpan<byte> packet, bool payloadStart)
    {
        if (!payloadStart)
        {
            return [];
        }

        var adaptationFieldControl = (packet[3] >> 4) & 0x3;
        if (adaptationFieldControl is not (1 or 3))
        {
            return [];
        }

        var offset = 4;
        if (adaptationFieldControl == 3)
        {
            offset += 1 + packet[4];
        }

        if (offset >= packet.Length)
        {
            return [];
        }

        // pointer_field says how far the section start is from here
        offset += 1 + packet[offset];
        return offset >= packet.Length ? [] : packet[offset..];
    }
}
