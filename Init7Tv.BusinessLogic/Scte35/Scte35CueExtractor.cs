using System.Diagnostics.CodeAnalysis;

namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>
/// Pulls cue messages out of a transport stream.
///
/// The cue PIDs are read from the PMT rather than assumed: SCTE 35 section 9.9.1
/// requires them to be listed with stream_type 0x86. These multicasts carry
/// several programmes, so a programme number can be given to follow just one.
///
/// This has to run on the source stream. The ffmpeg transcode rebuilds the PMT
/// around the streams it was asked for and the cue PID is not one of them.
/// </summary>
public sealed class Scte35CueExtractor
{
    public const int PacketSize = 188;
    public const byte SyncByte = 0x47;

    private const int PatPid = 0x0000;
    private const byte Scte35StreamType = 0x86;

    /// <summary>What ffmpeg declares a copied cue stream as, having no codec for it.</summary>
    private const byte PrivateDataStreamType = 0x06;

    private readonly int? m_programNumber;
    private readonly bool m_privateDataMayCarryCues;
    private readonly Dictionary<int, int> m_pmtPids = new();
    private readonly HashSet<int> m_cuePids = [];
    private readonly Dictionary<int, SectionBuffer> m_sections = new();

    /// <param name="programNumber">
    /// Which programme of a multi programme stream to follow. Every programme is
    /// followed when this is null.
    /// </param>
    /// <param name="privateDataMayCarryCues">
    /// Also follow streams declared as private data. A recording has to be read this way: ffmpeg
    /// has no codec for a cue stream, so copying one into the capture rebuilds the PMT around it as
    /// plain private data and the 0x86 it arrived with is gone. A source must not be read this way,
    /// because AC-3 and teletext are declared private data there too.
    ///
    /// Safe on either, in truth: a section is only taken once its table_id and its CRC agree.
    /// </param>
    public Scte35CueExtractor(int? programNumber = null, bool privateDataMayCarryCues = false)
    {
        m_programNumber = programNumber;
        m_privateDataMayCarryCues = privateDataMayCarryCues;
    }

    /// <summary>The cue PIDs seen in a PMT so far.</summary>
    public IReadOnlyCollection<int> CuePids => m_cuePids;

    /// <summary>
    /// Feeds one transport packet. Returns a cue message when this packet
    /// completed one.
    /// </summary>
    public bool TryRead(ReadOnlySpan<byte> packet, [NotNullWhen(true)] out SpliceInfoSection? cue)
    {
        cue = null;

        if (packet.Length != PacketSize || packet[0] != SyncByte)
        {
            return false;
        }

        // transport_error_indicator: the packet is known to be damaged
        if ((packet[1] & 0x80) != 0)
        {
            return false;
        }

        var pid = ((packet[1] & 0x1F) << 8) | packet[2];
        var payloadStart = (packet[1] & 0x40) != 0;
        var payload = Payload(packet);

        if (payload.IsEmpty)
        {
            return false;
        }

        if (pid == PatPid)
        {
            ReadPat(payload, payloadStart);
            return false;
        }

        if (m_pmtPids.ContainsKey(pid))
        {
            ReadPmt(payload, payloadStart, pid);
            return false;
        }

        return m_cuePids.Contains(pid) && TryReadCue(payload, payloadStart, pid, out cue);
    }

    /// <summary>Feeds a run of packets, yielding every cue message in it.</summary>
    public IEnumerable<SpliceInfoSection> Read(byte[] transportStream)
    {
        var cues = new List<SpliceInfoSection>();

        for (var offset = 0; offset + PacketSize <= transportStream.Length; offset += PacketSize)
        {
            if (TryRead(transportStream.AsSpan(offset, PacketSize), out var cue))
            {
                cues.Add(cue);
            }
        }

        return cues;
    }

