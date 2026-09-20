namespace Init7Tv.Dto;

/// <summary>
/// Where the advertising falls in a recording, in seconds from its start.
///
/// A mark rather than a cut: the recording keeps every frame it caught, and what these are for is
/// offering to skip past one and leaving out the ones somebody asked to download without. A break
/// that was announced wrongly then costs a button that does nothing rather than content that is
/// gone for good.
/// </summary>
public sealed record AdBreakMark
{
    public required double StartsAt { get; init; }
    public required double EndsAt { get; init; }
}
