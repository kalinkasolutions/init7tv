using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.Dal.Entities;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class RecordingCoordinator : IRecordingCoordinator
{
    /// <summary>A capture that died is only worth restarting while there is still
    /// programme left to catch.</summary>
    private static readonly TimeSpan WorthResuming = TimeSpan.FromMinutes(1);

    /// <summary>ffmpeg ends itself with -t; this is how long after that we stop waiting.</summary>
    private static readonly TimeSpan OverrunGrace = TimeSpan.FromMinutes(1);

    /// <summary>A source that keeps dropping is a broken channel, not bad luck.</summary>
    private const int MaxParts = 3;

    /// <summary>
    /// How far ahead one pass reads, and so the longest it sleeps in one go. A pick set inside this
    /// window rings the doorbell, so this only bounds the damage from a suspended machine or a
    /// system-clock jump: one late look rather than an arbitrarily long oversleep.
    /// </summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(1);

    // a recording at the default preset runs well under this, and guessing high
    // is the safe direction when the database shares the volume
    private const long BytesPerSecondEstimate = 1_500_000;
    private const long FreeSpaceFloorBytes = 5L * 1024 * 1024 * 1024;

    private readonly ILogger<RecordingCoordinator> m_logger;
    private readonly IRecordingRepository m_recordings;
    private readonly IPlannedRecordingRepository m_planned;
    private readonly IChannelService m_channelService;
    private readonly IAppSettingsService m_appSettingsService;
    private readonly IRecordingEngine m_engine;
    private readonly IRecordingService m_recordingService;
    private readonly IRecordingEventBus m_eventBus;
    private readonly Init7TvOptions m_options;

    public RecordingCoordinator(
        ILogger<RecordingCoordinator> logger,
        IRecordingRepository recordings,
        IPlannedRecordingRepository planned,
        IChannelService channelService,
        IAppSettingsService appSettingsService,
        IRecordingEngine engine,
        IRecordingService recordingService,
        IRecordingEventBus eventBus,
        IOptions<Init7TvOptions> options
    )
    {
        m_logger = logger;
        m_recordings = recordings;
        m_planned = planned;
        m_channelService = channelService;
        m_appSettingsService = appSettingsService;
        m_engine = engine;
        m_recordingService = recordingService;
        m_eventBus = eventBus;
        m_options = options.Value;
    }

    public async Task ReconcileAsync()
    {
        var unfinished = await m_recordings.GetUnfinishedAsync();
        if (unfinished.Length == 0)
        {
            return;
        }

        var settings = await GetSettings();
        var now = DateTime.UtcNow;

        m_logger.LogInformation("Reconciling {Count} recording(s) left over from the last run", unfinished.Length);

        foreach (var group in unfinished.GroupBy(x => x.Directory))
        {
            var rows = group.ToArray();
            var captureId = RecordingFiles.CaptureIdOf(group.Key);

            // nothing survives a restart, so a live one here means the row is stale
            if (captureId != Guid.Empty && m_engine.IsRunning(captureId))
            {
                continue;
            }

            if (now + WorthResuming < rows[0].ScheduledEnd && RecordingFiles.NextPart(group.Key) <= MaxParts)
            {
                await ResumeAsync(rows, settings, now);
                continue;
            }

            await FinalizeGroupAsync(rows, settings, cutShort: true);
        }

        m_eventBus.Publish(await m_recordingService.GetCurrentAsync());
    }

    public async Task<RecordingSweepResult> SweepAsync(DateTime now)
    {
        var settings = await GetSettings();
        var preRoll = PreRoll(settings);
        var postRoll = PostRoll(settings);

        // The window reaches one sleep ahead, because that is when the caller comes back. Reading
        // further would be reading rows this pass cannot act on and the next pass reads again.
        // Nothing is missed: a pick made inside the window rings the doorbell, and one made beyond it
        // is still beyond it when the window is redrawn on the next pass.
        var horizon = now + Horizon;

        await ForkDroppedOutAsync(settings, now);
        await HandleFinishedCapturesAsync(settings);
        await StopUnwantedAsync(settings, now, horizon);
        await StartDueAsync(settings, preRoll, postRoll, now, horizon);

        // Worked out after the starting rather than before it, so a pick this pass has just taken on
        // is counted at the moment it now has to be stopped instead of the start it has already
        // passed. Only what is still ahead counts: a moment left in the past would hand the caller a
        // zero delay and spin on it.
        var next = await NextMomentAsync(preRoll, postRoll, now, horizon);

        m_logger.LogDebug(
            "Swept the recording schedule at {Now} out to {Horizon}, next due {Next}", now, horizon, next);

        // A pass only runs when something could have changed, so this is where the dashboard hears
        // about it rather than on a poll of its own.
        m_eventBus.Publish(await m_recordingService.GetCurrentAsync());

        return new RecordingSweepResult(next, horizon);
    }

    /// <summary>
    /// The earliest moment inside the window that something has to happen: a pick's padded start, or
    /// the point at which a capture that has outstayed its window gets stopped.
    /// </summary>
    private async Task<DateTime?> NextMomentAsync(
        TimeSpan preRoll,
        TimeSpan postRoll,
        DateTime now,
        DateTime horizon
    )
    {
        var moments = new List<DateTime>();

        var claimed = new HashSet<Guid>();
        foreach (var row in await m_recordings.GetUnfinishedAsync())
        {
            claimed.Add(row.ProgrammeId);

            if (row.State == RecordingState.Recording)
            {
                moments.Add(row.ScheduledEnd + OverrunGrace);
            }
        }

        foreach (var plan in await m_planned.GetInWindowAsync(now - postRoll, horizon + preRoll))
        {
            if (!claimed.Contains(plan.ProgrammeId))
            {
                moments.Add(AsUtc(plan.StartsAt) - preRoll);
            }
        }

        var ahead = moments.Where(moment => moment > now && moment <= horizon).ToArray();

        return ahead.Length == 0 ? null : ahead.Min();
    }

    private static TimeSpan PreRoll(GeneralAppSettingsDto settings) =>
        TimeSpan.FromMinutes(Math.Max(settings.RecordingPreRollMinutes, 0));

    private static TimeSpan PostRoll(GeneralAppSettingsDto settings) =>
        TimeSpan.FromMinutes(Math.Max(settings.RecordingPostRollMinutes, 0));

    /// <summary>
    /// Gives whoever let go of a shared capture their share of it: everything up to the moment they
    /// did, cut from the transport stream on disk while the rest of it keeps being written for the
    /// people still waiting. A stream copy, so it costs no encode.
    /// </summary>
    private async Task ForkDroppedOutAsync(GeneralAppSettingsDto settings, DateTime now)
    {
        foreach (var row in await m_recordings.GetUnfinishedAsync())
        {
            var captureId = RecordingFiles.CaptureIdOf(row.Directory);

            // only a viewer who dropped out leaves a row finalizing against a capture still running
            if (row.State != RecordingState.Finalizing || !m_engine.IsRunning(captureId))
            {
                continue;
            }

            var theirs = RecordingFiles.DirectoryFor(m_options.RecordingPath, Guid.NewGuid());
            var upTo = (row.EndedAt ?? now) - (row.StartedAt ?? row.ScheduledStart);

            m_logger.LogInformation("Cutting {UserName}'s {Length} of {Title} out of {Directory}",
                row.UserName, upTo, row.Title, row.Directory);

            var forked = await m_engine.ForkAsync(row.Directory, theirs, upTo);

            await UpdateAsync([row], x =>
            {
                if (forked.IsSuccess)
                {
                    x.Directory = theirs;
                    x.FileSizeBytes = forked.Value;
                    x.State = RecordingState.Interrupted;
                    x.ErrorMessage = "Stopped while it was still recording for somebody else";
                }
                else
                {
                    x.State = RecordingState.Failed;
                    x.ErrorMessage = forked.ErrorMessage ?? "Your part of the recording could not be saved";
                }
            });
        }
    }

    // --- what became of the captures that ended --------------------------

    private async Task HandleFinishedCapturesAsync(GeneralAppSettingsDto settings)
    {
        foreach (var finished in m_engine.TakeFinished())
        {
            var directory = RecordingFiles.DirectoryFor(m_options.RecordingPath, finished.CaptureId);
            var rows = await m_recordings.GetByDirectoryAsync(directory);

            if (rows.Length == 0)
            {
                m_logger.LogWarning("Capture {CaptureId} finished with no rows to update", finished.CaptureId);
                continue;
            }

            var now = DateTime.UtcNow;
            var cutShort = now + WorthResuming < rows[0].ScheduledEnd;

            // ffmpeg also exits 0 when the source simply stops, so how far it got
            // decides whether to go back for the rest, not the exit code
            if (!finished.Stopped && cutShort && RecordingFiles.NextPart(directory) <= MaxParts)
            {
                m_logger.LogWarning(
                    "Capture {CaptureId} ended {Early} early with exit code {ExitCode}, going back for the rest",
                    finished.CaptureId, rows[0].ScheduledEnd - now, finished.ExitCode);

                await ResumeAsync(rows, settings, now);
                continue;
            }

            await FinalizeGroupAsync(rows, settings, cutShort || RecordingFiles.Captures(directory).Length > 1);
        }
    }

    private async Task ResumeAsync(RecordingRow[] rows, GeneralAppSettingsDto settings, DateTime now)
    {
        var channel = await m_channelService.GetChannelById(rows[0].ChannelId);
        if (!channel.IsSuccess)
        {
            await FailAsync(rows, "The channel could not be found");
            return;
        }

        var started = await StartCaptureAsync(rows, channel.Value, settings, rows[0].ScheduledEnd - now);
        if (!started)
        {
            await FinalizeGroupAsync(rows, settings, cutShort: true);
        }
    }

    /// <summary>
    /// Ends a recording. Nothing is converted and nothing is deleted: the transport stream on disk
    /// is what is kept, and it is what gets played, so a viewer part way through one is not cut off
    /// the moment it stops being written. Turning it into an mp4 is the download's business, and
    /// only if somebody asks for one.
    /// </summary>
    private async Task FinalizeGroupAsync(RecordingRow[] rows, GeneralAppSettingsDto settings, bool cutShort)
    {
        var directory = rows[0].Directory;
        var captured = RecordingFiles.Captures(directory).Sum(path => new FileInfo(path).Length);
        var now = DateTime.UtcNow;

        if (captured == 0)
        {
            m_logger.LogError("Nothing was captured in {Directory}", directory);

            await UpdateAsync(rows, row =>
            {
                row.State = RecordingState.Failed;
                row.EndedAt = now;
                row.ErrorMessage = "Nothing was recorded";
            });

            await ForgetPicksAsync(rows);
            return;
        }

        m_logger.LogInformation("Finished {Directory}, {Bytes} bytes{CutShort}",
            directory, captured, cutShort ? " (cut short)" : string.Empty);

        await UpdateAsync(rows, row =>
        {
            row.State = cutShort ? RecordingState.Interrupted : RecordingState.Completed;
            row.EndedAt = now;
            row.FileSizeBytes = captured;
            row.ErrorMessage = cutShort ? "Part of the programme is missing" : string.Empty;
        });

        await ForgetPicksAsync(rows);
    }

    // --- captures that outstayed their window ----------------------------

    /// <summary>
    /// Ends captures that should no longer be running: one that outstayed its window, and one whose
    /// programme nobody wants any more.
    ///
    /// Asking the database rather than being told is what lets taking the pick back stop the
    /// recording: the endpoint only rings the doorbell, and this works out what that means.
    /// </summary>
    private async Task StopUnwantedAsync(GeneralAppSettingsDto settings, DateTime now, DateTime horizon)
    {
        var running = (await m_recordings.GetUnfinishedAsync())
            .Where(row => row.State == RecordingState.Recording)
            .ToArray();

        if (running.Length == 0)
        {
            return;
        }

        var picks = await m_planned.GetInWindowAsync(now - PostRoll(settings), horizon + PreRoll(settings));
        var wanted = picks.Select(plan => plan.ProgrammeId).ToHashSet();

        m_logger.LogDebug("{Running} recording(s) under way, {Picks} pick(s) still wanted: {Wanted}",
            running.Length, picks.Length, string.Join(", ", picks.Select(x => $"{x.UserName}/{x.Title}")));

        foreach (var row in running)
        {
            var captureId = RecordingFiles.CaptureIdOf(row.Directory);
            if (captureId == Guid.Empty || !m_engine.IsRunning(captureId))
            {
                continue;
            }

            if (now >= row.ScheduledEnd + OverrunGrace)
            {
                m_logger.LogWarning("Capture {CaptureId} outran its window, stopping it", captureId);
                m_engine.Stop(captureId);
                continue;
            }

            // Everyone who asked for it has taken the pick back. Stopping keeps what has been
            // captured so far rather than throwing it away: the finalizing that follows turns it
            // into a playable clip of however long it ran.
            if (!wanted.Contains(row.ProgrammeId))
            {
                m_logger.LogInformation(
                    "Nobody is waiting for {Title} any more, stopping capture {CaptureId}",
                    row.Title, captureId);

                m_engine.Stop(captureId);
            }
        }
    }

    // --- picks whose time has come ---------------------------------------

    private async Task StartDueAsync(
        GeneralAppSettingsDto settings,
        TimeSpan preRoll,
        TimeSpan postRoll,
        DateTime now,
        DateTime horizon
    )
    {
        // The window is drawn wide enough for both jobs at once: anything whose padded window is still
        // open, and anything whose padded start falls before the horizon. The pass that works out the
        // next moment reads the same rows.
        var due = (await m_planned.GetInWindowAsync(now - postRoll, horizon + preRoll))
            .Where(plan => now >= AsUtc(plan.StartsAt) - preRoll && now < AsUtc(plan.EndsAt) + postRoll)
            .ToArray();

        if (due.Length == 0)
        {
            return;
        }

        var attempts = await m_recordings.GetLatestAttemptsAsync(due.Select(x => x.ProgrammeId).ToArray());

        var underway = (await m_recordings.GetUnfinishedAsync())
            .Where(row => row.State is RecordingState.Pending or RecordingState.Recording)
            .GroupBy(row => row.ProgrammeId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        // one capture per programme however many people asked for it
        var groups = due
            .GroupBy(plan => plan.ProgrammeId)
            .OrderBy(group => group.Min(plan => plan.StartsAt))
            .ToArray();

        foreach (var group in groups)
        {
            // Somebody picking it after it started joins what is already running. Anything else
            // would encode the same programme twice, which is the one thing sharing a capture
            // exists to avoid.
            if (underway.TryGetValue(group.Key, out var rows))
            {
                await JoinAsync(group.ToArray(), rows, now);
                continue;
            }

            if (AlreadyAttempted(attempts, group))
            {
                continue;
            }

            await StartGroupAsync(group.ToArray(), settings, preRoll, postRoll, now);
        }
    }

    /// <summary>Gives whoever has picked it since a row on the capture already running.</summary>
    private async Task JoinAsync(PlannedRecording[] plans, RecordingRow[] rows, DateTime now)
    {
        var already = rows.Select(row => row.UserName).ToHashSet();
        var newcomers = plans.Where(plan => !already.Contains(plan.UserName)).ToArray();

        if (newcomers.Length == 0)
        {
            return;
        }

        var running = rows[0];

        m_logger.LogInformation("{Names} joined the recording of {Title} already under way",
            string.Join(", ", newcomers.Select(plan => plan.UserName)), running.Title);

        // the same window, the same directory and the same state: it is one capture, and what they
        // get is whatever it ends up holding
        await m_recordings.AddRangeAsync(newcomers.Select(plan => new RecordingRow
        {
            RecordingId = Guid.NewGuid(),
            ProgrammeId = running.ProgrammeId,
            UserName = plan.UserName,
            ChannelId = running.ChannelId,
            ChannelName = running.ChannelName,
            CanonicalName = running.CanonicalName,
            Title = running.Title,
            SubTitle = running.SubTitle,
            ScheduledStart = running.ScheduledStart,
            ScheduledEnd = running.ScheduledEnd,
            StartedAt = running.StartedAt,
            State = running.State,
            Directory = running.Directory,
            CreatedAt = now
        }).ToArray());
    }

    /// <summary>
    /// Whether this programme has been had a go at since it was asked for. A pick made after the
    /// attempt is somebody asking again — having stopped the first one, say — and starts a new one.
    /// </summary>
    private static bool AlreadyAttempted(
        Dictionary<Guid, DateTime> attempts,
        IGrouping<Guid, PlannedRecording> group
    )
    {
        return attempts.TryGetValue(group.Key, out var attemptedAt)
               && attemptedAt >= group.Max(plan => AsUtc(plan.PlannedAt));
    }

    private async Task StartGroupAsync(
        PlannedRecording[] plans,
        GeneralAppSettingsDto settings,
        TimeSpan preRoll,
        TimeSpan postRoll,
        DateTime now
    )
    {
        var scheduledStart = AsUtc(plans[0].StartsAt) - preRoll;
        var scheduledEnd = AsUtc(plans[0].EndsAt) + postRoll;
        var duration = scheduledEnd - now;

        var inTheWay = WhatIsInTheWay(duration);
        if (inTheWay != null)
        {
            // another recording finishing or a disk being cleared usually clears
            // this, so keep trying while there is still programme left to catch.
            // Claiming the programme now would mean never coming back to it.
            if (now + WorthResuming < scheduledEnd)
            {
                return;
            }

            await SkipAsync(NewRows(plans, scheduledStart, scheduledEnd, now), inTheWay);
            return;
        }

        var rows = NewRows(plans, scheduledStart, scheduledEnd, now);
        await m_recordings.AddRangeAsync(rows);

        var channel = await m_channelService.GetChannelById(plans[0].ChannelId);
        if (!channel.IsSuccess)
        {
            await FailAsync(rows, "The channel could not be found");
            return;
        }

        if (!await StartCaptureAsync(rows, channel.Value, settings, duration))
        {
            await FailAsync(rows, "The channel would not start");
        }
    }

    /// <summary>One capture per programme, one row per person who asked for it.</summary>
    private RecordingRow[] NewRows(
        PlannedRecording[] plans,
        DateTime scheduledStart,
        DateTime scheduledEnd,
        DateTime now
    )
    {
        var directory = RecordingFiles.DirectoryFor(m_options.RecordingPath, Guid.NewGuid());

        return plans.Select(plan => new RecordingRow
        {
            RecordingId = Guid.NewGuid(),
            ProgrammeId = plan.ProgrammeId,
            UserName = plan.UserName,
            ChannelId = plan.ChannelId,
            ChannelName = plan.ChannelName,
            CanonicalName = plan.CanonicalName,
            Title = plan.Title,
            SubTitle = plan.SubTitle,
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledEnd,
            State = RecordingState.Pending,
            Directory = directory,
            CreatedAt = now
        }).ToArray();
    }

    /// <summary>Why this cannot start right now, or null when nothing is.</summary>
    private string? WhatIsInTheWay(TimeSpan duration)
    {
        if (m_engine.ActiveCount >= m_options.MaxConcurrentRecordings)
        {
            return $"Already recording {m_engine.ActiveCount} programmes at once";
        }

        return CheckFreeSpace(duration);
    }

    /// <summary>Puts one ffmpeg behind a group of rows and marks them as recording.</summary>
    private async Task<bool> StartCaptureAsync(
        RecordingRow[] rows,
        ChannelDto channel,
        GeneralAppSettingsDto settings,
        TimeSpan duration
    )
    {
        if (duration <= TimeSpan.Zero)
        {
            return false;
        }

        var started = await m_engine.StartAsync(new RecordingRequest
        {
            CaptureId = RecordingFiles.CaptureIdOf(rows[0].Directory),
            Channel = channel,
            Directory = rows[0].Directory,
            Duration = duration,
            Preset = settings.RecordingPreset,
            LogLevel = settings.FfmpegLogLevel
        });

        if (!started.IsSuccess)
        {
            m_logger.LogError("Could not start recording {Title} on {Channel}: {Error}",
                rows[0].Title, channel.CanonicalName, started.ErrorMessage);
            return false;
        }

        var now = DateTime.UtcNow;
        await UpdateAsync(rows, row =>
        {
            row.State = RecordingState.Recording;
            row.StartedAt ??= now;
            row.ErrorMessage = string.Empty;
        });

        return true;
    }

    /// <summary>The message when there is no room, which the page shows rather than saying nothing.</summary>
    private string? CheckFreeSpace(TimeSpan duration)
    {
        try
        {
            Directory.CreateDirectory(m_options.RecordingPath);
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(m_options.RecordingPath)) ?? "/")
                .AvailableFreeSpace;

            var needed = (long)duration.TotalSeconds * BytesPerSecondEstimate + FreeSpaceFloorBytes;

            return free < needed
                ? $"Not enough disk space: {free / 1_000_000_000.0:0.0} GB free"
                : null;
        }
        catch (Exception ex)
        {
            // an unreadable drive is not a reason to refuse to record
            m_logger.LogWarning(ex, "Could not check the free space at {Path}", m_options.RecordingPath);
            return null;
        }
    }

    // --- writing the rows back -------------------------------------------

    private async Task SkipAsync(RecordingRow[] rows, string reason)
    {
        m_logger.LogWarning("Skipping {Title}: {Reason}", rows[0].Title, reason);
        await ForgetPicksAsync(rows);

        foreach (var row in rows)
        {
            row.State = RecordingState.Skipped;
            row.ErrorMessage = reason;
            row.EndedAt = DateTime.UtcNow;
        }

        // never added, because a capture that is only waiting its turn must not
        // claim the programme
        await m_recordings.AddRangeAsync(rows);
    }

    private async Task FailAsync(RecordingRow[] rows, string reason)
    {
        m_logger.LogError("Recording {Title} failed: {Reason}", rows[0].Title, reason);
        await ForgetPicksAsync(rows);

        await UpdateAsync(rows, row =>
        {
            row.State = RecordingState.Failed;
            row.ErrorMessage = reason;
            row.EndedAt = DateTime.UtcNow;
        });
    }

    /// <summary>
    /// Drops the picks behind a recording that has finished, however it finished. A pick is a
    /// request for something to be recorded, and once it has been there is nothing left to wait
    /// for: left behind it sits in the planned list for ever, looking like something that is still
    /// going to happen when the pass will never take it up again.
    /// </summary>
    private async Task ForgetPicksAsync(RecordingRow[] rows)
    {
        foreach (var row in rows)
        {
            await m_planned.RemoveAsync(row.UserName, row.ProgrammeId);
        }
    }

    private async Task UpdateAsync(RecordingRow[] rows, Action<RecordingRow> change)
    {
        foreach (var row in rows)
        {
            change(row);
        }

        await m_recordings.SaveAsync(rows);
    }

    private async Task<GeneralAppSettingsDto> GetSettings()
    {
        var settings = await m_appSettingsService.GetGeneralSettingsAsync();

        // the defaults are the same ones the entity carries, so a missing row
        // means the recorder still behaves rather than stopping
        var general = settings.IsSuccess ? settings.Value : new GeneralAppSettingsDto();

        // a blank preset reaches ffmpeg as a flag with no value and takes the
        // whole command down with it
        if (string.IsNullOrWhiteSpace(general.RecordingPreset))
        {
            general.RecordingPreset = new GeneralAppSettingsDto().RecordingPreset;
        }

        if (string.IsNullOrWhiteSpace(general.FfmpegLogLevel))
        {
            general.FfmpegLogLevel = new GeneralAppSettingsDto().FfmpegLogLevel;
        }

        return general;
    }

    /// Picks were stored in UTC, but Sqlite hands them back with no kind at all.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
