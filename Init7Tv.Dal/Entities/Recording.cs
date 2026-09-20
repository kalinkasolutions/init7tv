using System.ComponentModel.DataAnnotations;

namespace Init7Tv.Dal.Entities;

public enum RecordingState
{
    /// <summary>Claimed by the scheduler, no process yet.</summary>
    Pending,
    Recording,

    /// <summary>Captured, being remuxed into the file that is kept.</summary>
    Finalizing,
    Completed,

    /// <summary>Something is on disk and playable, but not the whole programme.</summary>
    Interrupted,
    Failed,

    /// <summary>Never started, and why is in <see cref="Recording.ErrorMessage"/>.</summary>
    Skipped
}

/// <summary>
/// An attempt at recording a programme, and what came of it.
///
/// Separate from <see cref="PlannedRecording"/> because a plan is what somebody
/// asked for and this is what happened: removing the plan must not remove the
/// file, and one plan can produce more than one of these when the first attempt
/// fails. Two people who picked the same programme get a row each, both naming
/// the same file, so the programme is only ever encoded once.
/// </summary>
public sealed class Recording
{
    public Guid RecordingId { get; set; }

    /// <summary>The programme's id in the guide, which is what makes two picks one recording.</summary>
    public Guid ProgrammeId { get; set; }

    [MaxLength(256)]
    public string UserName { get; set; } = string.Empty;

    public Guid ChannelId { get; set; }

    [MaxLength(255)]
    public string ChannelName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string CanonicalName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string SubTitle { get; set; } = string.Empty;

    /// <summary>The padded window, so a restart works out the same end time again.</summary>
    public DateTime ScheduledStart { get; set; }

    public DateTime ScheduledEnd { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public RecordingState State { get; set; }

    /// <summary>The directory holding the capture and the finished file.</summary>
    [MaxLength(1024)]
    public string Directory { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>
    /// How many advertising breaks the capture announced, counted once when the recording was
    /// finished. Only whether there are any matters to the page, which offers a download without
    /// them when there is something to leave out; the download itself works the breaks out again
    /// from the capture rather than trusting this.
    /// </summary>
    public int AdBreakCount { get; set; }

    [MaxLength(500)]
    public string ErrorMessage { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
