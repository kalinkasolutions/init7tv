using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.StreamEventBus;

public interface IStreamEventBus
{
    IDisposable Subscribe(Action<CurrentStreamDto[]> handler);
    void Publish(CurrentStreamDto[] stream);
}