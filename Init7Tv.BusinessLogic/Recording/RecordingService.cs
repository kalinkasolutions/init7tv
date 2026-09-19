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
    private readonly ILogger<RecordingService> m_logger;
    private readonly Init7TvOptions m_options;

    public RecordingService(
        IRecordingRepository repository,
        IPlannedRecordingRepository planned,
        RecordingSignal signal,
        IRecordingEngine engine,
        IChannelService channelService,
        ILogger<RecordingService> logger,
        IOptions<Init7TvOptions> options
    )
    {
        m_repository = repository;
        m_planned = planned;
        m_signal = signal;
        m_engine = engine;
        m_channelService = channelService;
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

    public async Task<OperationResult<RecordingFileDto>> GetFileAsync(
        Guid recordingId,
        string userName,
        bool isAdmin
    )
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            // saying "not yours" would say it exists, and there is nothing to gain by it
            return OperationResult<RecordingFileDto>.NotFound("That recording was not found");
        }

        if (!IsFinished(recording))
        {
            return OperationResult<RecordingFileDto>.Invalid("That recording has nothing to play yet");
        }

        var path = ResolveFinalPath(recording);
        if (path == null)
        {
            return OperationResult<RecordingFileDto>.NotFound("The recording file is gone");
        }

        var file = new FileInfo(path);

        return OperationResult<RecordingFileDto>.Success(new RecordingFileDto
        {
            Path = path,
            DownloadName = DownloadName(recording),
            LastModified = file.LastWriteTimeUtc,
            Length = file.Length
        });
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
    /// Whether the file is actually there. Asked of the disk rather than taken from the row, because
    /// files can go without the row hearing about it, and a row that says otherwise offers a play
    /// button that opens an empty player and a download that answers with an error page.
    /// </summary>
    private bool HasFile(RecordingRow recording) =>
        IsFinished(recording) && ResolveFinalPath(recording) != null;

    /// <summary>
    /// The stored path is only trusted after it is shown to sit under the root:
    /// a row that was edited by hand must not be able to read anywhere it likes.
    /// </summary>
    private string? ResolveFinalPath(RecordingRow recording)
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

        var path = RecordingFiles.FinalPath(directory);
        return File.Exists(path) ? path : null;
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
    private static string DownloadName(RecordingRow recording)
    {
        var parts = new[] { recording.ChannelName, recording.Title, recording.SubTitle }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var name = string.Join(" - ", parts);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, ' ');
        }

        name = name.Trim();

        return string.IsNullOrEmpty(name)
            ? RecordingFiles.FinalName
            : $"{name}.mp4";
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
