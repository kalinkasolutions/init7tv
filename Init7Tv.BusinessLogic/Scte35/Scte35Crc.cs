namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>
/// CRC-32/MPEG-2, the one MPEG sections use: polynomial 0x04C11DB7, all ones to
/// start, no reflection and no final inversion. Running it over a section that
/// includes its own CRC gives zero.
/// </summary>
public static class Scte35Crc
{
    private const uint Polynomial = 0x04C11DB7;

    private static readonly uint[] Table = BuildTable();

    public static uint Checksum(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc = (crc << 8) ^ Table[((crc >> 24) ^ b) & 0xFF];
        }

        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var i = 0u; i < table.Length; i++)
        {
            var crc = i << 24;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ Polynomial : crc << 1;
            }

            table[i] = crc;
        }

        return table;
    }
}
