using System.Collections.Concurrent;
using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.Dto;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// The ffmpeg processes that are recording right now.
///
/// Deliberately knows nothing about the database: the scheduler owns the rows
/// and this owns the processes, so a failure on one side cannot corrupt the
/// other. Every capture ends on its own through -t; stopping one is the
/// exception rather than the normal path.
/// </summary>
public sealed class RecordingEngine : IRecordingEngine, IDisposable
{
    // keyframes are seek granularity in a file rather than segment boundaries,
    // so they can be further apart than the live stream's two seconds
    /// <summary>Public so the playlist cannot drift from the spacing actually recorded.</summary>
    public const int KeyframeSeconds = 4;

    private readonly ILogger<RecordingEngine> m_logger;
    private readonly IFfprobeService m_ffprobeService;
    private readonly IRecordingSegmentCache m_segments;
    private readonly RecordingSignal m_signal;
    private readonly Init7TvOptions m_options;

    private readonly ConcurrentDictionary<Guid, ActiveRecording> m_active = new();
    private readonly ConcurrentQueue<FinishedCapture> m_finished = new();

    private bool m_disposed;

    public RecordingEngine(
        ILogger<RecordingEngine> logger,
        IFfprobeService ffprobeService,
        IRecordingSegmentCache segments,
        RecordingSignal signal,
        IOptions<Init7TvOptions> options
    )
    {
        m_logger = logger;
        m_ffprobeService = ffprobeService;
        m_segments = segments;
        m_signal = signal;
        m_options = options.Value;
    }

    public int ActiveCount => m_active.Count;


    public bool IsRunning(Guid captureId) => m_active.ContainsKey(captureId);

