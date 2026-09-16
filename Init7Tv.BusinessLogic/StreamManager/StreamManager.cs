using System.Collections.Concurrent;
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

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine("#EXT-X-TARGETDURATION:6");

        string[] segments;
        int mediaSequenceId;
        lock (stream.PlaylistLock)
        {
            segments = stream.Playlist.ToArray();
            mediaSequenceId = stream.MediaSequenceId;
        }

        sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:{mediaSequenceId}");

        foreach (var segmentName in segments)
        {
            sb.AppendLine("#EXTINF:6.0,");
            sb.AppendLine($"/api/streaming/segment/{stream.StreamId}/{segmentName}");
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
            var segmentStartTime = DateTime.UtcNow;
            var stdout = stream.Ffmpeg.StandardOutput.BaseStream;
            var buffer = new byte[188 * 1024];
            var segmentBuffer = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stdout.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    // ffmpeg closed the pipe: the source is gone, retrying would just spin
                    m_logger.LogWarning("ffmpeg output ended for stream: {StreamId}", stream.StreamId);
                    return;
                }

                segmentBuffer.Write(buffer, 0, read);

                if (!((DateTime.UtcNow - segmentStartTime).TotalSeconds >= 6) || segmentBuffer.Length <= 0)
                {
                    continue;
                }

                var name = $"seg{stream.SegmentIndex++}.ts";
                stream.TsSegments[name] = segmentBuffer.ToArray();

                lock (stream.PlaylistLock)
                {
                    stream.Playlist.Add(name);
                    while (stream.Playlist.Count > 5)
                    {
                        var oldSegment = stream.Playlist[0];
                        stream.Playlist.RemoveAt(0);
                        stream.TsSegments.TryRemove(oldSegment, out _);
                        stream.MediaSequenceId++;
                    }
                }

                segmentBuffer = new MemoryStream();
                segmentStartTime = DateTime.UtcNow;
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

    private Process GetFfmpegProcess(ChannelDto channel, int audioStreamIndex, GeneralAppSettingsDto appSettings)
    {
        var ffmpegArgs = GetFfmpegArgs(channel, audioStreamIndex, appSettings);

        m_logger.LogInformation("starting ffmpeg with args: {FfmpegArgs}", string.Join(' ', ffmpegArgs));

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList quotes each entry, so a source url can never inject extra flags
        foreach (var arg in ffmpegArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return new Process { StartInfo = startInfo };
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
            args.AddRange(["-map", "0:v:0"]);
            args.AddRange(["-g", "300"]);
        }
        else
        {
            args.AddRange(["-i", channel.HlsSource]);
            args.AddRange(["-map", "0:v:0"]);
        }

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