using System.Diagnostics;
using System.Text.Json;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Init7Tv.UnitTest;

/// <summary>
/// Starts a real transcode of every channel and checks the segments it produces
/// are independently playable. This is a diagnostic sweep, not a unit test: it
/// needs ffmpeg on PATH, the Init7 API, a connection that can join the multicast
/// groups, and the better part of an hour.
///
///   dotnet test --filter "FullyQualifiedName~ChannelStreamSweepTest"
///
/// A single channel:
///   dotnet test --filter "FullyQualifiedName~ChannelStreamSweepTest&amp;TestCategory=..."
///   dotnet test --filter "Name~SAT.1"
/// </summary>
[Explicit("Transcodes every channel for real: needs ffmpeg, multicast and several minutes")]
public class ChannelStreamSweepTest
{
    /// <summary>Segments to collect before judging a channel. The first is skipped.</summary>
    private const int SegmentsToCollect = 4;

    private const double ExpectedSegmentSeconds = StreamManager.SegmentSeconds;
    private const double SegmentSecondsTolerance = 0.5;

    private static readonly TimeSpan PerChannelTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly GeneralAppSettingsDto Settings = new()
    {
        BaseDomain = "https://localhost",
        FfmpegPreset = "ultrafast",
        FfmpegLogLevel = "warning"
    };

    // one shared instance, so the channel list and its logos are fetched once
    // rather than per channel
    private static readonly IMemoryCache Cache = new MemoryCache(new MemoryCacheOptions());
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static IChannelService CreateChannelService() =>
        new ChannelService(new HttpClientWrapper(Http), Cache);

    [OneTimeTearDown]
    public void TearDown()
    {
        Cache.Dispose();
        Http.Dispose();
    }

    /// <summary>
    /// Runs during discovery on every `dotnet test`, so it must not reach the
    /// network on a machine that could not run the sweep anyway.
    /// </summary>
    public static IEnumerable<TestCaseData> AllChannels()
    {
        if (!IsOnPath("ffprobe") || !IsOnPath("ffmpeg"))
        {
            return [Unavailable("ffmpeg and ffprobe are not on PATH")];
        }

        try
        {
            var channels = CreateChannelService().GetChannelsAsync().GetAwaiter().GetResult();

            if (!channels.IsSuccess)
            {
                return [Unavailable(channels.ErrorMessage)];
            }

            return channels.Value
                .Select(channel => new TestCaseData(channel)
                    .SetName($"{Sanitise(channel.CanonicalName)}__{Sanitise(channel.DisplayName)}"))
                .ToArray();
        }
        catch (Exception ex)
        {
            return [Unavailable(ex.Message)];
        }
    }

    private static TestCaseData Unavailable(string reason) =>
        new TestCaseData((ChannelDto?)null).SetName($"Channel list unavailable_ {Sanitise(reason)}");

