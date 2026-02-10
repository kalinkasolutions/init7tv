using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.Dto;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.StreamManager;

public sealed class StreamManager : IStreamManager, IDisposable
{
    private readonly ILogger<StreamManager> m_logger;
    private readonly IChannelService m_channelService;
    private readonly IStreamEventBus m_streamEventBus;

    private readonly ConcurrentDictionary<string, TvStream> m_streams = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> m_streamLocks = new();

    private readonly Timer m_cleanupTimer;
    private readonly TimeSpan m_streamIdleTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan m_timerDueTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan m_timerPeriod = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private bool m_disposed;


    public StreamManager(
        ILogger<StreamManager> logger,
        IChannelService channelService,
        IStreamEventBus streamEventBus
    )
    {
        m_logger = logger;
        m_channelService = channelService;
        m_streamEventBus = streamEventBus;
        m_cleanupTimer = new Timer(
            _ => CleanupIdleStreams(),
            null,
            m_timerDueTime,
            m_timerPeriod);
    }

    public async Task<OperationResult<StreamDto>> StartStream(Guid channelId, int audioStreamIndex, string userName)
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(StreamManager));
        }

        StopSingleUserStream(userName);

        var channelResult = await m_channelService.GetChannelById(channelId);
        if (!channelResult.IsSuccess)
        {
            return channelResult.MapError<StreamDto>();
        }

        var streamId = GetStreamId($"{channelResult.Value.HlsUrl}_{audioStreamIndex}");
        var startStreamLock = m_streamLocks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));

        await startStreamLock.WaitAsync();

        try
        {
            if (m_streams.TryGetValue(streamId, out var existingStream))
            {
                existingStream.Users.Add(userName);
                m_streamEventBus.Publish(GetCurrentStreams());
                return OperationResult<StreamDto>.Success(StreamDtoMapper.Map(existingStream));
            }

            var streamInfo = await GetFfprobeInfo(channelResult.Value.HlsUrl);

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
                Ffmpeg = GetFfmpegProcess(channelResult.Value.HlsUrl, audioStreamIndex),
                StreamInfo = streamInfo.Value,
                Channel = channelResult.Value,
                Users = [userName]
            };

            try
            {
                stream.Ffmpeg.Start();
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to ffmpeg process: {StreamId}", streamId);
                return OperationResult<StreamDto>.Error("Failed to start stream");
            }

            m_streams.TryAdd(streamId, stream);

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
            });


            return OperationResult<StreamDto>.Success(StreamDtoMapper.Map(stream));
        }
        finally
        {
            startStreamLock.Release();
            m_streamLocks.TryRemove(streamId, out _);
        }
    }

    public OperationResult<string> GetPlaylist(string streamId, string userName)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.Error($"Could not find stream while getting playlist: {streamId}");
        }

        stream.LastAccess[userName] = DateTime.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine("#EXT-X-TARGETDURATION:6");
        sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:{stream.MediaSequenceId}");

        foreach (var segmentName in stream.Playlist)
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
            UserNames = stream.Users.ToArray(),
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
            var buffer = new byte[64 * 1024];
            var segmentBuffer = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stdout.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                {
                    continue;
                }

                segmentBuffer.Write(buffer, 0, read);

                if (!((DateTime.UtcNow - segmentStartTime).TotalSeconds >= 6) || segmentBuffer.Length <= 0)
                {
                    continue;
                }

                var name = $"seg{stream.SegmentIndex++}.ts";
                stream.TsSegments[name] = segmentBuffer.ToArray();

                lock (stream.Playlist)
                {
                    stream.Playlist.Add(name);
                    while (stream.Playlist.Count > 5)
                    {
                        stream.TsSegments.TryRemove(stream.Playlist[0], out _);
                        stream.Playlist.RemoveAt(0);
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

    private Process GetFfmpegProcess(string streamUrl, int audioStreamIndex)
    {
        var ffmpegArgs = $"-loglevel error -i {streamUrl} " +
                         "-map 0:v:0 " +
                         $"-c:v libx264 -preset veryfast -vf yadif=mode=send_frame:parity=auto -pix_fmt yuv420p " +
                         $"-map 0:a:{audioStreamIndex} " +
                         $"-c:a aac -b:a 128k -ac 2 -ar 48000 " +
                         "-f mpegts " +
                         "pipe:1";

        m_logger.LogInformation("starting ffmpeg with args: {FfmegArgs}", ffmpegArgs);

        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    private async Task<OperationResult<FfprobeRoot>> GetFfprobeInfo(string streamUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v quiet -print_format json -show_format -show_streams \"{streamUrl}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

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
        var stream = m_streams.Values.FirstOrDefault(s => s.Users.Contains(userName));
        if (stream == null)
        {
            return;
        }

        stream.Users.Remove(userName);
        if (stream.Users.Count == 0)
        {
            StopStream(stream.StreamId);
        }

        m_streamEventBus.Publish(GetCurrentStreams());
    }

    private void CleanupIdleStreams()
    {
        var now = DateTime.UtcNow;

        foreach (var (streamId, stream) in m_streams)
        {
            foreach (var (userName, lastAccess) in stream.LastAccess)
            {
                if (now - lastAccess > m_streamIdleTimeout)
                {
                    m_logger.LogInformation("User {UserName} stopped streaming", userName);
                    stream.Users.Remove(userName);
                    stream.LastAccess.TryRemove(userName, out _);
                }
            }

            if (!stream.LastAccess.IsEmpty)
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

        try
        {
            m_logger.LogInformation("Stopping stream: {StreamId}", streamId);

            stream.CancellationToken.Cancel();

            if (!stream.Ffmpeg.HasExited)
            {
                stream.Ffmpeg.Kill();
            }

            stream.Ffmpeg.Dispose();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to stop stream: {StreamId}", streamId);
        }
    }
}