using Init7Tv.BusinessLogic.Scte35;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.Dto;

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

    /// <summary>What a segment is worth when its real length cannot be worked out.</summary>
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
    /// Where the advertising falls, in seconds from the start of the recording.
    ///
    /// The cue messages are carried in the capture itself rather than read from the source
    /// separately, so a cue and the pictures it refers to are already on the same clock: what is
    /// left is the offset between that clock and the start of each part, which restarts whenever
    /// the capture had to.
    /// </summary>
    public IReadOnlyList<AdBreakMark> Breaks
    {
        get
        {
            var marks = new List<AdBreakMark>();

            foreach (var (part, scan) in m_parts.OrderBy(x => x.Key))
            {
                if (scan.FirstPts == null)
                {
                    continue;
                }


                foreach (var found in scan.Timeline.Breaks)
                {
                    // where the announcement went past, not the splice time it carries: that one is
                    // still on the clock of the source the capture was made from
                    var starts = scan.StartsAt + Seconds(found.ArrivalPts, scan.FirstPts.Value);
                    var length = found.Duration ?? AdBreakTimeline.UnknownBreakLength;

                    if (starts >= 0)
                    {
                        marks.Add(new AdBreakMark { StartsAt = starts, EndsAt = starts + length.TotalSeconds });
                    }
                }
            }

            return marks.OrderBy(x => x.StartsAt).ToArray();
        }
    }

    /// <summary>
    /// Distance between two timestamps on the stream's 90 kHz clock, which is 33 bits wide and so
    /// starts again roughly every twenty six hours.
    /// </summary>
    private static double Seconds(ulong pts, ulong from)
    {
        const ulong wrap = 1UL << 33;

        return (double)((pts + wrap - from) % wrap) / 90000.0;
    }

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

            var scan = m_parts.TryGetValue(part, out var existing) ? existing : m_parts[part] = new PartScan
            {
                StartsAt = m_segments.Sum(segment => segment.Seconds)
            };
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

                // the cue messages the channel carries, now inside the recording rather than only
                // in the source it came from
                if (scan.Cues.TryRead(packet, out var cue))
                {
                    scan.Timeline.Observe(cue, scan.LastPts);
                }

                if (payloadStart && ReadPts(packet) is { } pts)
                {
                    scan.LastPts = pts;
                    scan.FirstPts ??= pts;
                }

                if (scan.Detector.IsKeyframeStart(packet) && scan.LastTables >= 0)
                {
                    // the timestamp of the picture this segment opens with, which is what makes its
                    // length the length it really is rather than the one it was meant to be
                    if (scan.Cuts.Count == 0 || scan.Cuts[^1].Offset < scan.LastTables)
                    {
                        scan.Cuts.Add((scan.LastTables, scan.LastPts));
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
            var cut = scan.Cuts[i];
            var next = i + 1 < scan.Cuts.Count ? scan.Cuts[i + 1] : ((long?)null, (ulong?)null);
            var end = next.Item1 ?? scan.End;

            if (end <= cut.Offset)
            {
                continue;
            }

            // Declared lengths are what a player lays its timeline out from, so a nominal four
            // seconds against segments that are not quite four leaves the two drifting apart: about
            // two per cent, which is a minute and a half across a recording of a film.
            var seconds = next.Item2 is { } until
                ? Seconds(until, cut.Pts)
                : m_secondsPerSegment;

            m_segments.Add(new RecordingSegment(part, cut.Offset, end - cut.Offset, seconds));
        }

        scan.Published = Math.Max(scan.Published, available);
    }

    /// <summary>
    /// The presentation timestamp of a packet that begins one, which is what puts a cue message and
    /// the pictures it refers to on the same clock.
    /// </summary>
    private static ulong? ReadPts(ReadOnlySpan<byte> packet)
    {
        var adaptation = (packet[3] >> 4) & 0x3;
        var offset = 4 + (adaptation is 2 or 3 ? packet[4] + 1 : 0);

        // a PES packet starts 00 00 01, and only some of them carry a timestamp
        if (offset + 14 > packet.Length || packet[offset] != 0 || packet[offset + 1] != 0 || packet[offset + 2] != 1)
        {
            return null;
        }

        if ((packet[offset + 7] & 0x80) == 0)
        {
            return null;
        }

        var at = offset + 9;

        return ((ulong)(packet[at] & 0x0E) << 29)
               | ((ulong)packet[at + 1] << 22)
               | ((ulong)(packet[at + 2] & 0xFE) << 14)
               | ((ulong)packet[at + 3] << 7)
               | ((ulong)packet[at + 4] >> 1);
    }

    private sealed class PartScan
    {
        public TsKeyframeDetector Detector { get; } = new();
        // a capture, not a source: the cue stream arrives declared as private data
        public Scte35CueExtractor Cues { get; } = new(privateDataMayCarryCues: true);
        public AdBreakTimeline Timeline { get; } = new();

        /// <summary>Where this part begins in the recording as a whole.</summary>
        public double StartsAt { get; set; }

        public ulong? FirstPts { get; set; }
        public ulong LastPts { get; set; }
        public List<(long Offset, ulong Pts)> Cuts { get; } = [];
        public long Scanned { get; set; }
        public long End { get; set; }
        public long LastTables { get; set; } = -1;
        public int Published { get; set; }
    }
}
