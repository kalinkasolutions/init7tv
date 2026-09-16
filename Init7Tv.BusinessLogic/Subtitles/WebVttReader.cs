using System.Globalization;
using System.Text;

namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>
/// Turns the WebVTT ffmpeg writes out as it decodes teletext into cues.
///
/// It arrives a piece at a time down a pipe, so a chunk can stop anywhere: in
/// the middle of a timing line, or between the timing and the words under it. A
/// cue is only handed over once the blank line that ends it has been seen.
/// </summary>
public sealed class WebVttReader
{
    private const string Arrow = "-->";

    private readonly StringBuilder m_pending = new();
    private readonly List<string> m_block = [];

    private TimeSpan? m_start;
    private TimeSpan? m_end;

    /// <summary>Feeds the next piece of the file and returns whatever it completed.</summary>
    public IReadOnlyList<WebVttCue> Read(string chunk)
    {
        var cues = new List<WebVttCue>();
        m_pending.Append(chunk);

        while (true)
        {
            var text = m_pending.ToString();
            var newline = text.IndexOf('\n');
            if (newline < 0)
            {
                break;
            }

            var line = text[..newline].TrimEnd('\r');
            m_pending.Remove(0, newline + 1);

            if (line.Length == 0)
            {
                if (Complete() is { } cue)
                {
                    cues.Add(cue);
                }

                continue;
            }

            if (line.Contains(Arrow, StringComparison.Ordinal))
            {
                // a new timing line without a blank line before it still ends the
                // cue above, which teletext does when one caption replaces another
                if (Complete() is { } cue)
                {
                    cues.Add(cue);
                }

                ReadTiming(line);
                continue;
            }

            if (m_start != null)
            {
                m_block.Add(line);
            }
        }

        return cues;
    }

    /// <summary>The last cue has no blank line after it until more arrives.</summary>
    public WebVttCue? Flush() => Complete();

    private WebVttCue? Complete()
    {
        if (m_start == null || m_end == null || m_block.Count == 0)
        {
            m_block.Clear();
            m_start = m_end = null;
            return null;
        }

        var cue = new WebVttCue
        {
            Start = m_start.Value,
            End = m_end.Value,
            Text = string.Join("\n", m_block)
        };

        m_block.Clear();
        m_start = m_end = null;
        return cue;
    }

    private void ReadTiming(string line)
    {
        var parts = line.Split(Arrow, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseTime(parts[0], out var start))
        {
            return;
        }

        // the end may be followed by cue settings such as "align:start"
        var end = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (!TryParseTime(end, out var finish))
        {
            return;
        }

        m_start = start;
        m_end = finish;
    }

    /// <summary>Reads mm:ss.mmm and hh:mm:ss.mmm, both of which ffmpeg writes.</summary>
    private static bool TryParseTime(string value, out TimeSpan time)
    {
        time = default;

        var parts = value.Split(':');
        if (parts.Length is not (2 or 3))
        {
            return false;
        }

        var hours = 0d;
        if (parts.Length == 3 && !double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out hours))
        {
            return false;
        }

        var minutePart = parts[^2];
        var secondPart = parts[^1];

        if (!double.TryParse(minutePart, NumberStyles.Any, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(secondPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        time = TimeSpan.FromSeconds((hours * 3600) + (minutes * 60) + seconds);
        return true;
    }
}
