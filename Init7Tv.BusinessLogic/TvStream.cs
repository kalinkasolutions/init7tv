using System.Collections.Concurrent;
using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic;

public sealed class TvStream
{
    public required string StreamId { get; init; }
    public ConcurrentDictionary<string, byte[]> TsSegments { get; set; } = new();
    public required Process Ffmpeg { get; set; }
    public List<string> Playlist { get; set; } = [];
    public Lock PlaylistLock { get; } = new();
    public ulong SegmentIndex { get; set; }
    public CancellationTokenSource CancellationToken { get; set; } = new();
    public int MediaSequenceId { get; set; }

    /// <summary>User name to the time they last fetched a playlist.</summary>
    public ConcurrentDictionary<string, DateTime> Viewers { get; } = new();

    public required FfprobeRoot StreamInfo { get; set; }
    public required ChannelDto Channel { get; set; }
    public int AudioStreamIndex { get; set; }
    public string GetStreamedLanguage => StreamInfo.GetAudioLanguage(AudioStreamIndex) ?? "unknown";
}