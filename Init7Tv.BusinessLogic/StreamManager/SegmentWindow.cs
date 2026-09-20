using System.Collections.Concurrent;

namespace Init7Tv.BusinessLogic.StreamManager;

/// <summary>
/// The segments of a live stream that are still worth keeping, and the bytes behind them.
///
/// A live stream is a window rather than a whole recording: the oldest segment falls off as a new
/// one arrives, and a player is told how many have gone so it can tell the window moving from the
/// segments it holds being replaced. Owning the lock here is the point — the list, the bytes and
/// that count have to move together, and leaving the locking to whoever happened to be calling is
/// what lets them drift apart.
/// </summary>
public sealed class SegmentWindow
{
    private readonly int m_keep;
    private readonly TimeSpan m_perKeyframe;

    private readonly Lock m_lock = new();
    private readonly List<TvSegment> m_playlist = [];
    private readonly ConcurrentDictionary<string, byte[]> m_bytes = new();

    private ulong m_nextIndex;
    private int m_mediaSequenceId;

    /// <param name="keep">How many segments the window holds before the oldest falls off.</param>
    /// <param name="perKeyframe">
    /// How much media one keyframe is worth. Every keyframe is one forced interval, which is what a
    /// player needs; wall clock drifts whenever the transcode runs behind realtime.
    /// </param>
    public SegmentWindow(int keep, TimeSpan perKeyframe)
    {
        m_keep = keep;
        m_perKeyframe = perKeyframe;
    }

    public int Count
    {
        get
        {
            lock (m_lock)
            {
                return m_playlist.Count;
            }
        }
    }

    public void Add(SegmentCut cut)
    {
        lock (m_lock)
        {
            var name = $"seg{m_nextIndex++}.ts";
            m_bytes[name] = cut.Bytes;
            m_playlist.Add(new TvSegment(name, cut.Keyframes * m_perKeyframe));

            while (m_playlist.Count > m_keep)
            {
                var oldest = m_playlist[0];
                m_playlist.RemoveAt(0);
                m_bytes.TryRemove(oldest.Name, out _);
                m_mediaSequenceId++;
            }
        }
    }

    /// <summary>The window as it stands, taken together so the two cannot disagree.</summary>
    public (TvSegment[] Segments, int MediaSequenceId) Snapshot()
    {
        lock (m_lock)
        {
            return (m_playlist.ToArray(), m_mediaSequenceId);
        }
    }

    /// <summary>The bytes of one segment, or null once it has fallen out of the window.</summary>
    public byte[]? Bytes(string name) => m_bytes.GetValueOrDefault(name);
}
