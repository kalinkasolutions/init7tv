using System.Collections.Concurrent;
using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Subtitles;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic;

/// <summary>
/// A published segment, how much media it holds, and where it sits on the 90 kHz
/// clock of the stream. The last is what a caption is matched against.
/// </summary>
public readonly record struct TvSegment(string Name, TimeSpan Duration, ulong StartPts);

public sealed class TvStream
{
    public required string StreamId { get; init; }
    public ConcurrentDictionary<string, byte[]> TsSegments { get; set; } = new();
    public required Process Ffmpeg { get; set; }
    public List<TvSegment> Playlist { get; } = [];
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

    /// <summary>The subtitle track being extracted, if the channel carries one.</summary>
    public SubtitleTrack? Subtitle { get; set; }

    /// <summary>The named pipe ffmpeg writes its WebVTT into.</summary>
    public string? SubtitlePipe { get; set; }

    /// <summary>Captions seen so far, trimmed with the playlist they belong to.</summary>
    public List<WebVttCue> Cues { get; } = [];

    public Lock CuesLock { get; } = new();
}