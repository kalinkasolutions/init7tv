using System.Collections.Concurrent;
using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;

namespace Init7Tv.BusinessLogic;

public sealed class TvStream
{
    public string StreamId { get; init; }
    public ConcurrentDictionary<string, byte[]> TsSegments { get; set; } = new();
    public Process Ffmpeg { get; set; }
    public List<string> Playlist { get; set; } = [];
    public ulong SegmentIndex { get; set; }
    public CancellationTokenSource CancellationToken { get; set; } = new();
    public int MediaSequenceId { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public FfprobeRoot StreamInfo { get; set; }
}