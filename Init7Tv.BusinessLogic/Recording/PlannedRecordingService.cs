using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;
using Microsoft.Extensions.Options;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class PlannedRecordingService : IPlannedRecordingService
{
    private readonly IPlannedRecordingRepository m_repository;
    private readonly IChannelService m_channelService;
    private readonly RecordingSignal m_signal;
    private readonly Init7TvOptions m_options;

    public PlannedRecordingService(
        IPlannedRecordingRepository repository,
        IChannelService channelService,
        RecordingSignal signal,
        IOptions<Init7TvOptions> options
    )
    {
        m_repository = repository;
        m_channelService = channelService;
        m_signal = signal;
        m_options = options.Value;
    }

    public async Task<OperationResult<PlannedRecordingDto[]>> GetAsync(string userName, bool isAdmin)
    {
        // an admin is answering for the machine rather than for themselves
        var planned = isAdmin
            ? await m_repository.GetAllAsync()
            : await m_repository.GetForUserAsync(userName);

        return OperationResult<PlannedRecordingDto[]>.Success(
            planned.Select(x => ToDto(x, userName)).ToArray());
    }

    public async Task<OperationResult<PlannedRecordingDto>> PlanAsync(string userName, PlannedRecordingDto recording)
    {
        if (recording.ProgrammeId == Guid.Empty)
        {
            return OperationResult<PlannedRecordingDto>.Invalid("The programme was not identified");
        }

        if (recording.EndsAt <= recording.StartsAt)
        {
            return OperationResult<PlannedRecordingDto>.Invalid("The programme ends before it starts");
        }

        // nothing can be recorded from a time that has passed
        if (recording.EndsAt <= DateTime.UtcNow)
        {
            return OperationResult<PlannedRecordingDto>.Invalid("That programme is already over");
        }

        var channel = await m_channelService.GetChannelById(recording.ChannelId);
        if (!channel.IsSuccess)
        {
            return channel.MapError<PlannedRecordingDto>();
        }

        // the channel is the one thing a recorder cannot do without, so it is
        // taken from the list rather than from whatever the browser sent
        var stored = new PlannedRecording
        {
            ProgrammeId = recording.ProgrammeId,
            UserName = userName,
            ChannelId = channel.Value.ChannelId,
            ChannelName = channel.Value.DisplayName,
            CanonicalName = channel.Value.CanonicalName,
            Title = Trimmed(recording.Title, 500),
            SubTitle = Trimmed(recording.SubTitle, 500),
            StartsAt = recording.StartsAt.ToUniversalTime(),
            EndsAt = recording.EndsAt.ToUniversalTime(),
            PlannedAt = DateTime.UtcNow
        };

        await m_repository.AddAsync(stored);

        // One starting in a minute must not wait out a sleep the loop has already committed to
        m_signal.Signal();

        return OperationResult<PlannedRecordingDto>.Success(ToDto(stored, userName));
    }

    /// <summary>
    /// Records what is on a channel now, with no end in mind.
    ///
    /// The same pick a programme makes, so everything that follows — sharing one capture, joining
    /// one already running, stopping by taking the pick back, resuming after a restart — works
    /// without knowing this is any different.
    /// </summary>
    public async Task<OperationResult<PlannedRecordingDto>> RecordNowAsync(string userName, Guid channelId)
    {
        var channel = await m_channelService.GetChannelById(channelId);
        if (!channel.IsSuccess)
        {
            return channel.MapError<PlannedRecordingDto>();
        }

        var now = DateTime.UtcNow;

        var stored = new PlannedRecording
        {
            // The channel names it rather than a new id each time, so two people recording the same
            // channel share one capture exactly as two people picking the same programme do.
            ProgrammeId = OpenEndedProgrammeId(channelId),
            UserName = userName,
            ChannelId = channel.Value.ChannelId,
            ChannelName = channel.Value.DisplayName,
            CanonicalName = channel.Value.CanonicalName,
            Title = Trimmed(channel.Value.DisplayName, 500),
            SubTitle = string.Empty,
            StartsAt = now,

            // far enough out that it is never why one ends; the free space is what stops these
            EndsAt = now + m_options.OpenEndedBackstop,
            PlannedAt = now,
            OpenEnded = true
        };

        await m_repository.AddAsync(stored);

        m_signal.Signal();

        return OperationResult<PlannedRecordingDto>.Success(ToDto(stored, userName));
    }

    /// <summary>
    /// The id an open-ended recording of a channel goes under. Derived from the channel so that
    /// asking twice is one recording, the same way the guide's own id makes one pick of a programme.
    /// </summary>
    private static Guid OpenEndedProgrammeId(Guid channelId) =>
        new(Convert.FromHexString(Hash.GetSha256($"open-ended:{channelId}")[..32]));

    public async Task<OperationResult<bool>> CancelAsync(
        string userName,
        bool isAdmin,
        Guid programmeId,
        string owner
    )
    {
        // not found rather than forbidden, so somebody else's picks cannot be found by asking
        if (!isAdmin && owner != userName)
        {
            return OperationResult<bool>.NotFound("That programme was not planned");
        }

        var removed = await m_repository.RemoveAsync(owner, programmeId);

        if (removed)
        {
            m_signal.Signal();
        }

        return removed
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.NotFound("That programme was not planned");
    }

    private static string Trimmed(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private static PlannedRecordingDto ToDto(PlannedRecording x, string askedBy) => new()
    {
        ProgrammeId = x.ProgrammeId,
        ChannelId = x.ChannelId,
        ChannelName = x.ChannelName,
        CanonicalName = x.CanonicalName,
        Title = x.Title,
        SubTitle = x.SubTitle,
        StartsAt = AsUtc(x.StartsAt),
        EndsAt = AsUtc(x.EndsAt),
        UserName = x.UserName,
        IsMine = x.UserName == askedBy,
        OpenEnded = x.OpenEnded
    };

    /// These are stored in UTC, but the database hands them back with no kind at
    /// all, and a time that reaches the browser unmarked is read there as local.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
