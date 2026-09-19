using System.Globalization;
using System.Text;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class RecordingService : IRecordingService
{
    private readonly IRecordingRepository m_repository;
    private readonly IPlannedRecordingRepository m_planned;
    private readonly RecordingSignal m_signal;
    private readonly IRecordingEngine m_engine;
    private readonly IChannelService m_channelService;
    private readonly IRecordingSegmentCache m_segments;
    private readonly ILogger<RecordingService> m_logger;
    private readonly Init7TvOptions m_options;

    public RecordingService(
        IRecordingRepository repository,
        IPlannedRecordingRepository planned,
        RecordingSignal signal,
        IRecordingEngine engine,
        IChannelService channelService,
        IRecordingSegmentCache segments,
        ILogger<RecordingService> logger,
        IOptions<Init7TvOptions> options
    )
    {
        m_repository = repository;
        m_planned = planned;
        m_signal = signal;
        m_engine = engine;
        m_channelService = channelService;
        m_segments = segments;
        m_logger = logger;
        m_options = options.Value;
    }

    public async Task<OperationResult<RecordingDto[]>> GetAsync(string userName, bool isAdmin)
    {
        var recordings = isAdmin
            ? await m_repository.GetAllAsync()
            : await m_repository.GetForUserAsync(userName);

        // who else is on the same capture, and only for the ones still running: it is the one case
        // where stopping cannot simply end it, and the page has to be able to say why
        var running = recordings
            .Where(x => x.State is RecordingState.Pending or RecordingState.Recording)
            .Select(x => x.Directory)
            .Distinct()
            .ToArray();

        var sharers = running.Length == 0
            ? []
            : (await m_repository.GetByDirectoriesAsync(running))
            .GroupBy(x => x.Directory)
            .ToDictionary(group => group.Key, group => group.Select(x => x.UserName).ToArray());

        return OperationResult<RecordingDto[]>.Success(
            recordings.Select(x => ToDto(x, Others(sharers, x), HasFile(x))).ToArray());
    }

    public async Task<OperationResult<string>> GetPlaylistAsync(Guid recordingId, string userName, bool isAdmin)
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<string>.NotFound("That recording was not found");
        }

        var directory = ResolveDirectory(recording);
        if (directory == null)
        {
            return OperationResult<string>.NotFound("That recording is not on disk");
        }

        var parts = RecordingFiles.Captures(directory);
        var running = recording.State is RecordingState.Pending or RecordingState.Recording;
        var segments = m_segments.Segments(directory, parts, finished: !running);

        if (segments.Count == 0)
        {
            // a capture that has only just started has no whole segment yet
            return OperationResult<string>.Invalid("There is nothing to play yet");
        }


        return OperationResult<string>.Text(Playlist(segments, running), "application/vnd.apple.mpegurl");
    }

    /// <summary>
    /// Byte ranges rather than files: every segment is a stretch of a capture that already exists,
    /// so nothing is cut, copied or converted to make one.
    /// </summary>
    private static string Playlist(IReadOnlyList<RecordingSegment> segments, bool running)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        // byte ranges arrived in version 4
        sb.AppendLine("#EXT-X-VERSION:4");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{RecordingEngine.KeyframeSeconds + 1}");
        sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");

        // Every segment is kept rather than a window near the end, so somebody joining an hour in
        // can still start at the beginning. While the capture is still being written the list is
        // left open, which is what has a player come back for the rest of it on its own instead of
        // running out and having to be prodded.
        sb.AppendLine(running ? "#EXT-X-PLAYLIST-TYPE:EVENT" : "#EXT-X-PLAYLIST-TYPE:VOD");

        var previous = (Part: -1, End: -1L);
        foreach (var segment in segments)
        {
            // Each part is its own ffmpeg run, started when the last one was interrupted, so its
            // timestamps begin again at zero. Without being told, a player carries the timeline
            // across the join and every seek past it lands somewhere else entirely.
            if (previous.Part >= 0 && segment.Part != previous.Part)
            {
                sb.AppendLine("#EXT-X-DISCONTINUITY");
            }

            sb.AppendLine($"#EXTINF:{segment.Seconds.ToString("0.000", CultureInfo.InvariantCulture)},");

            // the offset may be left out when a range carries on from the one before, which is the
            // usual case and makes the playlist far smaller once it is thousands of lines long
            sb.AppendLine(segment.Part == previous.Part && segment.Offset == previous.End
                ? $"#EXT-X-BYTERANGE:{segment.Length}"
                : $"#EXT-X-BYTERANGE:{segment.Length}@{segment.Offset}");

            sb.AppendLine($"part/{segment.Part}.ts");
            previous = (segment.Part, segment.Offset + segment.Length);
        }

        if (!running)
        {
            sb.AppendLine("#EXT-X-ENDLIST");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Where the advertising falls in one recording. Scanning is what finds them, so the segments
    /// are asked for first even when only the breaks are wanted.
    /// </summary>
    public async Task<OperationResult<AdBreakMark[]>> GetAdBreaksAsync(
        Guid recordingId,
        string userName,
        bool isAdmin
    )
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<AdBreakMark[]>.NotFound("That recording was not found");
        }

        var directory = ResolveDirectory(recording);
        if (directory == null)
        {
            return OperationResult<AdBreakMark[]>.Success([]);
        }

        var parts = RecordingFiles.Captures(directory);
        var running = recording.State is RecordingState.Pending or RecordingState.Recording;

        m_segments.Segments(directory, parts, finished: !running);

        return OperationResult<AdBreakMark[]>.Success(m_segments.Breaks(directory).ToArray());
    }

    public async Task<OperationResult<RecordingFileDto>> GetPartAsync(
        Guid recordingId,
        int part,
        string userName,
        bool isAdmin
    )
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<RecordingFileDto>.NotFound("That recording was not found");
        }

        var directory = ResolveDirectory(recording);
        var parts = directory == null ? [] : RecordingFiles.Captures(directory);

        if (part < 0 || part >= parts.Length)
        {
            return OperationResult<RecordingFileDto>.NotFound("That part of the recording was not found");
        }

        var file = new FileInfo(parts[part]);

        return OperationResult<RecordingFileDto>.Success(new RecordingFileDto
        {
            Path = file.FullName,
            DownloadName = file.Name,
            LastModified = file.LastWriteTimeUtc,
            Length = file.Length
        });
    }

    public async Task<OperationResult<RecordingDownloadDto>> GetDownloadAsync(
        Guid recordingId,
        bool withoutAds,
        string userName,
        bool isAdmin
    )
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<RecordingDownloadDto>.NotFound("That recording was not found");
        }

        var directory = ResolveDirectory(recording);
        var parts = directory == null ? [] : RecordingFiles.Captures(directory);

        if (parts.Length == 0)
        {
            return OperationResult<RecordingDownloadDto>.NotFound("The recording is no longer on disk");
        }

        var segments = m_segments.Segments(directory!, parts, finished: IsFinished(recording));
        var breaks = withoutAds ? m_segments.Breaks(directory!) : [];

        return OperationResult<RecordingDownloadDto>.Success(new RecordingDownloadDto
        {
            Parts = parts,
            Ranges = Wanted(segments, breaks),
            DownloadName = DownloadName(recording, withoutAds)
        });
    }

    /// <summary>
    /// The stretches worth handing over, which is all of them unless the advertising is being left
    /// out. Cutting on segment boundaries is what makes it free: every one starts on a keyframe, so
    /// the pieces join without anything being decoded or encoded.
    /// </summary>
    private static RecordingSegment[] Wanted(
        IReadOnlyList<RecordingSegment> segments,
        IReadOnlyList<AdBreakMark> breaks
    )
    {
        if (breaks.Count == 0)
        {
            return segments.ToArray();
        }

        var wanted = new List<RecordingSegment>();
        var at = 0.0;

        foreach (var segment in segments)
        {
            var middle = at + segment.Seconds / 2;

            if (!breaks.Any(gap => middle >= gap.StartsAt && middle < gap.EndsAt))
            {
                wanted.Add(segment);
            }

            at += segment.Seconds;
        }

        return wanted.ToArray();
    }

    public async Task<OperationResult<bool>> StopAsync(Guid recordingId, string userName, bool isAdmin)
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<bool>.NotFound("That recording was not found");
        }

        if (recording.State is not (RecordingState.Pending or RecordingState.Recording))
        {
            return OperationResult<bool>.Invalid("That recording is not running");
        }

        // Taking the pick back is what stops it, rather than killing the process here: whether it
        // really stops depends on nobody else still wanting it, and working that out from the
        // database is the pass's job.
        await m_planned.RemoveAsync(recording.UserName, recording.ProgrammeId);

        // Somebody else is still waiting for it, so the capture is not this viewer's to end. Their
        // share of it is cut from what is on disk instead, which the pass does because copying
        // gigabytes is no business of a request. Finalizing against a capture still running is what
        // says so: nothing else leaves a row in that state.
        if (await m_planned.AnyForProgrammeAsync(recording.ProgrammeId))
        {
            recording.State = RecordingState.Finalizing;
            recording.EndedAt = DateTime.UtcNow;
            await m_repository.SaveAsync([recording]);
        }

        m_signal.Signal();

        return OperationResult<bool>.Success(true);
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid recordingId, string userName, bool isAdmin)
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<bool>.NotFound("That recording was not found");
        }

        var directory = recording.Directory;

        // Deleting it is also saying you do not want it, and the pick has to go with it. Left behind,
        // a pick still inside its window is simply due again with no attempt to show for it, and the
        // next pass starts the whole thing over.
        await m_planned.RemoveAsync(recording.UserName, recording.ProgrammeId);

        await m_repository.RemoveAsync(recording);

        // two people who picked the same programme share one capture, so it only
        // goes when the last of them is gone
        var others = await m_repository.GetByDirectoryAsync(directory);
        if (others.Length == 0)
        {
            // deleting one that is still going has to stop it as well. Without
            // this the encode runs on for the rest of the programme against a
            // directory nothing points at any more, holding a core and a slot,
            // and not even freeing the disk until it exits.
            m_engine.Stop(RecordingFiles.CaptureIdOf(directory));

            DeleteDirectory(directory);
        }

        return OperationResult<bool>.Success(true);
    }

    public async Task<CurrentRecordingDto[]> GetCurrentAsync()
    {
        var running = (await m_repository.GetUnfinishedAsync())
            .Where(x => x.State == RecordingState.Recording)
            .ToArray();

        var current = new List<CurrentRecordingDto>();

        // one capture serves everybody who picked it, so the rows sharing a directory are one entry
        foreach (var group in running.GroupBy(x => x.Directory))
        {
            var row = group.First();
            var channel = await m_channelService.GetChannelById(row.ChannelId);

            current.Add(new CurrentRecordingDto
            {
                RecordingId = row.RecordingId,
                ChannelId = row.ChannelId,
                ChannelDisplayName = row.ChannelName,
                ChannelLogo = channel.IsSuccess ? channel.Value.Logo : [],
                Title = row.Title,
                SubTitle = row.SubTitle,
                UserNames = group.Select(x => x.UserName).Order().ToArray(),
                StartedAt = row.StartedAt ?? row.ScheduledStart,
                ScheduledEnd = row.ScheduledEnd
            });
        }

        return current.OrderBy(x => x.ScheduledEnd).ToArray();
    }

    private static bool MayTouch(RecordingRow recording, string userName, bool isAdmin) =>
        isAdmin || recording.UserName == userName;

    private static bool IsFinished(RecordingRow recording) =>
        recording.State is RecordingState.Completed or RecordingState.Interrupted;

    /// <summary>
    /// Whether anything is actually there. Asked of the disk rather than taken from the row, because
    /// files can go without the row hearing about it, and a row that says otherwise offers a play
    /// button that opens an empty player and a download that answers with an error page.
    /// </summary>
    private bool HasFile(RecordingRow recording)
    {
        if (!IsFinished(recording))
        {
            return false;
        }

        var directory = ResolveDirectory(recording);

        return directory != null && RecordingFiles.Captures(directory).Length > 0;
    }

    /// <summary>
    /// The stored path is only trusted after it is shown to sit under the root:
    /// a row that was edited by hand must not be able to read anywhere it likes.
    /// </summary>
    /// <summary>The recording's own directory, once it is shown to be under the root.</summary>
    private string? ResolveDirectory(RecordingRow recording)
    {
        if (string.IsNullOrEmpty(recording.Directory))
        {
            return null;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(m_options.RecordingPath));
        var directory = Path.GetFullPath(recording.Directory);

        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            m_logger.LogError("Recording {RecordingId} points outside the recording root: {Directory}",
                recording.RecordingId, recording.Directory);
            return null;
        }

        return Directory.Exists(directory) ? directory : null;
    }

    private void DeleteDirectory(string directory)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(m_options.RecordingPath));
            var full = Path.GetFullPath(directory);

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                m_logger.LogError("Refusing to delete {Directory}, which is outside the recording root", directory);
                return;
            }

            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to delete {Directory}", directory);
        }
    }

    /// <summary>What the browser saves it as. The only place the title becomes a file name.</summary>
    private static string DownloadName(RecordingRow recording, bool withoutAds)
    {
        var parts = new[] { recording.ChannelName, recording.Title, recording.SubTitle }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var name = string.Join(" - ", parts);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, ' ');
        }

        name = name.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return RecordingFiles.FinalName;
        }

        return withoutAds ? $"{name} (no ads).mp4" : $"{name}.mp4";
    }

    private static string[] Others(Dictionary<string, string[]> sharers, RecordingRow row) =>
        sharers.TryGetValue(row.Directory, out var names)
            ? names.Where(name => name != row.UserName).Order().ToArray()
            : [];

    private static RecordingDto ToDto(RecordingRow x, string[] sharedWith, bool hasFile) => new()
    {
        RecordingId = x.RecordingId,
        ProgrammeId = x.ProgrammeId,
        ChannelId = x.ChannelId,
        ChannelName = x.ChannelName,
        Title = x.Title,
        SubTitle = x.SubTitle,
        ScheduledStart = x.ScheduledStart,
        ScheduledEnd = x.ScheduledEnd,
        StartedAt = x.StartedAt,
        EndedAt = x.EndedAt,
        // a finished recording whose file has gone is neither completed nor failed, it is simply
        // not there any more, and saying so is more use than a size nothing backs up
        State = IsFinished(x) && !hasFile ? "Missing" : x.State.ToString(),
        IsPlayable = hasFile,
        FileSizeBytes = hasFile ? x.FileSizeBytes : 0,
        ErrorMessage = IsFinished(x) && !hasFile ? "The file is no longer on disk" : x.ErrorMessage,
        UserName = x.UserName,
        SharedWith = sharedWith
    };
}
