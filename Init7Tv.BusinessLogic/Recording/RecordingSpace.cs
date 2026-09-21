namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// Whether there is room to record, and to keep recording.
///
/// Asked twice for different reasons: once before a capture starts, against a guess at how much
/// it will want, and then on every pass while it runs, against what is actually left. The second
/// is the one that protects the box — a recording with no end cannot be sized up front, and even
/// one that can may be sharing the volume with something else that grows.
/// </summary>
public static class RecordingSpace
{
    // a recording at the default preset runs well under this, and guessing high
    // is the safe direction when the database shares the volume
    public const long BytesPerSecondEstimate = 1_500_000;

    /// <summary>
    /// Room a capture wants before it may begin. A length of null is a recording with no end:
    /// there is nothing to size, so all that can be asked is that the floor is clear. Sizing it
    /// against its backstop would ask for a hundred gigabytes and refuse every time.
    /// </summary>
    public static long Needed(TimeSpan? expected, long floorBytes)
    {
        if (expected is not { } length || length <= TimeSpan.Zero)
        {
            return floorBytes;
        }

        return (long)length.TotalSeconds * BytesPerSecondEstimate + floorBytes;
    }

    /// <summary>Why there is no room, or null when there is.</summary>
    public static string? TooLittle(long free, long needed) =>
        free < needed ? $"Not enough disk space: {Gigabytes(free)} GB free" : null;

    /// <summary>Why a capture under way has to stop, or null when it may carry on.</summary>
    public static string? RunningOut(long free, long floorBytes) =>
        free < floorBytes
            ? $"Stopped with {Gigabytes(free)} GB left on the disk"
            : null;

    /// <summary>
    /// What is left on the volume holding <paramref name="path"/>, or null when it cannot be read.
    /// An unreadable drive is not a reason to refuse to record.
    /// </summary>
    public static long? Free(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "/").AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Gigabytes(long bytes) => (bytes / 1_000_000_000.0).ToString("0.0");
}
