using Init7Tv.BusinessLogic.Recording;

namespace Init7Tv.Services;

/// <summary>
/// Sleeps until the next recording falls due, acts on it, then goes back to sleep.
///
/// It holds no schedule, and neither does anything else in memory. Each pass asks the database what is
/// due and what is coming, so there is nothing that can drift out of step with it — no priming, no
/// eviction, and nothing for the rest of the app to remember when a pick is dropped. That is also what
/// makes a restart, a clock step or a change of summer time harmless: the pass after the event sees
/// exactly what the pass before it saw.
///
/// A pass reads only one sleep ahead rather than the whole table, which is safe in both directions:
/// nothing outside that window can come due before the next pass redraws it, and anything that arrives
/// inside it rings the doorbell. That is all the singleton <see cref="RecordingSignal"/> is — picking a
/// programme rings it, and so does a capture ending, so neither waits out a sleep already committed to.
/// </summary>
public sealed class RecordingScheduler : BackgroundService
{
    // How long to wait after a pass that could not run at all — a database that is not up yet, say.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory m_scopeFactory;
    private readonly IRecordingEngine m_engine;
    private readonly RecordingSignal m_signal;
    private readonly ILogger<RecordingScheduler> m_logger;

    public RecordingScheduler(
        IServiceScopeFactory scopeFactory,
        IRecordingEngine engine,
        RecordingSignal signal,
        ILogger<RecordingScheduler> logger
    )
    {
        m_scopeFactory = scopeFactory;
        m_engine = engine;
        m_signal = signal;
        m_logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await SweepAsync();

            try
            {
                // The answer is of no interest: rung or simply elapsed, what follows is the same — go
                // round and ask the database again.
                await m_signal.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The rows stay as they are: shutdown is measured in seconds and the next start reconciles
        // them, which it has to be able to do after a crash anyway.
        m_engine.StopAll();

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Runs one pass and returns how long to sleep afterwards. A pass that fails outright is logged and
    /// retried shortly; whatever it left undone is found again next time, since the database is the
    /// only record of what is outstanding.
    /// </summary>
    private async Task<TimeSpan> SweepAsync()
    {
        try
        {
            await using var scope = m_scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider
                .GetRequiredService<IRecordingCoordinator>()
                .SweepAsync(DateTime.UtcNow);

            return result.SleepFrom(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "A pass over the recording schedule failed; retrying in {Delay}", RetryDelay);
            return RetryDelay;
        }
    }

    /// <summary>
    /// The one pass that is not on the loop. An exception here must not stop the loop starting: every
    /// recording from then on would depend on it.
    /// </summary>
    private async Task ReconcileAsync()
    {
        try
        {
            await using var scope = m_scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IRecordingCoordinator>().ReconcileAsync();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Reconciling the recordings left over from the last run failed");
        }
    }
}
