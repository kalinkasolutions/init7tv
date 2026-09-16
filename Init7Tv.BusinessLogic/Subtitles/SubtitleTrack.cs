namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>A subtitle track a viewer can pick.</summary>
public sealed record SubtitleTrack
{
    /// <summary>Position among the subtitle streams, ffmpeg's <c>-map 0:s:N</c> numbering.</summary>
    public required int SubtitleStreamIndex { get; init; }

    public required string Language { get; init; }
}
