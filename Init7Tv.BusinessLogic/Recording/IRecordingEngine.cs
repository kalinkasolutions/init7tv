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

    /// <summary>How long to record for; the recording ends by itself after this.</summary>
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

    /// <summary>
    /// Copies somebody's share of a capture into a directory of its own: whole segments up to the
    /// moment they let go of it. The capture itself is left alone, because whoever is still waiting
    /// for it is having it written as this runs. A copy of the bytes, so it costs no encode and no
    /// conversion.
    /// </summary>
    Task<OperationResult<long>> ForkAsync(string fromDirectory, string intoDirectory, TimeSpan upTo);

    void StopAll();

    /// <summary>
    /// Ends any ffmpeg left writing into a directory by a previous run of this app, and forgets what
    /// it knew about them. Returns how many were still going.
    ///
    /// A clean shutdown stops its own captures, so this only ever finds something after a kill that
    /// could not be caught — an OOM, a -9, a machine losing power — where ffmpeg is left running
    /// against a multicast for as long as its -t has to go.
    /// </summary>
    int StopLeftovers(string directory);
}
