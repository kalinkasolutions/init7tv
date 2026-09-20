using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class RecordingSegmentCache : IRecordingSegmentCache
{
    /// <summary>
    /// How many recordings keep an index. An index is one entry per keyframe, so a long recording
    /// runs to tens of thousands of them, and every recording that is finished, played or
    /// downloaded leaves one behind: without a bound, a box that has recorded for a year holds one
    /// for every recording it has ever made. This is comfortably more than are ever being written
    /// and watched at once, and dropping one costs the next request a rescan and nothing else.
    /// </summary>
    private const int Keep = 16;

    private readonly Lock m_lock = new();
    private readonly Dictionary<string, RecordingSegments> m_indexes = [];

    /// <summary>Least recently asked about first, which is the one to drop.</summary>
    private readonly List<string> m_order = [];

    public IReadOnlyList<RecordingSegment> Segments(
        string directory,
        IReadOnlyList<string> parts,
        bool finished
    )
    {
        var index = Index(directory);

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
        RecordingSegments? index;

        lock (m_lock)
        {
            m_indexes.TryGetValue(directory, out index);
        }

        if (index == null)
        {
            return [];
        }

        lock (index)
        {
            return index.Breaks;
        }
    }

    public void Forget(string directory)
    {
        lock (m_lock)
        {
            m_indexes.Remove(directory);
            m_order.Remove(directory);
        }
    }

    /// <summary>The index for one recording, made if it is not there and marked as the newest.</summary>
    private RecordingSegments Index(string directory)
    {
        lock (m_lock)
        {
            if (!m_indexes.TryGetValue(directory, out var index))
            {
                index = new RecordingSegments(RecordingEngine.KeyframeSeconds);
                m_indexes[directory] = index;
            }

            m_order.Remove(directory);
            m_order.Add(directory);

            while (m_order.Count > Keep)
            {
                m_indexes.Remove(m_order[0]);
                m_order.RemoveAt(0);
            }

            return index;
        }
    }
}
