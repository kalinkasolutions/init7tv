namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// Everything a download needs: which captures, and which stretches of them to send.
///
/// Not a file, because there is not one. A recording is kept as the transport stream it was
/// captured as, and the mp4 is made out of these ranges as they are written to the response.
/// </summary>
public sealed record RecordingDownloadDto
{
    public required string[] Parts { get; init; }

    /// <summary>In order, and with the advertising left out when that was asked for.</summary>
    public required IReadOnlyList<RecordingSegment> Ranges { get; init; }

    public required string DownloadName { get; init; }
}
