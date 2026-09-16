namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>Values of splice_command_type, ANSI/SCTE 35 2023r1 table 7.</summary>
public enum SpliceCommandType : byte
{
    SpliceNull = 0x00,
    SpliceSchedule = 0x04,
    SpliceInsert = 0x05,
    TimeSignal = 0x06,
    BandwidthReservation = 0x07,
    PrivateCommand = 0xFF
}

/// <summary>
/// segmentation_type_id, table 23. Only the values this code reasons about are
/// named; anything else comes through as its number.
/// </summary>
public enum SegmentationType : byte
{
    NotIndicated = 0x00,
    ProgramStart = 0x10,
    ProgramEnd = 0x11,
    BreakStart = 0x22,
    BreakEnd = 0x23,
    ProviderAdvertisementStart = 0x30,
    ProviderAdvertisementEnd = 0x31,
    DistributorAdvertisementStart = 0x32,
    DistributorAdvertisementEnd = 0x33,
    ProviderPlacementOpportunityStart = 0x34,
    ProviderPlacementOpportunityEnd = 0x35,
    DistributorPlacementOpportunityStart = 0x36,
    DistributorPlacementOpportunityEnd = 0x37,
    ProviderOverlayPlacementOpportunityStart = 0x38,
    ProviderOverlayPlacementOpportunityEnd = 0x39,
    ProviderPromoStart = 0x3C,
    ProviderPromoEnd = 0x3D,
    DistributorPromoStart = 0x3E,
    DistributorPromoEnd = 0x3F
}

/// <summary>splice_time(), table 14. Absent time means "as soon as this arrives".</summary>
public sealed record SpliceTime
{
    public ulong? PtsTime { get; init; }
}

/// <summary>break_duration(), table 15.</summary>
public sealed record BreakDuration
{
    public required bool AutoReturn { get; init; }

    /// <summary>90 kHz ticks.</summary>
    public required ulong Duration { get; init; }
}

/// <summary>splice_insert(), table 10. Component splice mode is deprecated and not read.</summary>
public sealed record SpliceInsert
{
    public required uint SpliceEventId { get; init; }
    public required bool Cancelled { get; init; }
    public bool OutOfNetwork { get; init; }
    public bool ProgramSplice { get; init; }
    public bool SpliceImmediate { get; init; }
    public SpliceTime? SpliceTime { get; init; }
    public BreakDuration? BreakDuration { get; init; }
    public ushort UniqueProgramId { get; init; }
    public byte AvailNum { get; init; }
    public byte AvailsExpected { get; init; }
}

/// <summary>segmentation_descriptor(), table 20.</summary>
public sealed record SegmentationDescriptor
{
    public required uint SegmentationEventId { get; init; }
    public required bool Cancelled { get; init; }
    public SegmentationType Type { get; init; }

    /// <summary>90 kHz ticks, when segmentation_duration_flag was set.</summary>
    public ulong? Duration { get; init; }

    public byte UpidType { get; init; }
    public byte[] Upid { get; init; } = [];
    public byte SegmentNumber { get; init; }
    public byte SegmentsExpected { get; init; }
    public bool DeliveryNotRestricted { get; init; } = true;
    public bool WebDeliveryAllowed { get; init; } = true;
}

/// <summary>
/// A parsed splice_info_section, ANSI/SCTE 35 2023r1 table 5.
/// </summary>
public sealed record SpliceInfoSection
{
    public const byte TableId = 0xFC;

    /// <summary>The 90 kHz clock every time in this message is counted in.</summary>
    public const long TicksPerSecond = 90_000;

    /// <summary>pts_time and pts_adjustment are 33 bit and wrap roughly every 26.5 hours.</summary>
    public const ulong PtsModulus = 1UL << 33;

    public required byte ProtocolVersion { get; init; }

    /// <summary>
    /// The body is encrypted, so the command and descriptors could not be read.
    /// pts_adjustment and tier are in the clear either way.
    /// </summary>
    public required bool Encrypted { get; init; }

    /// <summary>Added to every pts_time in this message, ignoring the carry.</summary>
    public required ulong PtsAdjustment { get; init; }

    public required ushort Tier { get; init; }
    public required SpliceCommandType CommandType { get; init; }
    public SpliceInsert? SpliceInsert { get; init; }
    public SpliceTime? TimeSignal { get; init; }
    public IReadOnlyList<SegmentationDescriptor> SegmentationDescriptors { get; init; } = [];

    /// <summary>A pts_time from this message, moved into the stream's time base.</summary>
    public ulong Adjusted(ulong ptsTime) => (ptsTime + PtsAdjustment) % PtsModulus;

    public static TimeSpan ToTimeSpan(ulong ticks) =>
        TimeSpan.FromTicks((long)ticks * TimeSpan.TicksPerSecond / TicksPerSecond);
}
