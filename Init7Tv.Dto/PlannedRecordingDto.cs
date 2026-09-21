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

    /// <summary>Whose pick it is, so an admin looking at everybody's can tell them apart.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Whether it belongs to whoever asked for the list. Answered here rather than by comparing
    /// names in the browser, because the page would have to be told who it is looking at first and
    /// until it was every pick would look like somebody else's. Only an admin ever sees one that is
    /// not theirs; the guide reads this to decide whether the viewer has already picked a
    /// programme, which somebody else having picked it says nothing about.
    /// </summary>
    public bool IsMine { get; init; }

    /// <summary>Recorded from the channel with no end, rather than a programme the guide named.</summary>
    public bool OpenEnded { get; init; }
}
