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
    private readonly IRecordingEngine m_engine;
    private readonly ILogger<RecordingService> m_logger;
    private readonly Init7TvOptions m_options;

    public RecordingService(
        IRecordingRepository repository,
        IRecordingEngine engine,
        ILogger<RecordingService> logger,
        IOptions<Init7TvOptions> options
    )
    {
        m_repository = repository;
        m_engine = engine;
        m_logger = logger;
        m_options = options.Value;
    }

    public async Task<OperationResult<RecordingDto[]>> GetAsync(string userName, bool isAdmin)
    {
        var recordings = isAdmin
            ? await m_repository.GetAllAsync()
            : await m_repository.GetForUserAsync(userName);

        return OperationResult<RecordingDto[]>.Success(recordings.Select(ToDto).ToArray());
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

        if (!IsPlayable(recording))
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

    public async Task<OperationResult<bool>> DeleteAsync(Guid recordingId, string userName, bool isAdmin)
    {
        var recording = await m_repository.GetByIdAsync(recordingId);
        if (recording == null || !MayTouch(recording, userName, isAdmin))
        {
            return OperationResult<bool>.NotFound("That recording was not found");
        }

        var directory = recording.Directory;
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

    private static bool MayTouch(RecordingRow recording, string userName, bool isAdmin) =>
        isAdmin || recording.UserName == userName;

    private static bool IsPlayable(RecordingRow recording) =>
        recording.State is RecordingState.Completed or RecordingState.Interrupted;

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

    private static RecordingDto ToDto(RecordingRow x) => new()
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
        State = x.State.ToString(),
        IsPlayable = IsPlayable(x) && x.FileSizeBytes > 0,
        FileSizeBytes = x.FileSizeBytes,
        ErrorMessage = x.ErrorMessage,
        UserName = x.UserName
    };
}
