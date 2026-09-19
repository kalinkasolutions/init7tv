namespace Init7Tv.Dto;

/// <summary>A recording under way, as the dashboard shows it.</summary>
public sealed class CurrentRecordingDto
{
    public Guid RecordingId { get; set; }
    public Guid ChannelId { get; set; }
    public string ChannelDisplayName { get; set; } = string.Empty;

    /// <summary>Taken from the channel list rather than the row, which keeps no picture.</summary>
    public byte[] ChannelLogo { get; set; } = [];

    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;

    /// <summary>Everybody who asked for it; one capture serves all of them.</summary>
    public string[] UserNames { get; set; } = [];

    public DateTime StartedAt { get; set; }
    public DateTime ScheduledEnd { get; set; }
}
