namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>Which kind of cue message announced a break.</summary>
public enum AdBreakSignal
{
    /// <summary>splice_insert with out_of_network set, the legacy signal.</summary>
    SpliceInsert,

    /// <summary>time_signal carrying a segmentation_descriptor.</summary>
    Segmentation
}

/// <summary>An ad break the stream has announced.</summary>
public sealed record AdBreak
{
    /// <summary>splice_event_id or segmentation_event_id, whichever announced it.</summary>
    public required uint EventId { get; init; }

    public required AdBreakSignal Signal { get; init; }

    /// <summary>Start of the break on the stream's 90 kHz clock, pts_adjustment applied.</summary>
    public required ulong StartPts { get; init; }

    /// <summary>
    /// Where the stream was when the break was announced, on whatever clock the reader is following.
    ///
    /// Only a recording needs this. A capture carries the cue messages ffmpeg copied out of the
    /// source, untouched, so the splice time inside them is still on the source's clock while the
    /// pictures around them were given a clock of their own. The two cannot be compared, and the one
    /// thing that can be placed in a recording is the moment the announcement went past.
    /// </summary>
    public required ulong ArrivalPts { get; init; }

    /// <summary>Absent when the signal carried no duration; the end is then announced separately.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The kind of segment, when a segmentation_descriptor announced it.</summary>
    public SegmentationType? SegmentationType { get; init; }

    /// <summary>The splicer returns to the programme on its own at the end of the break.</summary>
    public bool AutoReturn { get; init; }

    /// <summary>Identifier of what is being signalled, when the descriptor carried one.</summary>
    public byte[] Upid { get; init; } = [];

    /// <summary>Set when the signal said this content may not be delivered over the web.</summary>
    public bool WebDeliveryBlocked { get; init; }

    /// <summary>
    /// Set when the break was already running before there was anything to read: only its end was
    /// announced here, so it starts wherever the reading does rather than where the break did.
    /// </summary>
    public bool AlreadyInProgress { get; init; }
}

/// <summary>What the cue messages say about a stream at a point in its timeline.</summary>
public sealed record AdBreakForecast
{
    public static readonly AdBreakForecast Empty = new();

    /// <summary>The break the stream is inside, if it is inside one.</summary>
    public AdBreak? InProgress { get; init; }

    /// <summary>How long the break in progress still has to run, when its length is known.</summary>
    public TimeSpan? Remaining { get; init; }

    /// <summary>The next announced break.</summary>
    public AdBreak? Next { get; init; }

    /// <summary>How far ahead that break starts.</summary>
    public TimeSpan? StartsIn { get; init; }
}
