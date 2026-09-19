using Init7Tv.Dto;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.Recording;

public sealed class RecordingEventBus : IRecordingEventBus
{
    private readonly ILogger<RecordingEventBus> m_logger;
    private readonly Lock m_lock = new();
    private event Action<CurrentRecordingDto[]>? RecordingsChanged;

    public RecordingEventBus(ILogger<RecordingEventBus> logger)
    {
        m_logger = logger;
    }

    public IDisposable Subscribe(Action<CurrentRecordingDto[]> handler)
    {
        m_logger.LogInformation("Subscribing to recording event bus");
        lock (m_lock)
        {
            RecordingsChanged += handler;
        }

        return new Unsubscriber(() =>
        {
            lock (m_lock)
            {
                m_logger.LogInformation("Unsubscribing from recording event bus");
                RecordingsChanged -= handler;
            }
        });
    }

    public void Publish(CurrentRecordingDto[] recordings)
    {
        RecordingsChanged?.Invoke(recordings);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly Action m_unsubscribe;

        public Unsubscriber(Action unsubscribe)
        {
            m_unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            m_unsubscribe();
        }
    }
}
