using System.Collections.Concurrent;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class RecordingSegmentCache : IRecordingSegmentCache
{
    private readonly ConcurrentDictionary<string, RecordingSegments> m_indexes = new();

    public IReadOnlyList<RecordingSegment> Segments(
        string directory,
        IReadOnlyList<string> parts,
        bool finished
    )
    {
        var index = m_indexes.GetOrAdd(directory, _ => new RecordingSegments(RecordingEngine.KeyframeSeconds));

        // one reader at a time per recording: two playlist requests arriving together would
        // otherwise both scan the same new bytes and add the same segments twice
        lock (index)
        {
            index.Extend(parts, finished);

            return index.Segments.ToArray();
        }
    }

    public IReadOnlyList<AdBreakMark> Breaks(string directory)
    {
        if (!m_indexes.TryGetValue(directory, out var index))
        {
            return [];
        }

        lock (index)
        {
            return index.Breaks;
        }
    }

    public void Forget(string directory) => m_indexes.TryRemove(directory, out _);
}
