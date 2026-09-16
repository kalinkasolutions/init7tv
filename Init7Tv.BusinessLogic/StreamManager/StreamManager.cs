using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    private readonly Init7TvOptions m_options;

    private readonly ConcurrentDictionary<string, TvStream> m_streams = new();
    // one lock per channel + audio track, kept for the lifetime of the manager:
    // removing entries would let two callers start the same stream at once
    private readonly ConcurrentDictionary<string, SemaphoreSlim> m_streamLocks = new();

    private readonly Timer m_cleanupTimer;
    // HLS segments have to start on a keyframe, so the encoder is told to emit
    // one exactly this often and the segmenter cuts on those keyframes
    private const int SegmentSeconds = 6;
    private const int PlaylistLength = 5;
    private static readonly TimeSpan SegmentDuration = TimeSpan.FromSeconds(SegmentSeconds);

    // the encoder emits a keyframe every SegmentDuration, but it is measured in
    // media time and this in wall clock: requiring the full duration rejects the
    // keyframe that lands a few ms early and doubles the segment length
    private static readonly TimeSpan MinSegmentDuration = SegmentDuration / 2;

    private readonly TimeSpan m_streamIdleTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan m_ffprobeTimeout = TimeSpan.FromSeconds(20);
    private readonly TimeSpan m_timerDueTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan m_timerPeriod = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private bool m_disposed;


    public StreamManager(
        ILogger<StreamManager> logger,
        IChannelService channelService,
        IStreamEventBus streamEventBus,
        IOptions<Init7TvOptions> options
    )
    {
        m_logger = logger;
        m_channelService = channelService;
        m_streamEventBus = streamEventBus;
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

        StopSingleUserStream(userName);

        var streamId = GetStreamId($"{channelResult.Value.HlsSource}_{audioStreamIndex}");
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

            var streamInfo = await GetFfprobeInfo(channelResult.Value.HlsSource);

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
                Ffmpeg = GetFfmpegProcess(channelResult.Value, audioStreamIndex, appSettings),
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

            m_streamEventBus.Publish(GetCurrentStreams());

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
            return OperationResult<string>.Error($"Could not find stream while getting playlist: {streamId}");
        }

        stream.Viewers[userName] = DateTime.UtcNow;

        TvSegment[] segments;
        int mediaSequenceId;
        lock (stream.PlaylistLock)
        {
            segments = stream.Playlist.ToArray();
            mediaSequenceId = stream.MediaSequenceId;
        }

        var targetDuration = segments.Length == 0
            ? SegmentSeconds
            : (int)Math.Ceiling(segments.Max(x => x.Duration.TotalSeconds));

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{targetDuration}");
        sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:{mediaSequenceId}");

        foreach (var segment in segments)
        {
            sb.AppendLine($"#EXTINF:{segment.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"/api/streaming/segment/{stream.StreamId}/{segment.Name}");
        }

        return OperationResult<string>.Text(sb.ToString(), "application/vnd.apple.mpegurl");
    }


    public OperationResult<byte[]> GetSegment(string streamId, string name)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<byte[]>.Error($"Could not find stream: {streamId} for segment:  {name}");
        }

        if (stream.TsSegments.TryGetValue(name, out var tsSegment))
        {
            return OperationResult<byte[]>.File(tsSegment, "video/MP2T");
        }

        return OperationResult<byte[]>.NotFound();
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

    private async Task StreamLoopAsync(TvStream stream, CancellationToken cancellationToken)
    {
        try
        {
            var stdout = stream.Ffmpeg.StandardOutput.BaseStream;
            var buffer = new byte[TsKeyframeDetector.PacketSize * 1024];
            var detector = new TsKeyframeDetector();
            var segment = new SegmentBuilder();
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

                carried = ConsumePackets(stream, detector, buffer, carried + read, segment);
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

    /// <summary>
    /// Copies whole transport stream packets into the current segment, starting a
    /// new one on each keyframe. Returns the number of trailing bytes moved to the
    /// front of the buffer, being the start of a packet the next read completes.
    /// </summary>
    private int ConsumePackets(
        TvStream stream,
        TsKeyframeDetector detector,
        byte[] buffer,
        int available,
        SegmentBuilder segment
    )
    {
        var consumed = 0;

        while (available - consumed >= TsKeyframeDetector.PacketSize)
        {
            var packet = buffer.AsSpan(consumed, TsKeyframeDetector.PacketSize);
            consumed += TsKeyframeDetector.PacketSize;

            if (packet[0] != TsKeyframeDetector.SyncByte)
            {
                // ffmpeg writes whole packets, so this only happens after a hiccup
                consumed -= TsKeyframeDetector.PacketSize - 1;
                continue;
            }

            if (detector.IsKeyframeStart(packet))
            {
                if (segment.Keyframes > 0 && DateTime.UtcNow - segment.StartedAt >= MinSegmentDuration)
                {
                    PublishSegment(stream, segment);
                    segment.Reset();
                }

                segment.Keyframes++;
            }

            // anything before the first keyframe cannot be decoded on its own
            if (segment.Keyframes > 0)
            {
                segment.Write(packet);
            }
        }

        var remaining = available - consumed;
        buffer.AsSpan(consumed, remaining).CopyTo(buffer);
        return remaining;
    }

    private void PublishSegment(TvStream stream, SegmentBuilder segment)
    {
        var name = $"seg{stream.SegmentIndex++}.ts";
        stream.TsSegments[name] = segment.ToArray();

        // every keyframe is one forced interval of media, which is what a player
        // needs; wall clock drifts whenever the transcode runs behind realtime
        var duration = segment.Keyframes * SegmentDuration;

        lock (stream.PlaylistLock)
        {
            stream.Playlist.Add(new TvSegment(name, duration));

            while (stream.Playlist.Count > PlaylistLength)
            {
                var oldSegment = stream.Playlist[0];
                stream.Playlist.RemoveAt(0);
                stream.TsSegments.TryRemove(oldSegment.Name, out _);
                stream.MediaSequenceId++;
            }
        }
    }

    private sealed class SegmentBuilder
    {
        private MemoryStream m_buffer = new();

        public int Keyframes { get; set; }
        public DateTime StartedAt { get; private set; } = DateTime.UtcNow;

        public void Write(ReadOnlySpan<byte> packet) => m_buffer.Write(packet);

        public byte[] ToArray() => m_buffer.ToArray();

        public void Reset()
        {
            m_buffer = new MemoryStream();
            Keyframes = 0;
            StartedAt = DateTime.UtcNow;
        }
    }

    private Process GetFfmpegProcess(ChannelDto channel, int audioStreamIndex, GeneralAppSettingsDto appSettings)
    {
        var ffmpegArgs = GetFfmpegArgs(channel, audioStreamIndex, appSettings);

        m_logger.LogInformation("starting ffmpeg with args: {FfmpegArgs}", string.Join(' ', ffmpegArgs));

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList quotes each entry, so a source url can never inject extra flags
        foreach (var arg in ffmpegArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // ffmpeg logs to stderr; without this the configured log level went to the
        // container's console with no indication of which stream produced it
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                m_logger.LogInformation("[ffmpeg {Channel}] {Line}", channel.CanonicalName, e.Data);
            }
        };

        return process;
    }

    private string[] GetFfmpegArgs(ChannelDto channel, int audioStreamIndex, GeneralAppSettingsDto appSettings)
    {
        var args = new List<string> { "-loglevel", appSettings.FfmpegLogLevel };

        if (m_options.UseMultiCast)
        {
            args.AddRange(["-fflags", "+genpts+discardcorrupt"]);
            args.AddRange(["-flags", "low_delay"]);
            args.AddRange(["-analyzeduration", "5000000"]);
            args.AddRange(["-probesize", "10000000"]);
            args.AddRange(["-i", $"{channel.UdpSource}?fifo_size=1000000&overrun_nonfatal=1"]);
        }
        else
        {
            args.AddRange(["-i", channel.HlsSource]);
        }

        args.AddRange(["-map", "0:v:0"]);

        // a keyframe exactly every segment, and nothing else: -g is a frame count
        // so libx264's default 250 lands between the forced ones at 50fps, and
        // scene cuts would add more. Both make segments span uneven media.
        args.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{SegmentSeconds})"]);
        args.AddRange(["-g", "600"]);
        args.AddRange(["-x264-params", "scenecut=0"]);

        args.AddRange(["-c:v", "libx264"]);
        args.AddRange(["-preset", appSettings.FfmpegPreset]);
        args.AddRange(["-vf", "yadif=mode=send_frame:parity=auto"]);
        args.AddRange(["-pix_fmt", "yuv420p"]);
        args.AddRange(["-map", $"0:a:{audioStreamIndex}"]);
        args.AddRange(["-c:a", "aac"]);
        args.AddRange(["-b:a", "128k"]);
        args.AddRange(["-ac", "2"]);
        args.AddRange(["-ar", "48000"]);
        args.AddRange(["-f", "mpegts"]);
        args.Add("pipe:1");

        return args.ToArray();
    }

    private async Task<OperationResult<FfprobeRoot>> GetFfprobeInfo(string streamUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList = { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", streamUrl },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? process;
        try
        {
            // throws rather than returning null when ffprobe is not on PATH
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to start ffprobe for url: {StreamUrl}", streamUrl);
            return OperationResult<FfprobeRoot>.Error("Failed to probe the stream");
        }

        if (process == null)
        {
            m_logger.LogError("Failed to start ffprobe for url: {StreamUrl}", streamUrl);
            return OperationResult<FfprobeRoot>.Error("Failed to probe the stream");
        }

        using var _ = process;

        // runs while holding the stream lock, so a hung probe would block
        // everyone tuning to this channel
        using var timeout = new CancellationTokenSource(m_ffprobeTimeout);

        string output;
        try
        {
            output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            m_logger.LogError("ffprobe timed out after {Timeout} for url: {StreamUrl}", m_ffprobeTimeout, streamUrl);
            KillProcess(process);
            return OperationResult<FfprobeRoot>.Error("Timed out while probing the stream");
        }

        try
        {
            var streamInfo = JsonSerializer.Deserialize<FfprobeRoot>(output, JsonSerializerOptions);
            if (streamInfo == null)
            {
                return OperationResult<FfprobeRoot>.Error("Failed to parse ffprobe json");
            }

            return OperationResult<FfprobeRoot>.Success(streamInfo);
        }
        catch (Exception e)
        {
            m_logger.LogError(e, "Failed to parse ffprobe json for url: {StreamUrl}", streamUrl);
            return OperationResult<FfprobeRoot>.Error("Failed to parse ffprobe json");
        }
    }

    private static string GetStreamId(string input)
    {
        return Hash.GetSha256(input);
    }

    private void StopSingleUserStream(string userName)
    {
        var stream = m_streams.Values.FirstOrDefault(s => s.Viewers.ContainsKey(userName));
        if (stream == null)
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
            KillProcess(stream.Ffmpeg);
            stream.Ffmpeg.Dispose();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to stop stream: {StreamId}", stream.StreamId);
        }
    }

    private void KillProcess(Process process)
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
            m_logger.LogError(ex, "Failed to kill {FileName}", process.StartInfo.FileName);
        }
    }
}