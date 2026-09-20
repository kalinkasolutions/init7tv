using System.Diagnostics;
using System.Text.Json;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.Ffprobe;

public sealed class FfprobeService : IFfprobeService
{
    // codec, frame rate, field order and the audio tracks belong to the channel
    // rather than to the moment, and probing costs about three seconds. Short
    // enough that a channel which does change its tracks recovers on its own.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<FfprobeService> m_logger;
    private readonly IMemoryCache m_cache;

    public FfprobeService(ILogger<FfprobeService> logger, IMemoryCache cache)
    {
        m_logger = logger;
        m_cache = cache;
    }

    public async Task<OperationResult<FfprobeRoot>> ProbeAsync(string streamUrl)
    {
        if (m_cache.TryGetValue(streamUrl, out FfprobeRoot? cached) && cached != null)
        {
            return OperationResult<FfprobeRoot>.Success(cached);
        }

        var result = await Probe(streamUrl);
        if (result.IsSuccess)
        {
            m_cache.Set(streamUrl, result.Value, CacheDuration);
        }

        return result;
    }

    private async Task<OperationResult<FfprobeRoot>> Probe(string streamUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList =
            {
                "-v", "quiet",
                // a live multicast only yields data as it arrives, so bound the
                // probe. One second of frames is enough to read the field order
                // and costs about half of what two did; below this no video
                // frames come back at all and it would be guessing.
                "-analyzeduration", "1000000",
                "-probesize", "2000000",
                "-print_format", "json",
                "-read_intervals", "%+1",
                "-show_entries", "stream:format:frame=media_type,interlaced_frame,top_field_first",
                streamUrl
            },
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
        using var timeout = new CancellationTokenSource(ProbeTimeout);

        string output;
        try
        {
            output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            m_logger.LogError("ffprobe timed out after {Timeout} for url: {StreamUrl}", ProbeTimeout, streamUrl);
            Kill(process);
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
            m_logger.LogError(ex, "Failed to kill ffprobe");
        }
    }
}
