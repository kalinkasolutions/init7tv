using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>What to capture, as the engine needs it. No database types: the engine
/// only ever handles processes and files.</summary>
public sealed record RecordingRequest
{
    /// <summary>One capture, which is one ffmpeg, however many people picked it.</summary>
    public required Guid CaptureId { get; init; }

    public required ChannelDto Channel { get; init; }
    public required string Directory { get; init; }

    /// <summary>What -t is set from; the recording ends by itself after this.</summary>
    public required TimeSpan Duration { get; init; }

    public required string Preset { get; init; }
    public required string LogLevel { get; init; }
}

/// <summary>A capture whose ffmpeg has exited, whatever the reason.</summary>
public sealed record FinishedCapture
{
    public required Guid CaptureId { get; init; }
    public required int ExitCode { get; init; }

    /// <summary>Set when we stopped it rather than it running out of -t.</summary>
    public required bool Stopped { get; init; }

    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }
}

public interface IRecordingEngine
{
    Task<OperationResult<bool>> StartAsync(RecordingRequest request);

    bool IsRunning(Guid captureId);
    int ActiveCount { get; }

    void Stop(Guid captureId);

    /// <summary>Hands back the captures that have exited since the last call, and forgets them.</summary>
    FinishedCapture[] TakeFinished();

    /// <summary>Turns the captures in a directory into the mp4 that is kept, and returns its size.</summary>
    Task<OperationResult<long>> FinalizeAsync(string directory, string logLevel);

    /// <summary>
    /// Writes somebody's share of a capture into a file of its own: everything up to the moment they
    /// let go of it. The capture is left alone, because whoever is still waiting for it is having it
    /// written as this runs.
    /// </summary>
    Task<OperationResult<long>> ForkAsync(string fromDirectory, string intoDirectory, TimeSpan upTo, string logLevel);

    void StopAll();
}
