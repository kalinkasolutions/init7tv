using System.Collections.Concurrent;
using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Init7Tv.BusinessLogic.StreamManager;

public sealed class StreamManager : IStreamManager, IDisposable
{
    private readonly ILogger<StreamManager> m_logger;
    private readonly IChannelService m_channelService;
    private readonly IStreamEventBus m_streamEventBus;
    private readonly IFfprobeService m_ffprobeService;
    private readonly Init7TvOptions m_options;

    private readonly ConcurrentDictionary<string, TvStream> m_streams = new();
    // one lock per channel + audio track, kept for the lifetime of the manager:
    // removing entries would let two callers start the same stream at once
    private readonly ConcurrentDictionary<string, SemaphoreSlim> m_streamLocks = new();

    private readonly Timer m_cleanupTimer;
    // HLS segments have to start on a keyframe, so the encoder is told to emit
    // one exactly this often and the segmenter cuts on those keyframes
    // Starting waits for SegmentsBeforeStart of media, and that wait is real
    // time bound, so their product is the floor on how fast a channel can open.
    // The segment length is also the playlist's target duration, which is how
    // often a player reloads it, and shorter segments cost bitrate: one second
    // measured 15% more than two.
    /// <summary>Public so tests cannot drift from the value actually used.</summary>
    public const int SegmentSeconds = 2;
    private const int PlaylistLength = 20;

    // Enough that the player has something to sit back into rather than riding
    // the live edge with nothing in hand; hls.js is told to stay this far back.
    private const int SegmentsBeforeStart = 2;
    private static readonly TimeSpan SegmentDuration = TimeSpan.FromSeconds(SegmentSeconds);

    private readonly TimeSpan m_streamIdleTimeout = TimeSpan.FromSeconds(30);

    // a player handed a playlist with no segments retries a couple of times and
    // then gives up, so starting waits until there is something to play
    private readonly TimeSpan m_firstSegmentTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan m_timerDueTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan m_timerPeriod = TimeSpan.FromSeconds(10);

    private bool m_disposed;


    public StreamManager(
        ILogger<StreamManager> logger,
        IChannelService channelService,
        IStreamEventBus streamEventBus,
        IFfprobeService ffprobeService,
        IOptions<Init7TvOptions> options
    )
    {
        m_logger = logger;
        m_channelService = channelService;
        m_streamEventBus = streamEventBus;
        m_ffprobeService = ffprobeService;
        m_options = options.Value;
        m_cleanupTimer = new Timer(
            _ => CleanupIdleStreams(),
            null,
            m_timerDueTime,
            m_timerPeriod);
    }

