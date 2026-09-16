using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Subtitles;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.Mapping;
using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Init7Tv.BusinessLogic.StreamManager;

public sealed class StreamManager : IStreamManager, IDisposable
{
    private readonly ILogger<StreamManager> m_logger;
    private readonly IChannelService m_channelService;
    private readonly IStreamEventBus m_streamEventBus;
    private readonly IMemoryCache m_cache;
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
    private readonly TimeSpan m_ffprobeTimeout = TimeSpan.FromSeconds(20);

    // codec, frame rate, field order and the audio tracks belong to the channel
    // rather than to the moment, and probing costs about three seconds. Short
    // enough that a channel which does change its tracks recovers on its own.
    private readonly TimeSpan m_probeCacheDuration = TimeSpan.FromMinutes(10);

    // a player handed a playlist with no segments retries a couple of times and
    // then gives up, so starting waits until there is something to play
    private readonly TimeSpan m_firstSegmentTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan m_timerDueTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan m_timerPeriod = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private bool m_disposed;


    public StreamManager(
        ILogger<StreamManager> logger,
        IChannelService channelService,
        IStreamEventBus streamEventBus,
        IMemoryCache cache,
        IOptions<Init7TvOptions> options
    )
    {
        m_logger = logger;
        m_channelService = channelService;
        m_streamEventBus = streamEventBus;
        m_cache = cache;
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

            var streamInfo = await GetCachedFfprobeInfo(SourceUrl(channelResult.Value));

            if (streamInfo.HasError)
            {
                return streamInfo.MapError<StreamDto>();
            }

            m_logger.LogInformation("starting stream: {StreamId}, videoCodec: {VideoCodec}, available lang: {Languages}",
                streamId,
                streamInfo.Value.GetVideoCodec,
                streamInfo.Value.GetLanguages);

            // One track. -txt_page configures the teletext decoder, and one input
            // has one decoder, so a second page would need the source opened and
            // decoded again. Channels carrying captions in two languages are rare
            // enough to be worth naming rather than paying that on every stream.
            var subtitle = streamInfo.Value.GetSubtitleTracks.FirstOrDefault();
            var subtitlePipe = subtitle == null ? null : CreateSubtitlePipe(streamId);
            if (subtitle != null && subtitlePipe == null)
            {
                subtitle = null;
            }

            var stream = new TvStream
            {
                StreamId = streamId,
                AudioStreamIndex = audioStreamIndex,
                Ffmpeg = GetFfmpegProcess(channelResult.Value, audioStreamIndex, appSettings, streamInfo.Value,
                    subtitle, subtitlePipe),
                Subtitle = subtitle,
                SubtitlePipe = subtitlePipe,
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

            if (stream.SubtitlePipe != null)
            {
                _ = Task.Run(() => ReadSubtitlesAsync(stream, stream.CancellationToken.Token));
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

    /// <summary>
    /// What a player is pointed at. With subtitles to offer this is a master
    /// naming both renditions, otherwise the media playlist itself, because a
    /// master with one rendition buys nothing and costs a round trip.
    /// </summary>
    public OperationResult<string> GetPlaylist(string streamId, string userName)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.NotFound($"Could not find stream while getting playlist: {streamId}");
        }

        if (stream.Subtitle == null)
        {
            return GetVideoPlaylist(streamId, userName);
        }

        stream.Viewers[userName] = DateTime.UtcNow;

        var master = new StringBuilder();
        master.AppendLine("#EXTM3U");
        master.AppendLine("#EXT-X-VERSION:6");
        master.AppendLine(
            $"#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"subs\",NAME=\"{stream.Subtitle.Label}\"," +
            $"LANGUAGE=\"{stream.Subtitle.Language}\",DEFAULT=NO,AUTOSELECT=NO," +
            $"URI=\"/api/streaming/playlist/subtitles?streamId={streamId}\"");
        master.AppendLine("#EXT-X-STREAM-INF:BANDWIDTH=3000000,SUBTITLES=\"subs\"");
        master.AppendLine($"/api/streaming/playlist/video?streamId={streamId}");

        return OperationResult<string>.Text(master.ToString(), "application/vnd.apple.mpegurl");
    }

    public OperationResult<string> GetVideoPlaylist(string streamId, string userName)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.NotFound($"Could not find stream while getting playlist: {streamId}");
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


    /// <summary>
    /// The captions, cut on the same boundaries as the pictures. It mirrors the
    /// video playlist exactly: a player expects the two to line up segment for
    /// segment, and works out where it is from the media sequence.
    /// </summary>
    public OperationResult<string> GetSubtitlePlaylist(string streamId, string userName)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.NotFound($"Could not find stream while getting subtitles: {streamId}");
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
            sb.AppendLine($"/api/streaming/subtitle/{stream.StreamId}/{Path.ChangeExtension(segment.Name, "vtt")}");
        }

