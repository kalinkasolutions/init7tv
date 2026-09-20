using System.Collections.Concurrent;
using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic;

/// <summary>A published segment and how much media it actually holds.</summary>
public readonly record struct TvSegment(string Name, TimeSpan Duration);

public sealed class TvStream
{
    public required string StreamId { get; init; }
    public required Process Ffmpeg { get; set; }

    /// <summary>The segments a player may still ask for, and the bytes behind them.</summary>
    public required SegmentWindow Segments { get; init; }

    public CancellationTokenSource CancellationToken { get; set; } = new();

    /// <summary>User name to the time they last fetched a playlist.</summary>
    public ConcurrentDictionary<string, DateTime> Viewers { get; } = new();

    public required FfprobeRoot StreamInfo { get; set; }
    public required ChannelDto Channel { get; set; }
    public int AudioStreamIndex { get; set; }
    public string GetStreamedLanguage => StreamInfo.GetAudioLanguage(AudioStreamIndex) ?? "unknown";
}