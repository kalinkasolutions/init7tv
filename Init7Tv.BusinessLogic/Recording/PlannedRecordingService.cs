using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class PlannedRecordingService : IPlannedRecordingService
{
    private readonly IPlannedRecordingRepository m_repository;
    private readonly IChannelService m_channelService;
    private readonly RecordingSignal m_signal;

    public PlannedRecordingService(
        IPlannedRecordingRepository repository,
        IChannelService channelService,
        RecordingSignal signal
    )
    {
        m_repository = repository;
        m_channelService = channelService;
        m_signal = signal;
    }

    public async Task<OperationResult<PlannedRecordingDto[]>> GetAsync(string userName, bool isAdmin)
    {
        // an admin is answering for the machine rather than for themselves
        var planned = isAdmin
            ? await m_repository.GetAllAsync()
            : await m_repository.GetForUserAsync(userName);

        return OperationResult<PlannedRecordingDto[]>.Success(planned.Select(ToDto).ToArray());
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

        return OperationResult<PlannedRecordingDto>.Success(ToDto(stored));
    }

    public async Task<OperationResult<bool>> CancelAsync(string userName, Guid programmeId)
    {
        var removed = await m_repository.RemoveAsync(userName, programmeId);

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

    private static PlannedRecordingDto ToDto(PlannedRecording x) => new()
    {
        ProgrammeId = x.ProgrammeId,
        ChannelId = x.ChannelId,
        ChannelName = x.ChannelName,
        CanonicalName = x.CanonicalName,
        Title = x.Title,
        SubTitle = x.SubTitle,
        StartsAt = AsUtc(x.StartsAt),
        EndsAt = AsUtc(x.EndsAt),
        UserName = x.UserName
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
