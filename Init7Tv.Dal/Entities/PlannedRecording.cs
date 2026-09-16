using System.ComponentModel.DataAnnotations;

namespace Init7Tv.Dal.Entities;

/// <summary>
/// A programme somebody asked to have recorded.
///
/// The guide's own id is the key: asking twice for the same programme is the
/// same request, and the guide is the only thing that can say when it airs.
/// What it said is copied here rather than looked up later, because a recording
/// has to survive the guide changing its mind or dropping the entry.
/// </summary>
public sealed class PlannedRecording
{
    /// <summary>The programme's id in the guide.</summary>
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

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime PlannedAt { get; set; }
}
