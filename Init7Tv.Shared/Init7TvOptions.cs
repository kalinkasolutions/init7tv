namespace Init7Tv.Shared;

public sealed class Init7TvOptions
{
    public bool UseMultiCast { get; set; }

    /// <summary>
    /// Where recordings are written. Inside the data volume by default, so they
    /// survive the container being replaced the same way the database does.
    /// </summary>
    public string RecordingPath { get; set; } = "/var/srv/recordings";

    /// <summary>Each one is an encode, so this is a CPU budget rather than a disk one.</summary>
    public int MaxConcurrentRecordings { get; set; } = 2;
}