    private static bool IsOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, executable)));
    }

    [TestCaseSource(nameof(AllChannels))]
    public async Task ChannelProducesPlayableSegments(ChannelDto? channel)
    {
        Assert.That(channel, Is.Not.Null, "the channel list could not be loaded, see the case name");

        using var streamManager = new StreamManager(
            NullLogger<StreamManager>.Instance,
            CreateChannelService(),
            new StreamEventBus(NullLogger<StreamEventBus>.Instance),
            // a fresh cache per case, so one channel's probe cannot serve another
            new FfprobeService(NullLogger<FfprobeService>.Instance, new MemoryCache(new MemoryCacheOptions())),
            Options.Create(new Init7TvOptions { UseMultiCast = true }));

        var userName = $"sweep-{Guid.NewGuid():N}";

        var started = await streamManager.StartStream(channel!.ChannelId, audioStreamIndex: 0, userName, Settings);
        Assert.That(started.IsSuccess, Is.True, $"could not start the stream: {started.ErrorMessage}");
        Assert.That(started.Value.Languages, Is.Not.Empty, "no audio track was reported");

        var streamId = started.Value.StreamId;
        var segments = await CollectSegmentsAsync(streamManager, streamId, userName);

        Assert.That(segments, Has.Count.GreaterThanOrEqualTo(SegmentsToCollect),
            $"only {segments.Count} segment(s) arrived within {PerChannelTimeout.TotalSeconds:0}s");

        // the first segment carries the startup transient, judge a later one
        var segment = segments[^1];
        var bytes = streamManager.GetSegment(streamId, segment).Value;

        Assert.That(bytes, Is.Not.Empty, $"{segment} was empty");
        Assert.That(bytes.Length % TsKeyframeDetector.PacketSize, Is.Zero,
            $"{segment} is not a whole number of transport stream packets");
        Assert.That(bytes[0], Is.EqualTo(TsKeyframeDetector.SyncByte),
            $"{segment} does not start on a packet boundary");

        var path = Path.Combine(Path.GetTempPath(), $"init7tv-sweep-{Guid.NewGuid():N}.ts");
        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            var probe = await ProbeAsync(path);

            Assert.Multiple(() =>
            {
                Assert.That(probe.FrameCount, Is.GreaterThan(0), "no video frames were decoded");
                Assert.That(probe.StartsOnKeyframe, Is.True,
                    "the segment does not start on a keyframe, so a joining player cannot decode it");
                Assert.That(probe.MediaSeconds,
                    Is.EqualTo(ExpectedSegmentSeconds).Within(SegmentSecondsTolerance),
                    "the segment does not hold one keyframe interval of media");
                Assert.That(probe.DecodeErrors, Is.Empty,
                    $"the segment does not decode on its own: {probe.DecodeErrors}");
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<List<string>> CollectSegmentsAsync(
        IStreamManager streamManager,
        string streamId,
        string userName)
    {
        var seen = new List<string>();
        var deadline = DateTime.UtcNow + PerChannelTimeout;

        while (DateTime.UtcNow < deadline && seen.Count < SegmentsToCollect)
        {
            // doubles as the keep alive that stops the idle cleanup reaping us
            var playlist = streamManager.GetPlaylist(streamId, userName);
            if (playlist.IsSuccess)
            {
                foreach (var name in ParseSegmentNames(playlist.Value))
                {
                    if (!seen.Contains(name))
                    {
                        seen.Add(name);
                    }
                }
            }

            await Task.Delay(PollInterval);
        }

        return seen;
    }

    private static IEnumerable<string> ParseSegmentNames(string playlist)
    {
        return playlist
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line[(line.LastIndexOf('/') + 1)..]);
    }

    private sealed record ProbeResult(int FrameCount, bool StartsOnKeyframe, double MediaSeconds, string DecodeErrors);

    private static async Task<ProbeResult> ProbeAsync(string path)
    {
        var json = await RunAsync("ffprobe",
        [
            "-hide_banner", "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "frame=pts_time,key_frame",
            "-of", "json", path
        ]);

        var frames = JsonDocument.Parse(json.StandardOutput).RootElement.GetProperty("frames");
        var times = new List<double>();
        var startsOnKeyframe = false;

        for (var i = 0; i < frames.GetArrayLength(); i++)
        {
            var frame = frames[i];
            times.Add(double.Parse(frame.GetProperty("pts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture));

            if (i == 0)
            {
                startsOnKeyframe = frame.GetProperty("key_frame").GetInt32() == 1;
            }
        }

        // the span covers frame starts, so add the duration of the last frame
        var mediaSeconds = 0.0;
        if (times.Count > 1)
        {
            var span = times[^1] - times[0];
            mediaSeconds = span + span / (times.Count - 1);
        }

        var decode = await RunAsync("ffmpeg", ["-hide_banner", "-v", "error", "-i", path, "-f", "null", "-"]);

        return new ProbeResult(times.Count, startsOnKeyframe, mediaSeconds, decode.StandardError.Trim());
    }

    private static async Task<(string StandardOutput, string StandardError)> RunAsync(string fileName, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"could not start {fileName}");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (await stdout, await stderr);
    }

    /// <summary>
    /// The NUnit filter parser cannot cope with spaces in a test name, so names
    /// stay filterable: dotnet test --filter "Name~SAT1.de"
    /// </summary>
    private static string Sanitise(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
    }
}