    public async Task<OperationResult<bool>> StartAsync(RecordingRequest request)
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(RecordingEngine));
        }

        if (m_active.ContainsKey(request.CaptureId))
        {
            return OperationResult<bool>.Conflict("That recording is already running");
        }

        var sourceUrl = FfmpegArguments.SourceUrl(request.Channel, m_options.UseMultiCast);

        var streamInfo = await m_ffprobeService.ProbeAsync(sourceUrl);
        if (streamInfo.HasError)
        {
            return streamInfo.MapError<bool>();
        }

        Directory.CreateDirectory(request.Directory);
        var capturePath = RecordingFiles.CapturePath(
            request.Directory, RecordingFiles.NextPart(request.Directory));

        // every track is kept; the pick carries no language preference, so the channel's own decides
        // only which of them leads
        var audioStreams = streamInfo.Value.GetAudioStreamsToRecord(request.Channel.MainLanguage);

        var args = FfmpegArguments.BuildRecording(
            request.Channel,
            audioStreams,
            request.Preset,
            request.LogLevel,
            streamInfo.Value,
            m_options.UseMultiCast,
            KeyframeSeconds,
            request.Duration,
            capturePath);

        m_logger.LogInformation(
            "recording {CaptureId} ({Channel}) to {CapturePath} for {Duration}, ffmpeg args: {Args}",
            request.CaptureId, request.Channel.CanonicalName, capturePath, request.Duration,
            string.Join(' ', args));

        // why a capture died is only ever visible in its ffmpeg's own output
        var process = FfmpegProcess.Logged(
            args, m_logger, $"recording {request.CaptureId} {request.Channel.CanonicalName}");
        var recording = new ActiveRecording
        {
            CaptureId = request.CaptureId,
            Ffmpeg = process,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to start ffmpeg for recording {CaptureId}", request.CaptureId);
            process.Dispose();
            return OperationResult<bool>.Error("Failed to start the recording");
        }

        if (!m_active.TryAdd(request.CaptureId, recording))
        {
            m_logger.LogError("Recording {CaptureId} was started twice, discarding this one", request.CaptureId);
            FfmpegProcess.Kill(process, m_logger);
            process.Dispose();
            return OperationResult<bool>.Conflict("That recording is already running");
        }

        _ = Task.Run(() => WatchAsync(recording));

        return OperationResult<bool>.Success(true);
    }

    public void Stop(Guid captureId)
    {
        if (!m_active.TryGetValue(captureId, out var recording))
        {
            return;
        }

        m_logger.LogInformation("Stopping recording {CaptureId}", captureId);

        // safe to kill outright, because a transport stream has no trailer to
        // write. An mp4 would have needed to be asked nicely.
        recording.Stopped = true;
        FfmpegProcess.Kill(recording.Ffmpeg, m_logger);
    }

    public FinishedCapture[] TakeFinished()
    {
        var taken = new List<FinishedCapture>();

        while (m_finished.TryDequeue(out var finished))
        {
            taken.Add(finished);
        }

        return taken.ToArray();
    }

    public void StopAll()
    {
        foreach (var captureId in m_active.Keys.ToArray())
        {
            Stop(captureId);
        }
    }

    public async Task<OperationResult<long>> ForkAsync(string fromDirectory, string intoDirectory, TimeSpan upTo)
    {
        var parts = RecordingFiles.Captures(fromDirectory);
        var segments = m_segments.Segments(fromDirectory, parts, finished: false);

        // whole segments only: half of one starts nowhere a player can begin
        var theirs = new List<RecordingSegment>();
        var seconds = 0.0;

        foreach (var segment in segments)
        {
            if (seconds >= upTo.TotalSeconds)
            {
                break;
            }

            theirs.Add(segment);
            seconds += segment.Seconds;
        }

        if (theirs.Count == 0)
        {
            return OperationResult<long>.Error("Nothing had been recorded yet");
        }

        Directory.CreateDirectory(intoDirectory);
        var written = 0L;

        try
        {
            // one file per part of the original, so the discontinuities line up the same way
            foreach (var group in theirs.GroupBy(x => x.Part).OrderBy(x => x.Key))
            {
                var from = parts[group.Key];
                var into = RecordingFiles.CapturePath(intoDirectory, group.Key + 1);

                written += CopyRange(from, into, group.Min(x => x.Offset), group.Sum(x => x.Length));
            }
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to copy a share of {Directory}", fromDirectory);
            return OperationResult<long>.Error("Your part of the recording could not be saved");
        }

        return OperationResult<long>.Success(written);
    }

    /// <summary>Copies a stretch of a file that is still being appended to.</summary>
    private static long CopyRange(string from, string into, long offset, long length)
    {
        using var source = File.Open(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var target = File.Create(into);

        source.Seek(offset, SeekOrigin.Begin);

        var buffer = new byte[1024 * 1024];
        var left = length;

        while (left > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0)
            {
                break;
            }

            target.Write(buffer, 0, read);
            left -= read;
        }

        return length - left;
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        StopAll();
    }

    /// <summary>
    /// Waits for one capture to end and queues what happened. The only place a
    /// recording's process is disposed, because a disposed Process throws when
    /// asked whether it exited rather than saying that it did.
    /// </summary>
    private async Task WatchAsync(ActiveRecording recording)
    {
        var exitCode = -1;

        try
        {
            await recording.Ffmpeg.WaitForExitAsync();
            exitCode = recording.Ffmpeg.ExitCode;
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to wait for recording {CaptureId}", recording.CaptureId);
        }

        // out of reach before it is disposed, so nothing can ask a dead object
        m_active.TryRemove(recording.CaptureId, out _);

        try
        {
            recording.Ffmpeg.Dispose();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to dispose ffmpeg for recording {CaptureId}", recording.CaptureId);
        }

        m_logger.LogInformation("Recording {CaptureId} ended with exit code {ExitCode}",
            recording.CaptureId, exitCode);

        m_finished.Enqueue(new FinishedCapture
        {
            CaptureId = recording.CaptureId,
            ExitCode = exitCode,
            Stopped = recording.Stopped,
            StartedAt = recording.StartedAt,
            EndedAt = DateTime.UtcNow
        });

        // A capture that has ended is either finished or worth going back for, and both want doing
        // now rather than whenever the loop happened to be due to wake.
        m_signal.Signal();
    }

    private sealed class ActiveRecording
    {
        public required Guid CaptureId { get; init; }
        public required Process Ffmpeg { get; init; }
        public required DateTime StartedAt { get; init; }

        /// <summary>Set when we killed it, so an exit code of its own is not a failure.</summary>
        public bool Stopped { get; set; }
    }
}
