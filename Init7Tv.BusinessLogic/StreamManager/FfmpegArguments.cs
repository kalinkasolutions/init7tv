using System.Globalization;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Subtitles;
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
        int segmentSeconds,
        SubtitleTrack? subtitle = null,
        string? subtitleOutput = null
    )
    {
        var args = new List<string> { "-loglevel", appSettings.FfmpegLogLevel };

        var withSubtitles = subtitle != null && !string.IsNullOrWhiteSpace(subtitleOutput);
        if (withSubtitles)
        {
            // the subtitle output is a named pipe that already exists, and without
            // this ffmpeg refuses to write to anything already there
            args.Add("-y");

            // Teletext carries the whole service, pages of football tables and all.
            // The page is named rather than asking for every subtitle page,
            // because one stream carries several: arte D has German on 150 and
            // French on 888, and asking for both returns them interleaved.
            args.AddRange(["-txt_format", "text"]);
            args.AddRange(["-txt_page", subtitle!.Page.ToString(CultureInfo.InvariantCulture)]);

            // without this every caption is given an end hours away, so none of
            // them ever clears and they pile up on top of each other
            args.Add("-fix_sub_duration");
        }

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

        // A second output of the same process rather than a second ffmpeg, so the
        // captions and the pictures are timed from one reading of the input. Two
        // processes start at different moments and their timelines would have to
        // be reconciled with nothing to reconcile them by.
        if (withSubtitles)
        {
            args.AddRange(["-map", $"0:s:{subtitle!.SubtitleStreamIndex}"]);
            args.AddRange(["-c:s", "webvtt"]);
            args.AddRange(["-f", "webvtt"]);
            args.Add(subtitleOutput!);
        }

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
