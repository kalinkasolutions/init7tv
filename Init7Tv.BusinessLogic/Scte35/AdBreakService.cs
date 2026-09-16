using System.Collections.Concurrent;

namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>
/// Follows the cue messages of every running stream.
///
/// The work is one extractor and one timeline per stream; this only keeps them
/// apart and makes them safe to reach from several requests at once.
/// </summary>
public sealed class AdBreakService : IAdBreakService
{
    private readonly ConcurrentDictionary<string, StreamCues> m_streams = new();

    public void Ingest(string streamId, byte[] transportStream)
    {
        var stream = m_streams.GetOrAdd(streamId, _ => new StreamCues());

        lock (stream)
        {
            foreach (var cue in stream.Extractor.Read(transportStream))
            {
                // A cue says when a break starts on the stream's own clock, so a cue
                // that says "immediately" is placed where the stream is now.
                stream.Timeline.Observe(cue, stream.LastPts);
            }
        }
    }

    public AdBreakForecast Forecast(string streamId, ulong currentPts)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return AdBreakForecast.Empty;
        }

        lock (stream)
        {
            stream.LastPts = currentPts;
            return stream.Timeline.Forecast(currentPts);
        }
    }

    public void Forget(string streamId) => m_streams.TryRemove(streamId, out _);

    private sealed class StreamCues
    {
        public Scte35CueExtractor Extractor { get; } = new();
        public AdBreakTimeline Timeline { get; } = new();
        public ulong LastPts { get; set; }
    }
}
