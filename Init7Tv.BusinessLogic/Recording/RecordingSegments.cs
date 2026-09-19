using Init7Tv.BusinessLogic.StreamManager;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// A stretch of one capture a player may start on: a keyframe and the program tables in front of it.
/// </summary>
public readonly record struct RecordingSegment(int Part, long Offset, long Length, double Seconds);

/// <summary>
/// Where a capture may be cut so a player can fetch it a piece at a time, without cutting it at all.
///
/// The pieces are byte ranges of the file ffmpeg is already writing, so watching one that is still
/// recording costs no second copy and no second encode: the playlist simply describes what is there.
///
/// A range has to begin with the program tables, not merely a keyframe. A demuxer handed a range on
/// its own cannot tell what the streams are without them, and unlike the live path, which injects a
/// set at the head of every segment, nothing can be inserted into a file somebody else is writing.
/// ffmpeg emits them far more often than it emits keyframes, so a segment starts at the last set
/// before its keyframe instead.
/// </summary>
public sealed class RecordingSegments
{
    private const int PacketSize = TsKeyframeDetector.PacketSize;

    /// <summary>Read in whole packets, and enough of them that scanning is not the slow part.</summary>
    private const int ReadBuffer = PacketSize * 4096;

    private readonly double m_secondsPerSegment;
    private readonly List<RecordingSegment> m_segments = [];
    private readonly Dictionary<int, PartScan> m_parts = new();

    public RecordingSegments(double secondsPerSegment)
    {
        m_secondsPerSegment = secondsPerSegment;
    }

    /// <summary>
    /// Everything that can be played so far. The last cut found is left out while the capture is
    /// still being written: what follows it is only the part of a segment that exists yet.
    /// </summary>
    public IReadOnlyList<RecordingSegment> Segments => m_segments;

    /// <summary>
    /// Reads whatever has been appended since the last time and adds the segments it completes.
    /// Only the new bytes are read, so keeping a playlist current costs the same however long the
    /// recording has been running.
    /// </summary>
    /// <param name="finished">
    /// True once nothing more will be written, when the bytes after the final cut are a whole
    /// segment rather than half of one.
    /// </param>
    public void Extend(IReadOnlyList<string> parts, bool finished)
    {
        for (var part = 0; part < parts.Count; part++)
        {
            var path = parts[part];
            if (!File.Exists(path))
            {
                continue;
            }

            var scan = m_parts.TryGetValue(part, out var existing) ? existing : m_parts[part] = new PartScan();
            var length = new FileInfo(path).Length;

            // whole packets only: the tail of a half written one says nothing yet
            var usable = length / PacketSize * PacketSize;
            if (usable > scan.Scanned)
            {
                ScanPart(path, part, scan, usable);
            }

            var lastPart = part == parts.Count - 1;
            scan.End = usable;

            // a cut is only the start of a segment once something follows it, and what follows the
            // last one is still being written unless this is the end of the recording
            Publish(part, scan, closeLast: !lastPart || finished);
        }
    }

    private void ScanPart(string path, int part, PartScan scan, long usable)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        file.Seek(scan.Scanned, SeekOrigin.Begin);

        var buffer = new byte[ReadBuffer];
        var at = scan.Scanned;

        while (at < usable)
        {
            var want = (int)Math.Min(ReadBuffer, usable - at);
            var read = file.ReadAtLeast(buffer.AsSpan(0, want), want, throwOnEndOfStream: false);
            if (read < PacketSize)
            {
                break;
            }

            for (var i = 0; i + PacketSize <= read; i += PacketSize)
            {
                var packet = buffer.AsSpan(i, PacketSize);
                if (packet[0] != TsKeyframeDetector.SyncByte)
                {
                    continue;
                }

                var pid = ((packet[1] & 0x1F) << 8) | packet[2];
                var payloadStart = (packet[1] & 0x40) != 0;

                // remember where the tables were, because that is where a segment starts
                if (pid == 0 && payloadStart)
                {
                    scan.LastTables = at + i;
                }

                if (scan.Detector.IsKeyframeStart(packet) && scan.LastTables >= 0)
                {
                    if (scan.Cuts.Count == 0 || scan.Cuts[^1] < scan.LastTables)
                    {
                        scan.Cuts.Add(scan.LastTables);
                    }
                }
            }

            at += read / PacketSize * PacketSize;
        }

        scan.Scanned = at;
    }

    /// <summary>Turns the cuts found so far into segments, which is the gap between each pair.</summary>
    private void Publish(int part, PartScan scan, bool closeLast)
    {
        var available = closeLast ? scan.Cuts.Count : scan.Cuts.Count - 1;

        for (var i = scan.Published; i < available; i++)
        {
            var start = scan.Cuts[i];
            var end = i + 1 < scan.Cuts.Count ? scan.Cuts[i + 1] : scan.End;

            if (end > start)
            {
                m_segments.Add(new RecordingSegment(part, start, end - start, m_secondsPerSegment));
            }
        }

        scan.Published = Math.Max(scan.Published, available);
    }

    private sealed class PartScan
    {
        public TsKeyframeDetector Detector { get; } = new();
        public List<long> Cuts { get; } = [];
        public long Scanned { get; set; }
        public long End { get; set; }
        public long LastTables { get; set; } = -1;
        public int Published { get; set; }
    }
}
