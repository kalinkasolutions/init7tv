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
        var args = new List<string>();
        args.AddRange(LogLevel(appSettings.FfmpegLogLevel));
        args.AddRange(Input(channel, useMultiCast));
        args.AddRange(Transcode(appSettings.FfmpegPreset, streamInfo, audioStreamIndex, segmentSeconds));
        args.AddRange(["-f", "mpegts"]);
        args.Add("pipe:1");

        return args.ToArray();
    }

    /// <summary>
    /// The same transcode written to a file rather than a pipe.
    ///
    /// Transport stream rather than mp4 because a killed ffmpeg leaves an mp4
    /// with no moov atom, which is an unplayable file: every crash would cost
    /// the whole recording rather than its tail. The remux at the end is what
    /// makes it seekable.
    /// </summary>
    /// <param name="duration">
    /// What -t is set from, and the thing that actually ends the recording. A
    /// scheduler that is wedged or restarting must not leave an ffmpeg running
    /// against a multicast for ever.
    /// </param>
    public static string[] BuildRecording(
        ChannelDto channel,
        int audioStreamIndex,
        string preset,
        string logLevel,
        FfprobeRoot streamInfo,
        bool useMultiCast,
        int keyframeSeconds,
        TimeSpan duration,
        string capturePath
    )
    {
        // without -nostdin an existing output file makes ffmpeg ask on stdin and
        // wait for an answer that is never coming
        var args = new List<string> { "-nostdin", "-y" };
        args.AddRange(LogLevel(logLevel));
        args.AddRange(Input(channel, useMultiCast));
        args.AddRange(Transcode(preset, streamInfo, audioStreamIndex, keyframeSeconds));
        args.AddRange(["-t", ((int)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-f", "mpegts"]);
        args.Add(capturePath);

        return args.ToArray();
    }

    /// <summary>
    /// Wraps the captured transport stream as mp4 so a browser can play and seek
    /// it. A stream copy, so this costs no encode and runs far faster than real
    /// time.
    /// </summary>
    /// <param name="upTo">
    /// Bounds the output, for taking somebody's share of a capture that is still being written. Left
    /// null the whole of it is wrapped.
    /// </param>
    public static string[] BuildRemux(string capturePath, string mp4Path, string logLevel, TimeSpan? upTo = null)
    {
        return
        [
            "-nostdin", "-y",
            .. LogLevel(logLevel),
            // a capture that was cut mid-packet can start without timestamps
            "-fflags", "+genpts",
            "-i", capturePath,
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-c", "copy",
            // aac leaves a transport stream as ADTS and mp4 wants it as ASC.
            // Recent ffmpeg inserts this itself; saying it keeps the command from
            // depending on which ffmpeg the image happens to ship.
            "-bsf:a", "aac_adtstoasc",
            .. Limit(upTo),
            // moves the index to the front, without which a browser downloads the
            // whole file before it can play a second of it
            "-movflags", "+faststart",
            mp4Path
        ];
    }

    /// <summary>Joins the parts of a recording that ffmpeg had to be restarted for.</summary>
    public static string[] BuildConcat(string listPath, string mp4Path, string logLevel, TimeSpan? upTo = null)
    {
        return
        [
            "-nostdin", "-y",
            .. LogLevel(logLevel),
            "-f", "concat",
            // the list names files this process wrote, so it is not reading
            // anywhere the caller did not intend
            "-safe", "0",
            "-i", listPath,
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-c", "copy",
            "-bsf:a", "aac_adtstoasc",
            .. Limit(upTo),
            "-movflags", "+faststart",
            mp4Path
        ];
    }

    private static string[] Limit(TimeSpan? upTo) =>
        upTo is { } limit
            ? ["-t", ((int)limit.TotalSeconds).ToString(CultureInfo.InvariantCulture)]
            : [];

    private static string[] LogLevel(string logLevel) => ["-loglevel", logLevel];

    private static string[] Input(ChannelDto channel, bool useMultiCast)
    {
        if (!useMultiCast)
        {
            return ["-i", channel.HlsSource];
        }

        return
        [
            "-fflags", "+genpts+discardcorrupt",

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
            "-analyzeduration", "1000000",
            "-probesize", "2000000",
            "-i", $"{channel.UdpSource}?fifo_size=1000000&overrun_nonfatal=1"
        ];
    }

    private static string[] Transcode(
        string preset,
        FfprobeRoot streamInfo,
        int audioStreamIndex,
        int keyframeSeconds
    )
    {
        var args = new List<string> { "-map", "0:v:0" };

        // A keyframe exactly every segment and nothing else, because segments are
        // cut on keyframes. force_key_frames is a time expression so it does not
        // care about the frame rate; -g is a frame count, and a fixed one is what
        // put a keyframe every 12s on 25fps channels while segments were 6s.
        // Derived from the measured rate and doubled, so it can never fire before
        // the forced keyframe does. scenecut would add unforced ones.
        args.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{keyframeSeconds})"]);
        args.AddRange(["-g", KeyframeInterval(streamInfo, keyframeSeconds).ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-x264-params", "scenecut=0"]);

        args.AddRange(["-c:v", "libx264"]);
        args.AddRange(["-preset", preset]);

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

        return args.ToArray();
    }

    private static int KeyframeInterval(FfprobeRoot streamInfo, int keyframeSeconds)
    {
        var frameRate = streamInfo.GetFrameRate;

        // a rate we could not measure should not shorten the interval
        return frameRate > 0
            ? (int)Math.Round(frameRate * keyframeSeconds * 2)
            : 600;
    }
}
