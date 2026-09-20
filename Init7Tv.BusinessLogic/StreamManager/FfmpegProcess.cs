using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.StreamManager;

/// <summary>
/// Starting and stopping the ffmpeg processes. The live stream, a capture and a download each run
/// their own, and all three need the same few things got right: arguments that cannot be misread as
/// flags, stderr going somewhere, and a kill that copes with a process already gone.
/// </summary>
public static class FfmpegProcess
{
    /// <summary>
    /// An ffmpeg whose stderr goes to the log under <paramref name="tag"/>, which is what says
    /// which stream or recording a line came from.
    ///
    /// Not started, and BeginErrorReadLine has to follow Start or none of it is logged after all.
    /// </summary>
    public static Process Logged(string[] args, ILogger logger, string tag, bool readOutput = false)
    {
        var process = Create(args, readOutput, writeInput: false);

        // ffmpeg logs to stderr; without this the configured log level went to the
        // container's console with no indication of which stream produced it
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                logger.LogInformation("[ffmpeg {Tag}] {Line}", tag, e.Data);
            }
        };

        return process;
    }

    /// <summary>
    /// An ffmpeg with every pipe open and nothing reading them, for a caller that feeds and drains
    /// it itself. Whoever starts one has to drain stderr as well, or a chatty ffmpeg fills that
    /// pipe and stops.
    /// </summary>
    public static Process Piped(string[] args) => Create(args, readOutput: true, writeInput: true);

    /// <summary>
    /// Whether it is gone. A disposed Process throws when asked rather than reporting that it
    /// exited, and these are disposed as soon as ffmpeg dies.
    /// </summary>
    public static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public static void Kill(Process process, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            // a disposed process throws here rather than reporting that it exited
            logger.LogWarning(ex, "Failed to kill {FileName}", process.StartInfo.FileName);
        }
    }

    private static Process Create(string[] args, bool readOutput, bool writeInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardInput = writeInput,
            RedirectStandardOutput = readOutput,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList quotes each entry, so a source url can never inject extra flags
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }
}
