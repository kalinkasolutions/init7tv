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

            // no -flags low_delay. It tells the decoder to assume frames need no
            // reordering, and these sources are MPEG-2 with B frames, so it
            // emitted them in decode order rather than presentation order and the
            // motion went forward, back, forward. Bisected against the same
            // recording: without it smooth, with it stutters, and +genpts alone
            // is smooth.
            // the stream has already been probed, so ffmpeg does not need to spend
            // five seconds rediscovering it. Measured on SAT.1: first output after
            // 5.4s at the old window against 3.1s at this one, and nothing below
            // this is faster because the floor is how fast udp delivers. Verified
            // that the later audio tracks are still found and mappable.
            args.AddRange(["-analyzeduration", "1000000"]);
            args.AddRange(["-probesize", "2000000"]);
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
            // send_field recovers both fields as frames. parity is told outright
            // because field_order is not dependable on these multicasts, and it
            // comes from the coded frames instead.
            var parity = streamInfo.IsTopFieldFirst ? 0 : 1;
            args.AddRange(["-vf", $"yadif=mode=send_field:parity={parity}"]);
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
