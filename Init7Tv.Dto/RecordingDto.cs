namespace Init7Tv.Dto;

/// <summary>A recording as the page shows it.</summary>
public sealed record RecordingDto
{
    public Guid RecordingId { get; init; }
    public Guid ProgrammeId { get; init; }
    public Guid ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SubTitle { get; init; } = string.Empty;

    public DateTime ScheduledStart { get; init; }
    public DateTime ScheduledEnd { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }

    /// <summary>The recording state as a name, so the page can badge it.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Only a finished recording has a file to play.</summary>
    public bool IsPlayable { get; init; }

    public long FileSizeBytes { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>Whose pick this was, shown to admins looking at everybody's.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Anybody else recording the same programme off the same capture. Stopping cannot cut a file
    /// somebody else is still filling, so the page says so before it lets go of it.
    /// </summary>
    public string[] SharedWith { get; init; } = [];

    /// <summary>
    /// How many advertising breaks the capture announced. The page offers a download without them
    /// only when there is something to leave out: most channels announce nothing, and a button that
    /// silently hands back the whole recording is worse than no button.
    ///
    /// Where they fall is asked for separately, by whoever is about to play it.
    /// </summary>
    public int AdBreakCount { get; init; }
}

/// <summary>
/// Everything the file endpoint needs, which is deliberately not the file: a
/// recording is gigabytes and must never be read into memory to be served.
/// </summary>
public sealed record RecordingFileDto
{
    public required string Path { get; init; }
    public required string DownloadName { get; init; }
    public required DateTimeOffset LastModified { get; init; }
    public required long Length { get; init; }
}
