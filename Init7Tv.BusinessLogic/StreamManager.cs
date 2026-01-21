using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.Dto;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic;

public partial class StreamManager : IStreamManager, IDisposable
{
    private readonly ILogger<StreamManager> m_logger;

    private readonly ConcurrentDictionary<string, TvStream> m_streams = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> m_streamLocks = new();

    private readonly Timer m_cleanupTimer;
    private readonly TimeSpan m_streamIdleTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan m_timerDueTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan m_timerPeriod = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonSerialierOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] CodecsToTranscode = ["mpeg2video"];

    private bool m_disposed;


    public StreamManager(ILogger<StreamManager> logger)
    {
        m_logger = logger;
        m_cleanupTimer = new Timer(
            CleanupIdleStreams,
            null,
            m_timerDueTime,
            m_timerPeriod);
    }

    public async Task<OperationResult<StreamDto>> StartStream(string streamUrl, int audioStreamIndex)
    {
        if (m_disposed)
        {
            throw new ObjectDisposedException(nameof(StreamManager));
        }

        var streamId = GetStreamId($"{streamUrl}_{audioStreamIndex}");
        var startStreamLock = m_streamLocks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));

        await startStreamLock.WaitAsync();

        try
        {
            if (m_streams.TryGetValue(streamId, out var existingStream))
            {
                return OperationResult<StreamDto>.Success(StreamDtoMapper.Map(existingStream));
            }

            var streamInfo = await GetFfprobeInfo(streamUrl);

            if (streamInfo.HasError)
            {
                return OperationResult<StreamDto>.Error($"Failed  to get stream info: {streamUrl}");
            }

            m_logger.LogInformation("starting stream: {StreamId}, videoCodec: {VideoCodec}, available lang: {Languages}",
                streamId,
                streamInfo.Value.GetVideoCodec,
                string.Join(", ", streamInfo.Value.GetLanguages));

            var stream = new TvStream
            {
                StreamId = streamId,
                Ffmpeg = await GetFfmpegProcess(streamUrl, audioStreamIndex, streamInfo.Value),
                StreamInfo = streamInfo.Value
            };

            try
            {
                stream.Ffmpeg.Start();
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to ffmpeg process: {StreamId}", streamId);
            }

            m_streams.TryAdd(streamId, stream);

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

    public OperationResult<string> GetPlaylist(string streamId)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.Error($"Could not find stream while getting playlist: {streamId}");
        }

        stream.LastAccessed = DateTime.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:6");
        sb.AppendLine("#EXT-X-TARGETDURATION:6");
        sb.AppendLine($"#EXT-X-MEDIA-SEQUENCE:{stream.MediaSequenceId}");

        foreach (var segmentName in stream.Playlist)
        {
            sb.AppendLine("#EXTINF:6.0,");
            sb.AppendLine($"/api/segment/{stream.StreamId}/{segmentName}");
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

        GC.SuppressFinalize(this);
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
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Stream loop failed stream: {StreamId}", stream.StreamId);
        }
    }

    private async Task<Process> GetFfmpegProcess(string streamUrl, int audioStreamIndex, FfprobeRoot streamInfo)
    {
        var videoTranscodeOptions = "-c:v copy ";
        var isInterlaced = await DetectInterlacing(streamUrl);

        if (CodecsToTranscode.Contains(streamInfo.GetVideoCodec) || streamInfo.NeedsYuvAdaption || isInterlaced)
        {
            videoTranscodeOptions = "-c:v libx264 -preset fast -crf 23 ";
            if (streamInfo.NeedsYuvAdaption)
            {
                videoTranscodeOptions += "-pix_fmt yuv420p ";
            }

            if (isInterlaced)
            {
                videoTranscodeOptions += "-vf \"yadif\" ";
            }
        }

        if (await DetectInterlacing(streamUrl))
        {
        }

        var ffmpegArgs = $"-i {streamUrl} " +
                         "-map 0:v:0 " +
                         $"-map 0:a:{audioStreamIndex} " +
                         $"{videoTranscodeOptions} " +
                         $"-c:a aac -b:a 128k -ac 2 -ar 48000 " +
                         "-f mpegts " +
                         "pipe:1";

        m_logger.LogInformation("starting ffmpeg with args {FfmegArgs}", ffmpegArgs);

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
            var streamInfo = JsonSerializer.Deserialize<FfprobeRoot>(output, JsonSerialierOptions);
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

    private async Task<bool> DetectInterlacing(string streamUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{streamUrl}\" -vf idet -frames:v 100 -an -f null -",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                m_logger.LogWarning("Failed to start interlacing detection for: {StreamUrl}", streamUrl);
                return false;
            }

            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Finde ALLE Matches und nimm das LETZTE
            var matches = Regex.Matches(stderr, @"Multi frame detection:\s+TFF:\s+(\d+)\s+BFF:\s+(\d+)\s+Progressive:\s+(\d+)");
        
            if (matches.Count > 0)
            {
                // Nimm das letzte Match (das ist die finale Statistik)
                var match = matches[matches.Count - 1];
            
                var tff = int.Parse(match.Groups[1].Value);
                var bff = int.Parse(match.Groups[2].Value);
                var progressive = int.Parse(match.Groups[3].Value);
            
                var interlacedFrames = tff + bff;
                var totalFrames = interlacedFrames + progressive;
            
                var isInterlaced = totalFrames > 0 && (interlacedFrames / (double)totalFrames) > 0.8;
            
                m_logger.LogInformation(
                    "Interlacing detection for {StreamUrl}: TFF={TFF}, BFF={BFF}, Progressive={Progressive}, NeedsDeinterlacing={NeedsDeinterlacing}",
                    streamUrl, tff, bff, progressive, isInterlaced);
            
                return isInterlaced;
            }

            m_logger.LogWarning("Could not parse interlacing detection output for: {StreamUrl}", streamUrl);
            return false;
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to detect interlacing for: {StreamUrl}", streamUrl);
            return false;
        }
    }

    private static string GetStreamId(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLower();
    }

    private void CleanupIdleStreams(object? state)
    {
        var now = DateTime.UtcNow;

        foreach (var (streamId, stream) in m_streams)
        {
            if (now - stream.LastAccessed <= m_streamIdleTimeout)
            {
                continue;
            }

            m_logger.LogInformation("Auto-stopping idle stream: {StreamId}", streamId);
            StopStream(streamId);
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

    [GeneratedRegex(@"Multi frame detection: TFF:\s*(\d+) BFF:\s*(\d+) Progressive:\s*(\d+)")]
    private static partial Regex InterlacingRegex();
}