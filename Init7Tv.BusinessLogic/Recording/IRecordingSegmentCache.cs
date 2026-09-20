using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// Keeps the segment index of each capture, so asking again costs only the bytes written since.
/// A singleton: a recording runs for hours and is asked for its playlist every few seconds, and
/// rebuilding the index each time would read the whole file over and over.
/// </summary>
public interface IRecordingSegmentCache
{
    IReadOnlyList<RecordingSegment> Segments(string directory, IReadOnlyList<string> parts, bool finished);

    /// <summary>
    /// Where the advertising falls, from the cue messages carried in the capture. Only what the
    /// last scan found, so it is asked for after the segments rather than before them.
    /// </summary>
    IReadOnlyList<AdBreakMark> Breaks(string directory);

    void Forget(string directory);
}
