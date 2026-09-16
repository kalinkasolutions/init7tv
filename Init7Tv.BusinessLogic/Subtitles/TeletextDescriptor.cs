namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>One component of a teletext service: what it is, and on which page.</summary>
public sealed record TeletextComponent
{
    public required string Language { get; init; }

    /// <summary>Page it is transmitted on, magazine and page together, as viewers know it.</summary>
    public required int Page { get; init; }

    public required bool IsSubtitle { get; init; }

    /// <summary>Written for viewers who cannot hear, which is a different page from the plain one.</summary>
    public required bool HearingImpaired { get; init; }
}

/// <summary>
/// Reads the teletext descriptor, tag 0x56 of the PMT, ETSI EN 300 468 6.2.43.
///
/// It is the only place the page numbers are written down. ffprobe reports the
/// languages of a teletext stream joined together, "deu,deu,fra" on arte, and
/// says nothing about which page each one is on, so asking ffmpeg for every
/// subtitle page at once returns two languages interleaved in one track.
/// </summary>
public static class TeletextDescriptor
{
    public const byte Tag = 0x56;

    private const int ComponentLength = 5;
    private const int SubtitleType = 0x02;
    private const int HearingImpairedSubtitleType = 0x05;

    /// <summary>
    /// The same thing as ffprobe reports it: the languages joined with commas in
    /// the tag, and the rest of each component as extradata, which is the
    /// descriptor with the language codes stripped out.
    /// </summary>
    public static IReadOnlyList<TeletextComponent> FromProbe(string? languages, string? extraData)
    {
        var bytes = ReadHexDump(extraData);
        var langs = (languages ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var components = new List<TeletextComponent>();

        for (var i = 0; i * 2 + 1 < bytes.Count && i < langs.Length; i++)
        {
            var type = bytes[i * 2] >> 3;
            var magazine = bytes[i * 2] & 0x07;

            components.Add(new TeletextComponent
            {
                Language = langs[i].Trim(),
                Page = PageNumber(magazine, bytes[(i * 2) + 1]),
                IsSubtitle = type is SubtitleType or HearingImpairedSubtitleType,
                HearingImpaired = type == HearingImpairedSubtitleType
            });
        }

        return components;
    }

    /// <summary>ffprobe writes bytes as "00000000: 0900 1150 2888   ...P(." per line.</summary>
    private static List<byte> ReadHexDump(string? dump)
    {
        var bytes = new List<byte>();

        foreach (var line in (dump ?? string.Empty).Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            // the printable column follows a run of spaces, and would otherwise be
            // read as more bytes whenever it happens to look like hex
            var body = line[(colon + 1)..];
            var gap = body.IndexOf("  ", StringComparison.Ordinal);
            if (gap >= 0)
            {
                body = body[..gap];
            }

            var digits = body.Where(Uri.IsHexDigit).ToArray();
            for (var i = 0; i + 1 < digits.Length; i += 2)
            {
                bytes.Add(Convert.ToByte(new string([digits[i], digits[i + 1]]), 16));
            }
        }

        return bytes;
    }

    public static IReadOnlyList<TeletextComponent> Parse(ReadOnlySpan<byte> descriptor)
    {
        var components = new List<TeletextComponent>();

        for (var offset = 0; offset + ComponentLength <= descriptor.Length; offset += ComponentLength)
        {
            var language = System.Text.Encoding.ASCII.GetString(descriptor.Slice(offset, 3)).Trim();
            var type = descriptor[offset + 3] >> 3;
            var magazine = descriptor[offset + 3] & 0x07;
            var page = descriptor[offset + 4];

            components.Add(new TeletextComponent
            {
                Language = language,
                Page = PageNumber(magazine, page),
                IsSubtitle = type is SubtitleType or HearingImpairedSubtitleType,
                HearingImpaired = type == HearingImpairedSubtitleType
            });
        }

        return components;
    }

    /// <summary>
    /// Magazine and page as one number. The page is two BCD digits, and magazine
    /// zero is magazine eight, which is why 0x77 in magazine 7 is page 777.
    /// </summary>
    private static int PageNumber(int magazine, byte page) =>
        ((magazine == 0 ? 8 : magazine) * 100) + (((page >> 4) & 0x0F) * 10) + (page & 0x0F);
}
