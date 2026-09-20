namespace Init7Tv.BusinessLogic.Scte35;

public interface IAdBreakService
{
    /// <summary>
    /// Feeds transport stream bytes from a channel's source. Has to be the source:
    /// the transcode rebuilds the PMT around the streams it was asked for and drops
    /// the cue PID.
    /// </summary>
    void Ingest(string streamId, byte[] transportStream);

    /// <summary>What the channel has announced, seen from where the stream is now.</summary>
    AdBreakForecast Forecast(string streamId, ulong currentPts);

    void Forget(string streamId);
}
