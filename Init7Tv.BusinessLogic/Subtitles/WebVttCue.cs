namespace Init7Tv.BusinessLogic.Subtitles;

/// <summary>One line of subtitle and the span it belongs on screen for.</summary>
public sealed record WebVttCue
{
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
    public required string Text { get; init; }
}
