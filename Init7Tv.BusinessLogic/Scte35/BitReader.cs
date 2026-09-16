namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>
/// Reads big endian bit fields. SCTE 35 is full of fields that are not whole
/// bytes and do not sit on byte boundaries: 33 bit times, 12 bit lengths, single
/// bit flags followed by 6 reserved ones.
/// </summary>
internal ref struct BitReader
{
    private readonly ReadOnlySpan<byte> m_data;
    private int m_bitPosition;

    public BitReader(ReadOnlySpan<byte> data)
    {
        m_data = data;
        m_bitPosition = 0;
    }

    public int BitsRemaining => m_data.Length * 8 - m_bitPosition;

    public bool BytePosition(out int position)
    {
        position = m_bitPosition / 8;
        return m_bitPosition % 8 == 0;
    }

    /// <summary>Reads up to 64 bits. Returns false rather than throwing when the data runs out.</summary>
    public bool TryReadBits(int count, out ulong value)
    {
        value = 0;

        if (count is < 0 or > 64 || count > BitsRemaining)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var bit = (m_data[m_bitPosition >> 3] >> (7 - (m_bitPosition & 7))) & 1;
            value = (value << 1) | (uint)bit;
            m_bitPosition++;
        }

        return true;
    }

    public bool TryReadFlag(out bool value)
    {
        var read = TryReadBits(1, out var bits);
        value = bits == 1;
        return read;
    }

    public bool TrySkipBits(int count)
    {
        if (count < 0 || count > BitsRemaining)
        {
            return false;
        }

        m_bitPosition += count;
        return true;
    }

    /// <summary>Reads whole bytes. Only valid on a byte boundary, which every caller here is on.</summary>
    public bool TryReadBytes(int count, out ReadOnlySpan<byte> value)
    {
        value = default;

        if (count < 0 || !BytePosition(out var start) || count * 8 > BitsRemaining)
        {
            return false;
        }

        value = m_data.Slice(start, count);
        m_bitPosition += count * 8;
        return true;
    }
}
