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
            var parity = streamInfo.IsTopFieldFirst ? 0 : 1;
            args.AddRange(["-vf", $"yadif=mode=send_frame:parity={parity}"]);
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

    private static int KeyframeInterval(FfprobeRoot streamInfo, int segmentSeconds)
    {
        var frameRate = streamInfo.GetFrameRate;

        // a rate we could not measure should not shorten the interval
        return frameRate > 0
            ? (int)Math.Round(frameRate * segmentSeconds * 2)
            : 600;
    }
}
