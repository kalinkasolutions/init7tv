namespace Init7Tv.Dto;

/// <summary>The streams a single viewer is currently watching.</summary>
public sealed class ViewerStreamsDto
{
    public string[] StreamIds { get; set; } = [];
}
