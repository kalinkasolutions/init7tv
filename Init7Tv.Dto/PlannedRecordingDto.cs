namespace Init7Tv.Dto;

/// <summary>A programme somebody asked to have recorded.</summary>
public sealed record PlannedRecordingDto
{
    /// <summary>The programme's id in the guide.</summary>
    public Guid ProgrammeId { get; init; }

    public Guid ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SubTitle { get; init; } = string.Empty;
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
}
