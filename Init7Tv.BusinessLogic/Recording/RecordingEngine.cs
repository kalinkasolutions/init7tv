using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
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
    private const int KeyframeSeconds = 4;

    private static readonly TimeSpan RemuxTimeout = TimeSpan.FromMinutes(30);

    private readonly ILogger<RecordingEngine> m_logger;
    private readonly IFfprobeService m_ffprobeService;
    private readonly RecordingSignal m_signal;
    private readonly Init7TvOptions m_options;

    private readonly ConcurrentDictionary<Guid, ActiveRecording> m_active = new();
    private readonly ConcurrentQueue<FinishedCapture> m_finished = new();

    private bool m_disposed;

    public RecordingEngine(
        ILogger<RecordingEngine> logger,
        IFfprobeService ffprobeService,
        RecordingSignal signal,
        IOptions<Init7TvOptions> options
    )
    {
        m_logger = logger;
        m_ffprobeService = ffprobeService;
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

        var sourceUrl = m_options.UseMultiCast ? request.Channel.UdpSource : request.Channel.HlsSource;

        var streamInfo = await m_ffprobeService.ProbeAsync(sourceUrl);
        if (streamInfo.HasError)
        {
            return streamInfo.MapError<bool>();
        }

        Directory.CreateDirectory(request.Directory);
        var capturePath = RecordingFiles.CapturePath(
            request.Directory, RecordingFiles.NextPart(request.Directory));

        var args = FfmpegArguments.BuildRecording(
            request.Channel,
            // the pick carries no language preference, so the first track it is
            audioStreamIndex: 0,
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

        var process = CreateProcess(args, request.CaptureId, request.Channel.CanonicalName);
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
            Kill(process);
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
        Kill(recording.Ffmpeg);
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

    public async Task<OperationResult<long>> FinalizeAsync(string directory, string logLevel)
    {
        var captures = RecordingFiles.Captures(directory);
        if (captures.Length == 0)
        {
            return OperationResult<long>.NotFound("Nothing was captured");
        }

        // a capture that never got a byte is worse than nothing: ffmpeg fails on
        // it and the failure hides whatever the real cause was
        captures = captures.Where(path => new FileInfo(path).Length > 0).ToArray();
        if (captures.Length == 0)
        {
            return OperationResult<long>.Error("The capture was empty");
        }

        var finalPath = RecordingFiles.FinalPath(directory);
        var listPath = Path.Combine(directory, RecordingFiles.PartListName);

        string[] args;
        if (captures.Length == 1)
        {
            args = FfmpegArguments.BuildRemux(captures[0], finalPath, logLevel);
        }
        else
        {
            // concat is safe here only because every part came out of the same
            // encoder settings, which is true by construction
            await File.WriteAllLinesAsync(listPath, captures.Select(path => $"file '{path}'"));
            args = FfmpegArguments.BuildConcat(listPath, finalPath, logLevel);
        }

        m_logger.LogInformation("finalizing {Directory} from {Parts} part(s)", directory, captures.Length);

        var remuxed = await RunToCompletion(args, directory);
        if (remuxed.HasError)
        {
            return remuxed.MapError<long>();
        }

        var file = new FileInfo(finalPath);
        if (!file.Exists || file.Length == 0)
        {
            return OperationResult<long>.Error("The recording could not be written");
        }

        foreach (var capture in captures)
        {
            TryDelete(capture);
        }

        TryDelete(listPath);

        return OperationResult<long>.Success(file.Length);
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

    private Process CreateProcess(string[] args, Guid captureId, string channelName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList quotes each entry, so a source url can never inject extra flags
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // why a capture died is only ever visible here
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                m_logger.LogInformation("[recording {CaptureId} {Channel}] {Line}", captureId, channelName, e.Data);
            }
        };

        return process;
    }

    /// <summary>Runs a short-lived ffmpeg, being the remux, and waits for it.</summary>
    private async Task<OperationResult<bool>> RunToCompletion(string[] args, string directory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to start ffmpeg to finalize {Directory}", directory);
            return OperationResult<bool>.Error("Failed to finalize the recording");
        }

        if (process == null)
        {
            return OperationResult<bool>.Error("Failed to finalize the recording");
        }

        using var _ = process;
        using var timeout = new CancellationTokenSource(RemuxTimeout);

        var errors = new StringBuilder();

        try
        {
            var reading = ReadErrors(process, errors, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await reading;
        }
        catch (OperationCanceledException)
        {
            m_logger.LogError("Finalizing {Directory} timed out after {Timeout}", directory, RemuxTimeout);
            Kill(process);
            return OperationResult<bool>.Error("Timed out while finalizing the recording");
        }

        if (process.ExitCode != 0)
        {
            m_logger.LogError("Finalizing {Directory} failed with exit code {ExitCode}: {Errors}",
                directory, process.ExitCode, errors.ToString());
            return OperationResult<bool>.Error("Failed to finalize the recording");
        }

        return OperationResult<bool>.Success(true);
    }

    private static async Task ReadErrors(Process process, StringBuilder errors, CancellationToken cancellationToken)
    {
        var text = await process.StandardError.ReadToEndAsync(cancellationToken);
        errors.Append(text);
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
            // a disposed process throws here rather than reporting that it exited
            m_logger.LogWarning(ex, "Failed to kill ffmpeg");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            m_logger.LogWarning(ex, "Failed to delete {Path}", path);
        }
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