    private bool TryReadCue(ReadOnlySpan<byte> payload, bool payloadStart, int pid,
        [NotNullWhen(true)] out SpliceInfoSection? cue)
    {
        cue = null;

        if (!m_sections.TryGetValue(pid, out var buffer))
        {
            buffer = new SectionBuffer();
            m_sections[pid] = buffer;
        }

        // A section may span packets. 9.6 requires it to start at the beginning of
        // a payload, so a start here means anything half collected is a lost cause.
        if (payloadStart)
        {
            var section = SectionStart(payload);
            if (section.IsEmpty)
            {
                buffer.Reset();
                return false;
            }

            buffer.Start(section);
        }
        else if (!buffer.Append(payload))
        {
            return false;
        }

        if (!buffer.TryTakeComplete(out var complete))
        {
            return false;
        }

        return SpliceInfoSectionParser.TryParse(complete, out cue!) && cue != null;
    }

    private void ReadPat(ReadOnlySpan<byte> payload, bool payloadStart)
    {
        var section = SectionStart(payload);
        if (!payloadStart || section.Length < 12 || section[0] != 0x00)
        {
            return;
        }

        var end = SectionEnd(section);

        for (var i = 8; i + 4 <= end; i += 4)
        {
            var programNumber = (section[i] << 8) | section[i + 1];
            if (programNumber == 0)
            {
                // the network PID, not a programme
                continue;
            }

            if (m_programNumber == null || m_programNumber == programNumber)
            {
                m_pmtPids[((section[i + 2] & 0x1F) << 8) | section[i + 3]] = programNumber;
            }
        }
    }

    private void ReadPmt(ReadOnlySpan<byte> payload, bool payloadStart, int pid)
    {
        var section = SectionStart(payload);
        if (!payloadStart || section.Length < 16 || section[0] != 0x02)
        {
            return;
        }

        var end = SectionEnd(section);
        var programInfoLength = ((section[10] & 0x0F) << 8) | section[11];
        var offset = 12 + programInfoLength;

        while (offset + 5 <= end)
        {
            var streamType = section[offset];
            var elementaryPid = ((section[offset + 1] & 0x1F) << 8) | section[offset + 2];
            var esInfoLength = ((section[offset + 3] & 0x0F) << 8) | section[offset + 4];

            if (streamType == Scte35StreamType
                || (m_privateDataMayCarryCues && streamType == PrivateDataStreamType))
            {
                m_cuePids.Add(elementaryPid);
            }

            offset += 5 + esInfoLength;
        }

        // keep the mapping so a PMT update is still followed
        m_pmtPids[pid] = m_pmtPids.GetValueOrDefault(pid);
    }

    private static int SectionEnd(ReadOnlySpan<byte> section)
    {
        var sectionLength = ((section[1] & 0x0F) << 8) | section[2];
        return Math.Min(3 + sectionLength - 4, section.Length);
    }

    private static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet)
    {
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

        return offset >= packet.Length ? [] : packet[offset..];
    }

    /// <summary>Skips the pointer_field that says how far into the payload the section starts.</summary>
    private static ReadOnlySpan<byte> SectionStart(ReadOnlySpan<byte> payload)
    {
        var offset = 1 + payload[0];
        return offset >= payload.Length ? [] : payload[offset..];
    }

    private sealed class SectionBuffer
    {
        private readonly List<byte> m_bytes = [];
        private int m_expected;

        public void Reset()
        {
            m_bytes.Clear();
            m_expected = 0;
        }

        public void Start(ReadOnlySpan<byte> section)
        {
            Reset();

            if (section.Length < 3 || section[0] != SpliceInfoSection.TableId)
            {
                return;
            }

            m_expected = 3 + (((section[1] & 0x0F) << 8) | section[2]);
            m_bytes.AddRange(section);
        }

        public bool Append(ReadOnlySpan<byte> payload)
        {
            if (m_expected == 0)
            {
                return false;
            }

            m_bytes.AddRange(payload);
            return true;
        }

        public bool TryTakeComplete(out byte[] section)
        {
            section = [];

            if (m_expected == 0 || m_bytes.Count < m_expected)
            {
                return false;
            }

            section = m_bytes.GetRange(0, m_expected).ToArray();
            Reset();
            return true;
        }
    }
}
