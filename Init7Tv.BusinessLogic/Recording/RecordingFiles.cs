using System.Globalization;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// Where the pieces of a recording live.
///
/// A directory per recording named after its id, never after its title: titles
/// come from the guide and carry slashes, colons and worse, and a path built
/// from one is a traversal waiting to happen. The readable name is kept in the
/// database and only appears in the download header.
/// </summary>
public static class RecordingFiles
{
    private const string CapturePrefix = "capture-";
    private const string CaptureSuffix = ".ts";

    public const string FinalName = "recording.mp4";
    public const string PartListName = "parts.txt";

    public static string DirectoryFor(string root, Guid captureId) =>
        Path.Combine(root, captureId.ToString("N"));

    /// <summary>The capture a directory belongs to, the directory being named after it.</summary>
    public static Guid CaptureIdOf(string directory) =>
        Guid.TryParseExact(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), "N", out var id)
            ? id
            : Guid.Empty;

    public static string FinalPath(string directory) => Path.Combine(directory, FinalName);

    public static string CapturePath(string directory, int part) =>
        Path.Combine(directory, $"{CapturePrefix}{part.ToString(CultureInfo.InvariantCulture)}{CaptureSuffix}");

    /// <summary>The captures made so far, in the order they were recorded.</summary>
    public static string[] Captures(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, $"{CapturePrefix}*{CaptureSuffix}")
            .Select(path => (path, part: PartOf(path)))
            .Where(x => x.part > 0)
            .OrderBy(x => x.part)
            .Select(x => x.path)
            .ToArray();
    }

    /// <summary>The number the next capture takes, so a resumed recording adds to the set.</summary>
    public static int NextPart(string directory)
    {
        var captures = Captures(directory);
        return captures.Length == 0 ? 1 : PartOf(captures[^1]) + 1;
    }

    private static int PartOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name[CapturePrefix.Length..], CultureInfo.InvariantCulture, out var part) ? part : 0;
    }
}
