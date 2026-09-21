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

    /// <summary>
    /// How much room to leave free. A recording is stopped rather than allowed to take the box
    /// past this, and the database shares the volume.
    /// </summary>
    public long FreeSpaceFloorBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>
    /// How often to look at the free space while something is recording. A recording with no end
    /// has nothing else to bring the loop back, and at a few megabits a second an hour of not
    /// looking is gigabytes.
    /// </summary>
    public int SpaceCheckSeconds { get; set; } = 60;

    /// <summary>
    /// The longest a recording started with no end runs before it stops on its own. Not the limit
    /// on such a recording — the free space is that — only a backstop, so one forgotten with the
    /// scheduler wedged cannot run for ever.
    /// </summary>
    public TimeSpan OpenEndedBackstop { get; set; } = TimeSpan.FromHours(24);
}
