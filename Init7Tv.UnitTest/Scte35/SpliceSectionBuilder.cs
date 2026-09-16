using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// Builds splice_info_sections the way an encoder would, so the tests exercise
/// the parser against bytes rather than against a mock of itself. The CRC is
/// computed for real, which is also what proves the section is well formed.
/// </summary>
internal sealed class SpliceSectionBuilder
{
    private readonly List<byte> m_command = [];
    private readonly List<byte> m_descriptors = [];

    private SpliceCommandType m_commandType = SpliceCommandType.SpliceNull;
    private ulong m_ptsAdjustment;
    private bool m_encrypted;

    public static SpliceSectionBuilder SpliceNull() => new();

    public SpliceSectionBuilder WithPtsAdjustment(ulong ticks)
    {
        m_ptsAdjustment = ticks;
        return this;
    }

    public SpliceSectionBuilder Encrypted()
    {
        m_encrypted = true;
        return this;
    }

    /// <summary>splice_insert(), table 10.</summary>
    public static SpliceSectionBuilder SpliceInsert(
        uint eventId,
        bool outOfNetwork = true,
        ulong? ptsTime = 0,
        ulong? durationTicks = null,
        bool autoReturn = true,
        bool cancelled = false)
    {
        var builder = new SpliceSectionBuilder { m_commandType = SpliceCommandType.SpliceInsert };
        var bits = new BitWriter();

        bits.Write(eventId, 32);
        bits.Flag(cancelled);
        bits.Write(0x7F, 7);

        if (!cancelled)
        {
            var immediate = ptsTime == null;
            bits.Flag(outOfNetwork);
            bits.Flag(true);                    // program_splice_flag
            bits.Flag(durationTicks != null);   // duration_flag
            bits.Flag(immediate);
            bits.Flag(false);                   // event_id_compliance_flag
            bits.Write(0x7, 3);

            if (!immediate)
            {
                WriteSpliceTime(bits, ptsTime);
            }

            if (durationTicks != null)
            {
                bits.Flag(autoReturn);
                bits.Write(0x3F, 6);
                bits.Write(durationTicks.Value, 33);
            }

            bits.Write(1, 16);                  // unique_program_id
            bits.Write(0, 8);                   // avail_num
            bits.Write(0, 8);                   // avails_expected
        }

        builder.m_command.AddRange(bits.ToArray());
        return builder;
    }

    /// <summary>time_signal(), table 11: a splice_time and nothing else.</summary>
    public static SpliceSectionBuilder TimeSignal(ulong? ptsTime)
    {
        var builder = new SpliceSectionBuilder { m_commandType = SpliceCommandType.TimeSignal };
        var bits = new BitWriter();
        WriteSpliceTime(bits, ptsTime);
        builder.m_command.AddRange(bits.ToArray());
        return builder;
    }

    /// <summary>Any other splice_descriptor, table 17: a tag, a length and a body.</summary>
    public SpliceSectionBuilder WithDescriptor(byte tag, byte[] body)
    {
        m_descriptors.Add(tag);
        m_descriptors.Add((byte)body.Length);
        m_descriptors.AddRange(body);
        return this;
    }

    /// <summary>segmentation_descriptor(), table 20.</summary>
    public SpliceSectionBuilder WithSegmentation(
        uint eventId,
        SegmentationType type,
        ulong? durationTicks = null,
        byte[]? upid = null,
        bool cancelled = false,
        bool webDeliveryAllowed = true,
        bool deliveryNotRestricted = true)
    {
        upid ??= [];

        var bits = new BitWriter();
        bits.Write(0x43554549, 32);             // "CUEI"
        bits.Write(eventId, 32);
        bits.Flag(cancelled);
        bits.Flag(false);                       // event_id_compliance_indicator
        bits.Write(0x3F, 6);

        if (!cancelled)
        {
            bits.Flag(true);                    // program_segmentation_flag
            bits.Flag(durationTicks != null);
            bits.Flag(deliveryNotRestricted);

            if (deliveryNotRestricted)
            {
                bits.Write(0x1F, 5);
            }
            else
            {
                bits.Flag(webDeliveryAllowed);
                bits.Flag(true);                // no_regional_blackout_flag
                bits.Flag(true);                // archive_allowed_flag
                bits.Write(0x3, 2);             // device_restrictions
            }

            if (durationTicks != null)
            {
                bits.Write(durationTicks.Value, 40);
            }

            bits.Write(0x0C, 8);                // segmentation_upid_type: MPU
            bits.Write((ulong)upid.Length, 8);
            foreach (var b in upid)
            {
                bits.Write(b, 8);
            }

            bits.Write((byte)type, 8);
            bits.Write(1, 8);                   // segment_num
            bits.Write(1, 8);                   // segments_expected
        }

        var body = bits.ToArray();
        m_descriptors.Add(0x02);                // splice_descriptor_tag
        m_descriptors.Add((byte)body.Length);
        m_descriptors.AddRange(body);
        return this;
    }

    public byte[] Build()
    {
        var bits = new BitWriter();
        bits.Flag(m_encrypted);
        bits.Write(0, 6);                       // encryption_algorithm
        bits.Write(m_ptsAdjustment, 33);
        bits.Write(0, 8);                       // cw_index
        bits.Write(0xFFF, 12);                  // tier
        bits.Write((ulong)m_command.Count, 12);
        bits.Write((byte)m_commandType, 8);

        var body = new List<byte> { 0x00 };     // protocol_version
        body.AddRange(bits.ToArray());
        body.AddRange(m_command);
        body.Add((byte)(m_descriptors.Count >> 8));
        body.Add((byte)(m_descriptors.Count & 0xFF));
        body.AddRange(m_descriptors);

        // section_length counts everything after it, including the CRC
        var sectionLength = body.Count + 4;

        var section = new List<byte>
        {
            SpliceInfoSection.TableId,
            (byte)(0x30 | ((sectionLength >> 8) & 0x0F)),   // syntax 0, private 0, sap_type 3
            (byte)(sectionLength & 0xFF)
        };
        section.AddRange(body);

        var crc = Scte35Crc.Checksum(section.ToArray());
        section.Add((byte)(crc >> 24));
        section.Add((byte)(crc >> 16));
        section.Add((byte)(crc >> 8));
        section.Add((byte)crc);

        return section.ToArray();
    }

    private static void WriteSpliceTime(BitWriter bits, ulong? ptsTime)
    {
        if (ptsTime == null)
        {
            bits.Flag(false);
            bits.Write(0x7F, 7);
            return;
        }

        bits.Flag(true);
        bits.Write(0x3F, 6);
        bits.Write(ptsTime.Value, 33);
    }

    private sealed class BitWriter
    {
        private readonly List<byte> m_bytes = [];
        private int m_bitCount;

        public void Write(ulong value, int bits)
        {
            for (var i = bits - 1; i >= 0; i--)
            {
                var bit = (value >> i) & 1;

                if (m_bitCount % 8 == 0)
                {
                    m_bytes.Add(0);
                }

                m_bytes[^1] |= (byte)(bit << (7 - (m_bitCount % 8)));
                m_bitCount++;
            }
        }

        public void Flag(bool value) => Write(value ? 1UL : 0UL, 1);

        public byte[] ToArray()
        {
            if (m_bitCount % 8 != 0)
            {
                throw new InvalidOperationException($"{m_bitCount} bits is not a whole number of bytes");
            }

            return m_bytes.ToArray();
        }
    }
}
