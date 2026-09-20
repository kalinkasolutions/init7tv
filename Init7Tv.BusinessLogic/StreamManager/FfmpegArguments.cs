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
        args.AddRange(Transcode(appSettings.FfmpegPreset, streamInfo, [audioStreamIndex], segmentSeconds));
        args.AddRange(["-f", "mpegts"]);
        args.Add("pipe:1");

        return args.ToArray();
    }

    /// <summary>
    /// The same transcode written to a file rather than a pipe.
    ///
    /// Transport stream rather than mp4 because a killed ffmpeg leaves an mp4
    /// with no moov atom, which is an unplayable file: every crash would cost
    /// the whole recording rather than its tail. It is also what is kept, and
    /// what is played; an mp4 is only made when somebody downloads one.
    /// </summary>
    /// <param name="duration">
    /// What -t is set from, and the thing that actually ends the recording. A
    /// scheduler that is wedged or restarting must not leave an ffmpeg running
    /// against a multicast for ever.
    /// </param>
    /// <param name="audioStreamIndexes">
    /// Every track to carry, in the order they should appear. A player with no way to choose takes
    /// the first, so whichever leads is the one most people will hear.
    /// </param>
    public static string[] BuildRecording(
        ChannelDto channel,
        IReadOnlyList<int> audioStreamIndexes,
        string preset,
        string logLevel,
        FfprobeRoot streamInfo,
        bool useMultiCast,
        int keyframeSeconds,
        TimeSpan duration,
        string capturePath,
        int? cueStreamIndex
    )
    {
        // without -nostdin an existing output file makes ffmpeg ask on stdin and
        // wait for an answer that is never coming
        // the cue messages are a data stream ffmpeg has no decoder for, and without this it refuses
        // to carry one at all
        var args = new List<string> { "-nostdin", "-y", "-copy_unknown" };
        args.AddRange(LogLevel(logLevel));
        args.AddRange(Input(channel, useMultiCast));
        args.AddRange(Transcode(preset, streamInfo, audioStreamIndexes, keyframeSeconds));

        // The advertising cues the channel already carries, copied in beside the pictures so that a
        // cue and what it refers to end up on the same clock. Read from the source separately they
        // would have to be lined up against a transcode that starts whenever it starts.
        //
        // Which data stream that is has to be said: these channels carry another one in front of
        // it. Still optional, because a stream can go between the probe and the start.
        if (cueStreamIndex is { } cues)
        {
            args.AddRange(["-map", $"0:d:{cues.ToString(CultureInfo.InvariantCulture)}?", "-c:d", "copy"]);
        }
        args.AddRange(["-t", ((int)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-f", "mpegts"]);
        args.Add(capturePath);

        return args.ToArray();
    }

    /// <summary>
    /// Wraps a transport stream arriving on stdin as an mp4 going out on stdout. A stream copy, so
    /// it costs no encode: the capture is already H.264 and AAC and only the box around it changes.
    ///
    /// Fragmented, because an ordinary mp4 keeps its index at one end or the other and cannot be
    /// written to something that cannot be seeked back into. It plays everywhere the other one does.
    /// </summary>
    public static string[] BuildDownload(string logLevel)
    {
        return
        [
            "-nostdin",
            .. LogLevel(logLevel),
            // the pieces are cut at segment boundaries, so what arrives may start anywhere
            "-fflags", "+genpts",
            "-f", "mpegts", "-i", "pipe:0",
            "-map", "0:v:0",
            // every track the capture holds: choosing one was the recording's job, and it kept them all
            "-map", "0:a",
            "-c", "copy",
            // aac leaves a transport stream as ADTS and mp4 wants it as ASC
            "-bsf:a", "aac_adtstoasc",

            // delay_moov earns its place: empty_moov writes the header before a single packet has
            // been through, which is before aac_adtstoasc has seen an ADTS frame to build the audio
            // config from, so the header went out without one. Chrome works it out from the first
            // frame anyway; Firefox does not, and played the picture in silence.
            "-movflags", "frag_keyframe+empty_moov+default_base_moof+delay_moov",
            "-f", "mp4", "pipe:1"
        ];
    }

    /// <summary>The transport ffmpeg reads, which is also the one worth probing.</summary>
    public static string SourceUrl(ChannelDto channel, bool useMultiCast) =>
        useMultiCast ? channel.UdpSource : channel.HlsSource;

    private static string[] LogLevel(string logLevel) => ["-loglevel", logLevel];

    private static string[] Input(ChannelDto channel, bool useMultiCast)
    {
        var source = SourceUrl(channel, useMultiCast);

        if (!useMultiCast)
        {
            return ["-i", source];
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
            "-i", $"{source}?fifo_size=1000000&overrun_nonfatal=1"
        ];
    }

    private static string[] Transcode(
        string preset,
        FfprobeRoot streamInfo,
        IReadOnlyList<int> audioStreamIndexes,
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

        foreach (var audioStreamIndex in audioStreamIndexes)
        {
            args.AddRange(["-map", $"0:a:{audioStreamIndex}"]);
        }

        // one setting covers every mapped track
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