    public async Task<OperationResult<StreamDto>> StartStream(
        Guid channelId,
        int audioStreamIndex,
        string userName,
        GeneralAppSettingsDto appSettings
    )
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(StreamManager));
        }

        var channelResult = await m_channelService.GetChannelById(channelId);
        if (!channelResult.IsSuccess)
        {
            return channelResult.MapError<StreamDto>();
        }

        var streamId = GetStreamId($"{channelResult.Value.HlsSource}_{audioStreamIndex}");

        // Leaving a channel stops it once nobody is left, but not when this is
        // the same channel: a second start would otherwise find the caller
        // listed as the only viewer of the stream the first start is still
        // waiting on, stop it, and fail that first request.
        StopSingleUserStream(userName, streamId);
        var startStreamLock = m_streamLocks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));

        await startStreamLock.WaitAsync();

        try
        {
            if (m_streams.TryGetValue(streamId, out var existingStream))
            {
                existingStream.Viewers[userName] = DateTime.UtcNow;
                m_streamEventBus.Publish(GetCurrentStreams());
                return OperationResult<StreamDto>.Success(existingStream.ToDto());
            }

            var streamInfo = await m_ffprobeService.ProbeAsync(
                FfmpegArguments.SourceUrl(channelResult.Value, m_options.UseMultiCast));

            if (streamInfo.HasError)
            {
                return streamInfo.MapError<StreamDto>();
            }

            m_logger.LogInformation("starting stream: {StreamId}, videoCodec: {VideoCodec}, available lang: {Languages}",
                streamId,
                streamInfo.Value.GetVideoCodec,
                streamInfo.Value.GetLanguages);

            var stream = new TvStream
            {
                StreamId = streamId,
                AudioStreamIndex = audioStreamIndex,
                Ffmpeg = GetFfmpegProcess(channelResult.Value, audioStreamIndex, appSettings, streamInfo.Value),
                Segments = new SegmentWindow(PlaylistLength, SegmentDuration),
                StreamInfo = streamInfo.Value,
                Channel = channelResult.Value
            };

            // must be set before the stream is published, or the cleanup timer
            // can reap it in the gap before the first playlist request
            stream.Viewers[userName] = DateTime.UtcNow;

            try
            {
                stream.Ffmpeg.Start();
                stream.Ffmpeg.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to ffmpeg process: {StreamId}", streamId);
                return OperationResult<StreamDto>.Error("Failed to start stream");
            }

            if (!m_streams.TryAdd(streamId, stream))
            {
                m_logger.LogError("Stream {StreamId} was registered concurrently, discarding it", streamId);
                StopProcess(stream);
                return OperationResult<StreamDto>.Error("Failed to start stream");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await StreamLoopAsync(stream, stream.CancellationToken.Token);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Failed to start stream: {StreamId}", streamId);
                }
                finally
                {
                    if (!stream.CancellationToken.IsCancellationRequested)
                    {
                        StopStream(streamId);
                        m_streamEventBus.Publish(GetCurrentStreams());
                    }
                }
            });


            if (!await WaitForInitialSegments(stream))
            {
                // Someone stopped it while it was starting, which happens when the
                // viewer picks another channel before this one is up. Not a
                // failure, and it is already gone, so leave it alone.
                if (stream.CancellationToken.IsCancellationRequested)
                {
                    m_logger.LogInformation("Stream was stopped while starting: {StreamId}", streamId);
                    return OperationResult<StreamDto>.Conflict("The channel was closed before it started");
                }

                m_logger.LogError("No segment was produced for stream: {StreamId}", streamId);
                StopStream(streamId);
                return OperationResult<StreamDto>.Error("The channel did not start streaming");
            }

            m_streamEventBus.Publish(GetCurrentStreams());

            return OperationResult<StreamDto>.Success(stream.ToDto());
        }
        finally
        {
            startStreamLock.Release();
        }
    }

    public OperationResult<string> GetPlaylist(string streamId, string userName)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.NotFound($"Could not find stream while getting playlist: {streamId}");
        }

        stream.Viewers[userName] = DateTime.UtcNow;

        var (segments, mediaSequenceId) = stream.Segments.Snapshot();

        return OperationResult<string>.Text(
            HlsPlaylist.Live(stream.StreamId, segments, mediaSequenceId, SegmentSeconds),
            "application/vnd.apple.mpegurl");
    }


    public OperationResult<byte[]> GetSegment(string streamId, string name)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<byte[]>.NotFound($"Could not find stream: {streamId} for segment: {name}");
        }

        var bytes = stream.Segments.Bytes(name);

        return bytes == null
            ? OperationResult<byte[]>.NotFound()
            : OperationResult<byte[]>.File(bytes, "video/MP2T");
    }

    public CurrentStreamDto[] GetCurrentStreams()
    {
        return m_streams.Values.ToArray().Select(stream => new CurrentStreamDto
        {
            StreamId = stream.StreamId,
            ChannelId = stream.Channel.ChannelId,
            ChannelDisplayName = stream.Channel.DisplayName,
            ChannelLogo = stream.Channel.Logo,
            Language = stream.GetStreamedLanguage,
            UserNames = stream.Viewers.Keys.ToArray(),
        }).ToArray();
    }

    public void StopAllStreams()
    {
        foreach (var streamId in m_streams.Keys.ToArray())
        {
            StopStream(streamId);
        }

        m_streamEventBus.Publish(GetCurrentStreams());
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;

        m_cleanupTimer.Dispose();
        foreach (var stream in m_streams.Values.ToArray())
        {
            StopStream(stream.StreamId);
        }
    }

    /// <summary>False if ffmpeg died or produced too little in time.</summary>
    private async Task<bool> WaitForInitialSegments(TvStream stream)
    {
        var deadline = DateTime.UtcNow + m_firstSegmentTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (stream.Segments.Count >= SegmentsBeforeStart)
            {
                return true;
            }

            if (stream.CancellationToken.IsCancellationRequested || FfmpegProcess.HasExited(stream.Ffmpeg))
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return false;
    }

    private async Task StreamLoopAsync(TvStream stream, CancellationToken cancellationToken)
    {
        try
        {
            var stdout = stream.Ffmpeg.StandardOutput.BaseStream;
            var buffer = new byte[TsKeyframeDetector.PacketSize * 1024];
            var segmenter = new HlsSegmenter();
            var carried = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stdout.ReadAsync(buffer.AsMemory(carried, buffer.Length - carried), cancellationToken);
                if (read == 0)
                {
                    // ffmpeg closed the pipe: the source is gone, retrying would just spin
                    m_logger.LogWarning("ffmpeg output ended for stream: {StreamId}", stream.StreamId);
                    return;
                }

                var cuts = segmenter.Consume(buffer, carried + read);
                foreach (var cut in cuts.Segments)
                {
                    stream.Segments.Add(cut);
                }

                carried = cuts.Carried;
            }
        }
        catch (OperationCanceledException)
        {
            m_logger.LogInformation("Stream was stopped streamId: {StreamId}", stream.StreamId);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Stream loop failed stream: {StreamId}", stream.StreamId);
        }
    }

    private Process GetFfmpegProcess(
        ChannelDto channel,
        int audioStreamIndex,
        GeneralAppSettingsDto appSettings,
        FfprobeRoot streamInfo
    )
    {
        var ffmpegArgs = FfmpegArguments.Build(
            channel, audioStreamIndex, appSettings, streamInfo, m_options.UseMultiCast, SegmentSeconds);

        m_logger.LogInformation("starting ffmpeg with args: {FfmpegArgs}", string.Join(' ', ffmpegArgs));

        return FfmpegProcess.Logged(ffmpegArgs, m_logger, channel.CanonicalName, readOutput: true);
    }

    private static string GetStreamId(string input)
    {
        return Hash.GetSha256(input);
    }

    private void StopSingleUserStream(string userName, string? keepStreamId = null)
    {
        var stream = m_streams.Values.FirstOrDefault(s => s.Viewers.ContainsKey(userName));
        if (stream == null || stream.StreamId == keepStreamId)
        {
            return;
        }

        stream.Viewers.TryRemove(userName, out _);
        if (stream.Viewers.IsEmpty)
        {
            StopStream(stream.StreamId);
        }

        m_streamEventBus.Publish(GetCurrentStreams());
    }

    private void CleanupIdleStreams()
    {
        // runs on a timer thread, where an escaping exception would kill the process
        try
        {
            RemoveIdleStreams();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to clean up idle streams");
        }
    }

    private void RemoveIdleStreams()
    {
        var now = DateTime.UtcNow;

        foreach (var (streamId, stream) in m_streams)
        {
            foreach (var (userName, lastAccess) in stream.Viewers)
            {
                if (now - lastAccess > m_streamIdleTimeout)
                {
                    m_logger.LogInformation("User {UserName} stopped streaming", userName);
                    stream.Viewers.TryRemove(userName, out _);
                }
            }

            if (!stream.Viewers.IsEmpty)
            {
                continue;
            }

            m_logger.LogInformation("Auto-stopping idle stream: {StreamId}", streamId);
            StopStream(streamId);
            m_streamEventBus.Publish(GetCurrentStreams());
        }
    }

    private void StopStream(string streamId)
    {
        if (!m_streams.TryRemove(streamId, out var stream))
        {
            m_logger.LogWarning("Could not remove stream: {StreamId}", streamId);
            return;
        }

        m_logger.LogInformation("Stopping stream: {StreamId}", streamId);
        StopProcess(stream);
    }

    private void StopProcess(TvStream stream)
    {
        try
        {
            stream.CancellationToken.Cancel();
            FfmpegProcess.Kill(stream.Ffmpeg, m_logger);
            stream.Ffmpeg.Dispose();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to stop stream: {StreamId}", stream.StreamId);
        }
    }
}