using Init7Tv.Dto;
using Microsoft.Extensions.Logging;

namespace Init7Tv.BusinessLogic.StreamEventBus;

public sealed class StreamEventBus : IStreamEventBus
{
    private readonly ILogger<StreamEventBus> m_logger;
    private readonly Lock m_lock = new();
    private event Action<CurrentStreamDto[]>? StreamsChanged;

    public StreamEventBus(ILogger<StreamEventBus> logger)
    {
        m_logger = logger;
    }

    public IDisposable Subscribe(Action<CurrentStreamDto[]> handler)
    {
        m_logger.LogInformation("Subscribing to stream event bus");
        lock (m_lock)
        {
            StreamsChanged += handler;
        }

        return new Unsubscriber(() =>
        {
            lock (m_lock)
            {
                m_logger.LogInformation("Unsubscribing from stream event bus");
                StreamsChanged -= handler;
            }
        });
    }

    public void Publish(CurrentStreamDto[] streams)
    {
        StreamsChanged?.Invoke(streams);
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