        return OperationResult<string>.Text(sb.ToString(), "application/vnd.apple.mpegurl");
    }

    public OperationResult<string> GetSubtitleSegment(string streamId, string name)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<string>.NotFound($"Could not find stream: {streamId} for subtitles: {name}");
        }

        var tsName = Path.ChangeExtension(name, "ts");

        TvSegment segment;
        lock (stream.PlaylistLock)
        {
            var found = stream.Playlist.FirstOrDefault(x => x.Name == tsName);
            if (found.Name == null)
            {
                return OperationResult<string>.NotFound($"No segment {name} in stream {streamId}");
            }

            segment = found;
        }

        var start = PtsToTime(segment.StartPts);

        WebVttCue[] cues;
        lock (stream.CuesLock)
        {
            cues = stream.Cues.ToArray();
        }

        return OperationResult<string>.Text(
            WebVttSegment.Build(cues, start, start + segment.Duration), "text/vtt");
    }

    public OperationResult<byte[]> GetSegment(string streamId, string name)
    {
        if (!m_streams.TryGetValue(streamId, out var stream))
        {
            return OperationResult<byte[]>.NotFound($"Could not find stream: {streamId} for segment: {name}");
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
            lock (stream.PlaylistLock)
            {
                if (stream.Playlist.Count >= SegmentsBeforeStart)
                {
                    return true;
                }
            }

            if (stream.CancellationToken.IsCancellationRequested || HasExited(stream))
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return false;
    }

    /// <summary>
    /// The read loop disposes the process as soon as ffmpeg dies, and a disposed
    /// Process throws rather than reporting that it exited.
    /// </summary>
    private static bool HasExited(TvStream stream)
    {
        try
        {
            return stream.Ffmpeg.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
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
                // scenecut is off and -g cannot fire first, so every keyframe here is
                // a forced one exactly SegmentDuration of media after the last
                if (segment.Keyframes > 0)
                {
                    PublishSegment(stream, segment);
                    segment.Reset();
                }

                // where this segment sits on the stream clock, which is what a
                // caption is matched against
                segment.StartPts ??= detector.LastKeyframePts;

                // the program tables have to lead the segment. ffmpeg emits them
                // periodically, so cutting at a keyframe left them a third of a
                // second in, and a player that demuxes each segment on its own
                // discards everything before them.
                foreach (var table in detector.ProgramTables)
                {
                    segment.Write(table);
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

        TimeSpan? oldestStart = null;

        lock (stream.PlaylistLock)
        {
            stream.Playlist.Add(new TvSegment(name, duration, segment.StartPts ?? 0));

            while (stream.Playlist.Count > PlaylistLength)
            {
                var oldSegment = stream.Playlist[0];
                stream.Playlist.RemoveAt(0);
                stream.TsSegments.TryRemove(oldSegment.Name, out _);
                stream.MediaSequenceId++;
            }

            if (stream.Playlist.Count > 0)
            {
                oldestStart = PtsToTime(stream.Playlist[0].StartPts);
            }
        }

        DropCuesBefore(stream, oldestStart);
    }

    /// <summary>
    /// A named pipe for ffmpeg's WebVTT. A pipe rather than a file so the captions
    /// arrive as they are spoken; a file would have to be watched for growth and
    /// left behind afterwards.
    /// </summary>
    private string? CreateSubtitlePipe(string streamId)
    {
        var path = Path.Combine(Path.GetTempPath(), $"init7tv-{streamId[..12]}.vtt");

        try
        {
            File.Delete(path);

            using var mkfifo = Process.Start(new ProcessStartInfo("mkfifo", path) { UseShellExecute = false });
            mkfifo?.WaitForExit(TimeSpan.FromSeconds(5));

            if (mkfifo?.ExitCode == 0)
            {
                return path;
            }

            m_logger.LogWarning("Could not create a subtitle pipe at {Path}, continuing without subtitles", path);
        }
        catch (Exception ex)
        {
            // subtitles are worth having but not worth failing a channel over
            m_logger.LogWarning(ex, "Could not create a subtitle pipe at {Path}", path);
        }

        return null;
    }

    /// <summary>
    /// Reads the captions ffmpeg writes for as long as the stream runs. Opening
    /// the pipe blocks until ffmpeg opens its end, which is why this is never
    /// awaited by the start.
    /// </summary>
    private async Task ReadSubtitlesAsync(TvStream stream, CancellationToken cancellationToken)
    {
        var path = stream.SubtitlePipe;
        if (path == null)
        {
            return;
        }

        try
        {
            await using var pipe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);
            using var text = new StreamReader(pipe);

            var reader = new WebVttReader();
            var buffer = new char[2048];

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await text.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                var cues = reader.Read(new string(buffer, 0, read));
                if (cues.Count == 0)
                {
                    continue;
                }

                lock (stream.CuesLock)
                {
                    stream.Cues.AddRange(cues);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // the stream was stopped
        }
        catch (Exception ex)
        {
            m_logger.LogWarning(ex, "Reading subtitles failed for stream: {StreamId}", stream.StreamId);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Could not remove the subtitle pipe {Path}", path);
            }
        }
    }

    /// <summary>The 90 kHz clock the transport stream and the captions share.</summary>
    private static TimeSpan PtsToTime(ulong pts) =>
        TimeSpan.FromTicks((long)pts * TimeSpan.TicksPerSecond / 90_000);

    /// <summary>
    /// Captions older than the playlist itself can never be asked for again, and
    /// a channel left running all day would otherwise collect every word spoken.
    /// </summary>
    private static void DropCuesBefore(TvStream stream, TimeSpan? oldest)
    {
        if (oldest == null)
        {
            return;
        }

        lock (stream.CuesLock)
        {
            stream.Cues.RemoveAll(x => x.End < oldest.Value);
        }
    }

    private sealed class SegmentBuilder
    {
        private MemoryStream m_buffer = new();

        public int Keyframes { get; set; }

        /// <summary>Presentation time of the keyframe this segment opens on.</summary>
        public ulong? StartPts { get; set; }

        public void Write(ReadOnlySpan<byte> packet) => m_buffer.Write(packet);

        public byte[] ToArray() => m_buffer.ToArray();

        public void Reset()
        {
            m_buffer = new MemoryStream();
            Keyframes = 0;
            StartPts = null;
        }
    }

    /// <summary>The transport ffmpeg reads, which is also the one worth probing.</summary>
    private string SourceUrl(ChannelDto channel) =>
        m_options.UseMultiCast ? channel.UdpSource : channel.HlsSource;

    private Process GetFfmpegProcess(
        ChannelDto channel,
        int audioStreamIndex,
        GeneralAppSettingsDto appSettings,
        FfprobeRoot streamInfo,
        SubtitleTrack? subtitle,
        string? subtitlePipe
    )
    {
        var ffmpegArgs = FfmpegArguments.Build(
            channel, audioStreamIndex, appSettings, streamInfo, m_options.UseMultiCast, SegmentSeconds,
            subtitle, subtitlePipe);

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

    private async Task<OperationResult<FfprobeRoot>> GetCachedFfprobeInfo(string streamUrl)
    {
        if (m_cache.TryGetValue(streamUrl, out FfprobeRoot? cached) && cached != null)
        {
            return OperationResult<FfprobeRoot>.Success(cached);
        }

        var result = await GetFfprobeInfo(streamUrl);
        if (result.IsSuccess)
        {
            m_cache.Set(streamUrl, result.Value, m_probeCacheDuration);
        }

        return result;
    }

    private async Task<OperationResult<FfprobeRoot>> GetFfprobeInfo(string streamUrl)
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
                // the teletext pages are only in the stream's extradata, and the
                // page is what tells one language's captions from another's
                "-show_data",
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