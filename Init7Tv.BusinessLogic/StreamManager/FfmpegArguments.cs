using System.Globalization;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;

namespace Init7Tv.BusinessLogic.StreamManager;

/// <summary>
/// Builds the ffmpeg command line. Kept pure and separate so it can be pinned by
/// tests: a change made for one channel is easy to make while quietly breaking
/// the rest of the list.
/// </summary>
public static class FfmpegArguments
{
    private const string DefaultDeinterlaceMode = "yadif:send_field";

    public static string[] Build(
        ChannelDto channel,
        int audioStreamIndex,
        GeneralAppSettingsDto appSettings,
        FfprobeRoot streamInfo,
        bool useMultiCast,
        int segmentSeconds
    )
    {
        var args = new List<string> { "-loglevel", appSettings.FfmpegLogLevel };

        if (useMultiCast)
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

        // A keyframe exactly every segment and nothing else, because segments are
        // cut on keyframes. force_key_frames is a time expression so it does not
        // care about the frame rate; -g is a frame count, and a fixed one is what
        // put a keyframe every 12s on 25fps channels while segments were 6s.
        // Derived from the measured rate and doubled, so it can never fire before
        // the forced keyframe does. scenecut would add unforced ones.
        args.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{segmentSeconds})"]);
        args.AddRange(["-g", KeyframeInterval(streamInfo, segmentSeconds).ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-x264-params", "scenecut=0"]);

        args.AddRange(["-c:v", "libx264"]);
        args.AddRange(["-preset", appSettings.FfmpegPreset]);

        if (streamInfo.IsInterlaced)
        {
            // one output per input frame. Deinterlacing a progressive channel
            // would soften it for nothing, so this is conditional.
            var filter = DeinterlaceFilter(appSettings.FfmpegDeinterlaceMode, streamInfo.IsTopFieldFirst);

            if (filter != null)
            {
                args.AddRange(["-vf", filter]);
            }
        }

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

    /// <summary>
    /// Null when deinterlacing is turned off. Only known values are accepted, so
    /// whatever is in the database cannot turn into a broken filter graph.
    /// </summary>
    private static string? DeinterlaceFilter(string? setting, bool topFieldFirst)
    {
        var parity = topFieldFirst ? 0 : 1;

        // values written before the deinterlacer was selectable named only the mode
        var value = string.IsNullOrWhiteSpace(setting) ? DefaultDeinterlaceMode : setting.Trim();
        if (!value.Contains(':'))
        {
            value = $"yadif:{value}";
        }

        var parts = value.Split(':', 2);
        var (deinterlacer, mode) = (parts[0], parts[1]);

        if (deinterlacer == "none")
        {
            return null;
        }

        if (deinterlacer is not ("yadif" or "bwdif") || mode is not ("send_frame" or "send_field"))
        {
            return $"yadif=mode=send_field:parity={parity}";
        }

        return $"{deinterlacer}=mode={mode}:parity={parity}";
    }

    private static int KeyframeInterval(FfprobeRoot streamInfo, int segmentSeconds)
    {
        var frameRate = streamInfo.GetFrameRate;

        // a rate we could not measure should not shorten the interval
        return frameRate > 0
            ? (int)Math.Round(frameRate * segmentSeconds * 2)
            : 600;
    }
}
