namespace Init7Tv.BusinessLogic.StreamManager;

/// <summary>One segment cut from the stream, and how many keyframes it holds.</summary>
public readonly record struct SegmentCut(byte[] Bytes, int Keyframes);

/// <summary>What one read produced: the segments it completed, and the bytes left for the next.</summary>
public readonly record struct SegmentCuts(IReadOnlyList<SegmentCut> Segments, int Carried);

/// <summary>
/// Cuts ffmpeg's transport stream into the pieces a player may start on: one segment per keyframe,
/// with the program tables in front of it.
///
/// Reads arrive in whatever sizes the pipe hands over, so a packet is routinely split across two of
/// them. The tail of a part-read packet is moved to the front of the buffer and counted back to the
/// caller, which is what lets the next read carry straight on from it.
/// </summary>
public sealed class HlsSegmenter
{
    private readonly TsKeyframeDetector m_detector = new();

    private MemoryStream m_segment = new();
    private int m_keyframes;

    /// <summary>
    /// Copies whole packets out of the first <paramref name="available"/> bytes of
    /// <paramref name="buffer"/>, starting a new segment on each keyframe. The leftover bytes are
    /// moved to the front of <paramref name="buffer"/>, so the caller reads the next chunk in
    /// behind them.
    /// </summary>
    public SegmentCuts Consume(byte[] buffer, int available)
    {
        List<SegmentCut>? cut = null;
        var consumed = 0;

        while (available - consumed >= TsKeyframeDetector.PacketSize)
        {
            var packet = buffer.AsSpan(consumed, TsKeyframeDetector.PacketSize);
            consumed += TsKeyframeDetector.PacketSize;

            if (packet[0] != TsKeyframeDetector.SyncByte)
            {
                // ffmpeg writes whole packets, so this only happens after a hiccup
                consumed -= TsKeyframeDetector.PacketSize - 1;
                continue;
            }

            if (m_detector.IsKeyframeStart(packet))
            {
                // scenecut is off and -g cannot fire first, so every keyframe here is
                // a forced one exactly one segment of media after the last
                if (m_keyframes > 0)
                {
                    (cut ??= []).Add(Close());
                }

                // the program tables have to lead the segment. ffmpeg emits them
                // periodically, so cutting at a keyframe left them a third of a
                // second in, and a player that demuxes each segment on its own
                // discards everything before them.
                foreach (var table in m_detector.ProgramTables)
                {
                    m_segment.Write(table);
                }

                m_keyframes++;
            }

            // anything before the first keyframe cannot be decoded on its own
            if (m_keyframes > 0)
            {
                m_segment.Write(packet);
            }
        }

        var remaining = available - consumed;
        buffer.AsSpan(consumed, remaining).CopyTo(buffer);

        return new SegmentCuts(cut ?? [], remaining);
    }

    private SegmentCut Close()
    {
        var closed = new SegmentCut(m_segment.ToArray(), m_keyframes);

        m_segment = new MemoryStream();
        m_keyframes = 0;

        return closed;
    }
}
