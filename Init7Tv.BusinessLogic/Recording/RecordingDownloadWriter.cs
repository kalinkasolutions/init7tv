using System.Diagnostics;
using Init7Tv.BusinessLogic.StreamManager;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class RecordingDownloadWriter : IRecordingDownloadWriter
{
    private const int Buffer = 1024 * 1024;

    private readonly ILogger<RecordingDownloadWriter> m_logger;

    public RecordingDownloadWriter(ILogger<RecordingDownloadWriter> logger)
    {
        m_logger = logger;
    }

    public async Task WriteAsync(RecordingDownloadDto download, Stream into)
    {
        var args = FfmpegArguments.BuildDownload("error");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var ffmpeg = Process.Start(startInfo);
        if (ffmpeg == null)
        {
            m_logger.LogError("Could not start ffmpeg to write a download");
            return;
        }

        // stderr has to be drained or a chatty ffmpeg fills the pipe and stops
        var complaints = ffmpeg.StandardError.ReadToEndAsync();
        var feeding = FeedAsync(download, ffmpeg.StandardInput.BaseStream);

        try
        {
            await ffmpeg.StandardOutput.BaseStream.CopyToAsync(into);
            await feeding;
            await ffmpeg.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            // the usual one is the viewer closing the tab part way through
            m_logger.LogInformation(ex, "A download ended early");
            Kill(ffmpeg);
        }

        if (ffmpeg.HasExited && ffmpeg.ExitCode != 0)
        {
            m_logger.LogError("Writing a download failed: {Errors}", await complaints);
        }
    }

    /// <summary>
    /// Sends the stretches worth keeping, in order. Cutting between them is free because every one
    /// begins on a keyframe with the program tables in front of it.
    /// </summary>
    private async Task FeedAsync(RecordingDownloadDto download, Stream into)
    {
        var buffer = new byte[Buffer];

        try
        {
            foreach (var range in download.Ranges)
            {
                await using var part = File.Open(
                    download.Parts[range.Part], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                part.Seek(range.Offset, SeekOrigin.Begin);
                var left = range.Length;

                while (left > 0)
                {
                    var read = await part.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)));
                    if (read == 0)
                    {
                        break;
                    }

                    await into.WriteAsync(buffer.AsMemory(0, read));
                    left -= read;
                }
            }
        }
        catch (Exception ex)
        {
            m_logger.LogInformation(ex, "Stopped feeding a download");
        }
        finally
        {
            await into.DisposeAsync();
        }
    }

    private void Kill(Process process)
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
            m_logger.LogWarning(ex, "Failed to stop ffmpeg after a download ended early");
        }
    }
}
