namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>A subtitle track a viewer can pick.</summary>
public sealed record SubtitleTrack
{
    /// <summary>Position among the subtitle streams, ffmpeg's <c>-map 0:s:N</c> numbering.</summary>
    public required int SubtitleStreamIndex { get; init; }

    public required string Language { get; init; }

    /// <summary>
    /// Teletext page the captions are on. One stream carries several: arte D has
    /// German on 150 and French on 888, so the page is what separates them.
    /// </summary>
    public required int Page { get; init; }

    /// <summary>Written for viewers who cannot hear, and worth saying so.</summary>
    public bool HearingImpaired { get; init; }

    /// <summary>What a viewer sees in the list.</summary>
    public string Label => HearingImpaired ? $"{Language} (hard of hearing)" : Language;
}
